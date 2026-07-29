namespace UrabaConecta.Application;

/// <summary>
/// Caché de vida corta para las respuestas públicas del directorio y de las fichas publicadas.
/// Sólo admite información que ya es pública para cualquier visitante: nunca paneles privados,
/// datos personales, respuestas dependientes del usuario, tokens ni páginas administrativas.
/// </summary>
public interface IPublicDirectoryCache
{
    Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken);

    /// <summary>
    /// Descarta lo cacheado. Se invoca al publicar, suspender, archivar o editar un negocio,
    /// y al cambiar sus imágenes, para que el directorio no muestre información vencida.
    /// </summary>
    void Invalidate();
}

public sealed class PublicCacheOptions
{
    public const string SectionName = "PublicCache";

    /// <summary>Vigencia en segundos. Se acota a un rango corto para que un cambio se vea pronto.</summary>
    public int SecondsToLive { get; set; } = 60;

    public bool Enabled { get; set; } = true;

    public TimeSpan TimeToLive => TimeSpan.FromSeconds(Math.Clamp(SecondsToLive, 30, 120));
}
