using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Cuenta las sentencias SQL ejecutadas. Vive en el contenedor de servicios de la fábrica, que
/// xunit crea una vez por clase de prueba, así que no hay estado estático compartido entre clases.
/// </summary>
public sealed class QueryCounter
{
    private int executed;

    public int Count => Volatile.Read(ref executed);

    public void Reset() => Interlocked.Exchange(ref executed, 0);

    internal void Increment() => Interlocked.Increment(ref executed);
}

/// <summary>
/// EF Core descubre por sí solo los <see cref="IInterceptor"/> registrados como singleton en el
/// contenedor de la aplicación, de modo que basta con registrarlo para que empiece a contar.
/// </summary>
public sealed class CountingInterceptor(QueryCounter counter) : DbCommandInterceptor
{
    public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        counter.Increment();
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result)
    {
        counter.Increment();
        return base.ScalarExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }
}
