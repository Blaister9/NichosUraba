using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;
using UrabaConecta.Application;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Storage;

/// <summary>
/// Normaliza una imagen cargada: valida la firma binaria real, reescala al lado mayor permitido,
/// recomprime y descarta todos los metadatos (EXIF, XMP, ICC y perfiles IPTC).
/// </summary>
public sealed class ImageSharpImageProcessor : IImageProcessor
{
    private static readonly Configuration SafeConfiguration = CreateConfiguration();

    public NormalizedImage Normalize(ReadOnlyMemory<byte> original, BusinessImageKind kind)
    {
        if (original.Length == 0) throw new DomainException("EMPTY_FILE", "El archivo está vacío.");
        if (original.Length > ImagePolicy.MaximumOriginalBytes)
            throw new DomainException("FILE_TOO_LARGE", "El archivo supera el máximo permitido.");

        using var input = new MemoryStream(original.ToArray(), writable: false);
        Image image;
        IImageFormat format;
        try
        {
            // La configuración sólo registra JPEG, PNG y WebP: SVG, GIF, BMP, TIFF y ejecutables fallan aquí.
            image = Image.Load(SafeConfiguration, input, out format);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException
                                       or NotSupportedException)
        {
            throw new DomainException("UNSUPPORTED_IMAGE",
                "Solo se admiten imágenes JPEG, PNG o WebP.");
        }

        using (image)
        {
            if (!ImagePolicy.AllowedContentTypes.Contains(format.DefaultMimeType))
                throw new DomainException("UNSUPPORTED_IMAGE", "Solo se admiten imágenes JPEG, PNG o WebP.");

            var maximumSide = ImagePolicy.LongestSideFor(kind);
            var longest = Math.Max(image.Width, image.Height);
            if (longest > maximumSide)
            {
                var scale = (double)maximumSide / longest;
                image.Mutate(x => x.Resize(
                    Math.Max(1, (int)Math.Round(image.Width * scale)),
                    Math.Max(1, (int)Math.Round(image.Height * scale))));
            }

            StripMetadata(image);

            // Salida siempre WebP: un logo PNG de 1600 px pesaba cientos de kilobytes para
            // mostrarse a 64 px. WebP conserva la transparencia del PNG.
            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder { Quality = 82 });
            return new(output.ToArray(), ImagePolicy.OutputContentType, ImagePolicy.OutputExtension,
                image.Width, image.Height);
        }
    }

    /// <summary>Elimina toda la metadata para no publicar geolocalización ni datos del dispositivo.</summary>
    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;
        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.XmpProfile = null;
        }
    }

    private static Configuration CreateConfiguration()
    {
        var configuration = new Configuration(
            new JpegConfigurationModule(), new PngConfigurationModule(), new WebpConfigurationModule());
        // Techo defensivo de memoria para entradas maliciosamente grandes ya validadas por tamaño.
        configuration.MemoryAllocator = SixLabors.ImageSharp.Memory.MemoryAllocator.Create(
            new SixLabors.ImageSharp.Memory.MemoryAllocatorOptions { AllocationLimitMegabytes = 128 });
        return configuration;
    }
}
