using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

internal sealed class EfTransaction(IDbContextTransaction transaction) : IApplicationTransaction
{
    public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}

public sealed class AccessInvitationStore(AppDbContext db) : IAccessInvitationStore
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => new EfTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public Task<AccessInvitation?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken)
        => db.AccessInvitations.SingleOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);

    public Task<AccessInvitation?> GetAsync(Guid invitationId, CancellationToken cancellationToken)
        => db.AccessInvitations.SingleOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

    public async Task<IReadOnlyList<InvitationRecord>> ListAsync(Guid? businessId, Guid? createdByUserId,
        CancellationToken cancellationToken)
    {
        var query = db.AccessInvitations.AsNoTracking().AsQueryable();
        if (businessId is { } scope) query = query.Where(x => x.BusinessId == scope);
        if (createdByUserId is { } creator) query = query.Where(x => x.CreatedByUserId == creator);
        return await (from invitation in query
                      join business in db.Businesses on invitation.BusinessId equals business.Id into scoped
                      from business in scoped.DefaultIfEmpty()
                      orderby invitation.CreatedAtUtc descending
                      select new InvitationRecord(invitation, business.Name)).Take(200)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasPendingAsync(string email, Guid? businessId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return db.AccessInvitations.AnyAsync(x => x.Email == normalized && x.BusinessId == businessId &&
            x.AcceptedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > now, cancellationToken);
    }

    public Task<string?> GetBusinessNameAsync(Guid businessId, CancellationToken cancellationToken)
        => db.Businesses.AsNoTracking().Where(x => x.Id == businessId).Select(x => x.Name)
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(AccessInvitation invitation) => db.Add(invitation);
    public void AddAudit(PlatformAccessAudit audit) => db.Add(audit);
    public void AddMembership(BusinessMembership membership) => db.Add(membership);

    public Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId,
        CancellationToken cancellationToken)
        => db.BusinessMemberships.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.UserId == userId,
            cancellationToken);

    public async Task<IReadOnlyList<PlatformAccessAuditDto>> ListAuditAsync(Guid? businessId, int take,
        CancellationToken cancellationToken)
    {
        var query = db.PlatformAccessAudits.AsNoTracking().AsQueryable();
        if (businessId is { } scope) query = query.Where(x => x.BusinessId == scope);
        return await (from entry in query
                      join user in db.Users on entry.ActorUserId equals user.Id into actors
                      from actor in actors.DefaultIfEmpty()
                      orderby entry.OccurredAtUtc descending
                      select new PlatformAccessAuditDto(entry.Id, actor.Email, entry.Action.ToString(),
                          entry.Entity, entry.EntityId, entry.BusinessId, entry.Summary, entry.IpAddress,
                          entry.OccurredAtUtc)).Take(take).ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ApiException("CONCURRENCY_CONFLICT", "La información cambió. Recargue e intente de nuevo.", 409); }
    }
}

public sealed class BusinessImageStore(AppDbContext db) : IBusinessImageStore
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => new EfTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task<IReadOnlyList<BusinessImage>> ListAsync(Guid businessId, CancellationToken cancellationToken)
        => await db.BusinessImages.Where(x => x.BusinessId == businessId && !x.IsDeleted)
            .OrderBy(x => x.Kind).ThenBy(x => x.DisplayOrder).ToListAsync(cancellationToken);

    public Task<BusinessImage?> GetAsync(Guid businessId, Guid imageId, CancellationToken cancellationToken)
        => db.BusinessImages.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == imageId,
            cancellationToken);

    public Task<Business?> GetBusinessAsync(Guid businessId, CancellationToken cancellationToken)
        => db.Businesses.SingleOrDefaultAsync(x => x.Id == businessId, cancellationToken);

    /// <summary>El filtro por negocio es la frontera: sin él bastaría un identificador ajeno.</summary>
    public Task<bool> CatalogTargetExistsAsync(Guid businessId, BusinessImageKind kind, Guid targetId,
        CancellationToken cancellationToken)
        => kind == BusinessImageKind.Service
            ? db.Services.AnyAsync(x => x.BusinessId == businessId && x.Id == targetId, cancellationToken)
            : db.Products.AnyAsync(x => x.BusinessId == businessId && x.Id == targetId, cancellationToken);

    public void Add(BusinessImage image) => db.Add(image);
    public void AddAudit(PlatformAuditEntry audit) => db.Add(audit);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        { throw new ApiException("CONCURRENCY_CONFLICT", "La imagen cambió. Recargue e intente de nuevo.", 409); }
    }
}
