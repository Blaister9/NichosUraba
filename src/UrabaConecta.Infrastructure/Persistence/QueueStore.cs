using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Domain;
using UrabaConecta.Contracts;
using Npgsql;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class QueueStore(AppDbContext db) : IQueueStore
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct)
        => new EfTransaction(await db.Database.BeginTransactionAsync(ct));

    public Task<QueueDefinition?> GetPublicDefinitionAsync(string slug, CancellationToken ct)
        => db.QueueDefinitions.SingleOrDefaultAsync(q => q.IsActive && q.IsEnabled &&
            db.Businesses.Any(b => b.Id == q.BusinessId && b.Slug == slug && b.IsPublished &&
                b.Status == BusinessStatus.Active) &&
            db.BusinessModules.Any(m => m.BusinessId == q.BusinessId &&
                m.Module == BusinessModuleKind.VirtualQueues && m.IsEnabled), ct);

    public async Task<(QueueDefinition Definition, Business Business)?> GetPublicContextAsync(string slug, CancellationToken ct)
    {
        var row = await (from q in db.QueueDefinitions
            join b in db.Businesses on q.BusinessId equals b.Id
            where q.IsActive && q.IsEnabled && b.Slug == slug && b.IsPublished && b.Status == BusinessStatus.Active
                && db.BusinessModules.Any(m => m.BusinessId == b.Id && m.Module == BusinessModuleKind.VirtualQueues && m.IsEnabled)
            select new { q, b }).SingleOrDefaultAsync(ct);
        return row is null ? null : (row.q, row.b);
    }

    public async Task<(QueueTicket Ticket, QueueDefinition Definition, Business Business)?> FindTicketAsync(string hash, CancellationToken ct)
    {
        var row = await (from t in db.QueueTickets
            join s in db.QueueSessions on t.QueueSessionId equals s.Id
            join q in db.QueueDefinitions on s.QueueDefinitionId equals q.Id
            join b in db.Businesses on t.BusinessId equals b.Id
            where t.PublicCodeHash == hash
            select new { t, q, b }).SingleOrDefaultAsync(ct);
        return row is null ? null : (row.t, row.q, row.b);
    }

    public Task<QueueDefinition?> GetDefinitionAsync(Guid businessId, CancellationToken ct)
        => db.QueueDefinitions.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.IsActive, ct);
    public Task<QueueSession?> GetCurrentSessionAsync(Guid businessId, CancellationToken ct)
        => db.QueueSessions.SingleOrDefaultAsync(x => x.BusinessId == businessId &&
            (x.Status == QueueSessionStatus.Open || x.Status == QueueSessionStatus.Paused), ct);
    public async Task<QueueSession?> LockCurrentSessionAsync(Guid businessId, CancellationToken ct)
        => await db.QueueSessions.FromSqlInterpolated($"""
            SELECT * FROM queue_sessions
            WHERE "BusinessId" = {businessId} AND "Status" IN ('Open','Paused')
            FOR UPDATE
            """).SingleOrDefaultAsync(ct);
    public Task<QueueTicket?> GetTicketAsync(Guid businessId, Guid ticketId, CancellationToken ct)
        => db.QueueTickets.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == ticketId, ct);
    public async Task<IReadOnlyList<QueueTicket>> GetSessionTicketsAsync(Guid businessId, Guid sessionId, CancellationToken ct)
        => await db.QueueTickets.Where(x => x.BusinessId == businessId && x.QueueSessionId == sessionId)
            .OrderBy(x => x.Number).ToListAsync(ct);
    public Task<int> CountWaitingAsync(Guid businessId, Guid sessionId, CancellationToken ct)
        => db.QueueTickets.CountAsync(x => x.BusinessId == businessId && x.QueueSessionId == sessionId &&
            x.Status == QueueTicketStatus.Waiting, ct);
    public Task<int> CountActiveAsync(Guid businessId, Guid sessionId, CancellationToken ct)
        => db.QueueTickets.CountAsync(x => x.BusinessId == businessId && x.QueueSessionId == sessionId &&
            (x.Status == QueueTicketStatus.Waiting || x.Status == QueueTicketStatus.Called ||
             x.Status == QueueTicketStatus.InService || x.Status == QueueTicketStatus.Skipped), ct);
    public Task<QueueTicket?> GetNextWaitingAsync(Guid businessId, Guid sessionId, CancellationToken ct)
        => db.QueueTickets.Where(x => x.BusinessId == businessId && x.QueueSessionId == sessionId &&
            x.Status == QueueTicketStatus.Waiting).OrderBy(x => x.Number).FirstOrDefaultAsync(ct);
    public Task<bool> CanManageQueuesAsync(Guid userId, Guid businessId, CancellationToken ct)
        => db.BusinessMemberships.AnyAsync(x => x.UserId == userId && x.BusinessId == businessId && x.IsActive &&
            (x.Role == MembershipRole.Owner || x.CanManageQueues), ct);
    public Task<bool> IsBusinessActiveAsync(Guid businessId, CancellationToken ct)
        => db.Businesses.AnyAsync(x => x.Id == businessId &&
            x.Status == BusinessStatus.Active && x.IsPublished, ct);
    public async Task<(string BusinessName, string BusinessSlug)?> GetBusinessNameAsync(Guid businessId, CancellationToken ct)
    {
        var x = await db.Businesses.Where(b => b.Id == businessId).Select(b => new { b.Name, b.Slug }).SingleOrDefaultAsync(ct);
        return x is null ? null : (x.Name, x.Slug);
    }
    public void AddDefinition(QueueDefinition x) => db.Add(x);
    public void AddSession(QueueSession x) => db.Add(x);
    public void AddTicket(QueueTicket x) => db.Add(x);
    public void AddConsent(ConsentReceipt x) => db.Add(x);
    public async Task SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException("CONCURRENCY_CONFLICT", "La fila cambió. Recargue la información.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException("QUEUE_CONCURRENCY_CONFLICT",
                "Otra operación modificó la fila al mismo tiempo. Recargue la información.", 409);
        }
    }

    private sealed class EfTransaction(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
        : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
