using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Storage;

public sealed class ObjectStorageOptions
{
    public const string SectionName = "ObjectStorage";
    public const string LocalProvider = "Local";
    public const string S3Provider = "S3";

    /// <summary>"Local" para desarrollo y pruebas, "S3" para Cloudflare R2 u otro compatible.</summary>
    public string Provider { get; set; } = LocalProvider;
    public string? ServiceUrl { get; set; }
    public string? Bucket { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    /// <summary>Base pública de las imágenes. En R2, el dominio del bucket público o el dominio propio.</summary>
    public string? PublicBaseUrl { get; set; }
    public string? Region { get; set; } = "auto";
    /// <summary>Carpeta del proveedor local. Nunca debe apuntar a wwwroot ni al volumen de llaves.</summary>
    public string LocalRootPath { get; set; } = "";

    public bool UsesS3 => string.Equals(Provider, S3Provider, StringComparison.OrdinalIgnoreCase);

    public IReadOnlyList<string> MissingKeys()
    {
        var missing = new List<string>();
        if (!UsesS3)
        {
            if (string.IsNullOrWhiteSpace(LocalRootPath)) missing.Add("ObjectStorage__LocalRootPath");
            return missing;
        }
        void Check(string? value, string key) { if (string.IsNullOrWhiteSpace(value)) missing.Add(key); }
        Check(ServiceUrl, "ObjectStorage__ServiceUrl");
        Check(Bucket, "ObjectStorage__Bucket");
        Check(AccessKey, "ObjectStorage__AccessKey");
        Check(SecretKey, "ObjectStorage__SecretKey");
        Check(PublicBaseUrl, "ObjectStorage__PublicBaseUrl");
        return missing;
    }
}

/// <summary>
/// Almacenamiento en disco para Development, Demo local y pruebas. Las imágenes se sirven por
/// el endpoint <c>/media/{key}</c>, nunca desde wwwroot del contenedor.
/// </summary>
public sealed class LocalObjectStorage : IObjectStorage
{
    private readonly string _root;

    public LocalObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        var configured = options.Value.LocalRootPath;
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), "urabaconecta-media")
            : configured);
        Directory.CreateDirectory(_root);
    }

    public string Provider => ObjectStorageOptions.LocalProvider;

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType,
        CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content.ToArray(), cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        return Task.FromResult<Stream?>(File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var path = Resolve(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string PublicUrl(string key) => "/media/" + key;

    public Task<ObjectStorageHealth> CheckHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(Directory.Exists(_root)
            ? new ObjectStorageHealth(true, $"Local: {_root}")
            : new ObjectStorageHealth(false, $"No existe la carpeta {_root}."));

    /// <summary>Impide que una clave manipulada escape de la carpeta raíz.</summary>
    private string Resolve(string key)
    {
        var candidate = Path.GetFullPath(Path.Combine(_root, key.Replace('\\', '/')));
        if (!candidate.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException("La clave de almacenamiento no es válida.");
        return candidate;
    }
}

/// <summary>Almacenamiento compatible con S3. Verificado contra el modo S3 de Cloudflare R2.</summary>
public sealed class S3CompatibleObjectStorage : IObjectStorage, IDisposable
{
    private readonly IAmazonS3 _client;
    private readonly ObjectStorageOptions _options;

    public S3CompatibleObjectStorage(IOptions<ObjectStorageOptions> options)
    {
        _options = options.Value;
        var missing = _options.MissingKeys();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "Faltan variables de almacenamiento de objetos: " + string.Join(", ", missing));
        var config = new AmazonS3Config
        {
            ServiceURL = _options.ServiceUrl,
            // R2 no admite direccionamiento por subdominio de bucket.
            ForcePathStyle = true,
            AuthenticationRegion = string.IsNullOrWhiteSpace(_options.Region) ? "auto" : _options.Region,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        };
        _client = new AmazonS3Client(new BasicAWSCredentials(_options.AccessKey, _options.SecretKey), config);
    }

    public string Provider => ObjectStorageOptions.S3Provider;

    public async Task PutAsync(string key, ReadOnlyMemory<byte> content, string contentType,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content.ToArray(), writable: false);
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _options.Bucket, Key = key, InputStream = stream, ContentType = contentType,
            DisablePayloadSigning = true
        }, cancellationToken);
    }

    public async Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetObjectAsync(_options.Bucket, key, cancellationToken);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
        => _client.DeleteObjectAsync(_options.Bucket, key, cancellationToken);

    public string PublicUrl(string key) => $"{_options.PublicBaseUrl!.TrimEnd('/')}/{key}";

    public async Task<ObjectStorageHealth> CheckHealthAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.GetBucketLocationAsync(_options.Bucket, cancellationToken);
            return new(true, $"S3: {_options.Bucket}");
        }
        catch (Exception ex)
        {
            return new(false, ex.GetType().Name);
        }
    }

    public void Dispose() => _client.Dispose();
}
