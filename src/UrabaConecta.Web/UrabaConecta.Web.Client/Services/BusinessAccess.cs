using UrabaConecta.Contracts;

namespace UrabaConecta.Web.Client.Services;

/// <summary>
/// Qué puede hacer esta persona en cada uno de sus negocios, resuelto una sola vez por circuito.
///
/// Antes cada pantalla de configuración pedía "Mis negocios" por su cuenta y decidía a mano qué
/// tarjetas enseñar; el resultado era que una droguería veía "Servicios" y "Personal" —secciones
/// que no le sirven de nada— y que una pantalla nueva podía olvidarse de la regla. La regla vive
/// ahora en un sitio, sale del servidor con las capacidades del negocio, y cualquier pantalla la
/// consulta desde aquí.
///
/// La caché no es un adorno de rendimiento: en InteractiveServer el contexto de datos es uno por
/// circuito, y dos componentes de la misma pantalla pidiendo a la vez lo tumban. Guardar la tarea
/// —no el resultado— hace que el segundo espere al primero en lugar de abrir una consulta paralela.
/// </summary>
public sealed class BusinessAccess(IUrabaConectaApi api)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Task<IReadOnlyList<MyBusinessDto>>? pending;

    public async Task<IReadOnlyList<MyBusinessDto>> AllAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Un fallo no se cachea: la siguiente pantalla vuelve a intentarlo en lugar de heredar
            // un error que quizá ya no ocurre.
            pending ??= api.GetMyBusinessesAsync(cancellationToken);
            return await pending;
        }
        catch
        {
            pending = null;
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<MyBusinessDto?> ForAsync(Guid businessId, CancellationToken cancellationToken = default)
        => (await AllAsync(cancellationToken)).SingleOrDefault(x => x.Id == businessId);

    /// <summary>Tras cambiar capacidades o permisos, la próxima consulta vuelve a preguntar.</summary>
    public void Invalidate() => pending = null;
}
