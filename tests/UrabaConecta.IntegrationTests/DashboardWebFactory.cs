using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using UrabaConecta.Application;
using UrabaConecta.Contracts;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// La misma aplicación contra PostgreSQL real, pero con el reloj detenido y guardando el SQL que se
/// ejecuta. Lo primero permite afirmar cifras de "hoy" sin que la prueba dependa de la hora a la que
/// alguien la corra —a las 11 p. m. ya no queda ninguna cita futura del día—; lo segundo permite
/// comprobar que quien agrupa y cuenta es PostgreSQL y no la aplicación.
/// </summary>
public class DashboardWebFactory : PostgresWebFactory
{
    /// <summary>
    /// Hoy a las 3:00 p. m. en Bogotá. Se fija el día real y sólo se congela la hora, para que las
    /// ventanas del día local sigan siendo las de la fecha en curso y quede margen tanto hacia atrás
    /// —lo ya atendido— como hacia adelante —lo que viene—.
    /// </summary>
    public static readonly DateTimeOffset Now =
        new(DateTimeOffset.UtcNow.Date.AddHours(20), TimeSpan.Zero);

    public SqlRecorder Sql { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FrozenTime(Now));
            services.AddSingleton(Sql);
            services.AddSingleton<IInterceptor, RecordingInterceptor>();
        });
    }

    private sealed class FrozenTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>
/// La misma aplicación, pero con el resumen operativo averiado. Sirve para comprobar que el panel
/// sobrevive a que esa consulta falle: la persona tiene que conservar sus negocios y sus accesos.
/// </summary>
public sealed class BrokenDashboardWebFactory : DashboardWebFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOwnerDashboardUseCases>();
            services.AddScoped<IOwnerDashboardUseCases, FailingDashboard>();
        });
    }

    private sealed class FailingDashboard : IOwnerDashboardUseCases
    {
        public Task<IReadOnlyList<OwnerDashboardSummaryDto>> SummarizeAsync(IReadOnlyList<MyBusinessDto> mine,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Fallo simulado del resumen operativo.");
    }
}

/// <summary>Las sentencias ejecutadas, en orden, para poder inspeccionar la traducción.</summary>
public sealed class SqlRecorder
{
    private readonly ConcurrentQueue<string> statements = new();

    public IReadOnlyList<string> Statements => [.. statements];

    public void Reset() => statements.Clear();

    internal void Record(string sql) => statements.Enqueue(sql);

    /// <summary>
    /// Las sentencias que leen esa tabla. Se busca "FROM tabla" y no el nombre suelto porque los
    /// enumerados se guardan como texto: la consulta de membresías compara contra 'Appointments' y
    /// aparecería como si consultara la tabla de citas.
    /// </summary>
    public IReadOnlyList<string> All(string table)
        => [.. Statements.Where(x => x.Contains($"FROM {table}", StringComparison.OrdinalIgnoreCase))];
}

public sealed class RecordingInterceptor(SqlRecorder recorder) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        recorder.Record(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
