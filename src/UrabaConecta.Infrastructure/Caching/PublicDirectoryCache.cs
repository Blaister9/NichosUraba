using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;

namespace UrabaConecta.Infrastructure.Caching;

/// <summary>
/// Caché en memoria del proceso con invalidación por generación: en lugar de recorrer y borrar
/// claves, se incrementa un contador que forma parte de la clave, así toda la generación anterior
/// queda inalcanzable de inmediato y expira por su propia vigencia.
/// </summary>
public sealed class PublicDirectoryCache(IMemoryCache cache, IOptions<PublicCacheOptions> options)
    : IPublicDirectoryCache
{
    private long generation;

    public async Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled) return await factory(cancellationToken);

        var scoped = $"public:{Interlocked.Read(ref generation)}:{key}";
        if (cache.TryGetValue(scoped, out T? cached) && cached is not null) return cached;

        var value = await factory(cancellationToken);
        // Un resultado nulo (por ejemplo, una ficha inexistente) no se guarda: evita que un
        // error transitorio o un negocio aún no publicado quede fijado durante la vigencia.
        if (value is not null) cache.Set(scoped, value, settings.TimeToLive);
        return value;
    }

    public void Invalidate() => Interlocked.Increment(ref generation);
}
