using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class MembershipAdministrationStore(AppDbContext db) : IMembershipAdministrationStore
{
    public async Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
        => new EfApplicationTransaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task<IReadOnlyList<BusinessMembership>> LockBusinessMembershipsAsync(Guid businessId,
        CancellationToken cancellationToken)
        => await db.BusinessMemberships.FromSqlInterpolated(
                $"""SELECT * FROM "business_memberships" WHERE "BusinessId" = {businessId} FOR UPDATE""")
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);

    public Task<BusinessMembership?> GetMembershipAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken)
        => db.BusinessMemberships.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == membershipId,
            cancellationToken);

    public Task<BusinessMembership?> GetMembershipByUserAsync(Guid businessId, Guid userId,
        CancellationToken cancellationToken)
        => db.BusinessMemberships.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BusinessId == businessId && x.UserId == userId && x.IsActive, cancellationToken);

    public async Task<IReadOnlyList<BusinessMemberDto>> ListMembersAsync(Guid businessId,
        CancellationToken cancellationToken)
    {
        var items = await QueryMembers(businessId).ToListAsync(cancellationToken);
        return items.OrderByDescending(x => x.IsActive).ThenByDescending(x => x.IsOwner)
            .ThenBy(x => x.DisplayName).ToArray();
    }

    public Task<BusinessMemberDto?> GetMemberDtoAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken)
        => QueryMembers(businessId, membershipId).SingleOrDefaultAsync(cancellationToken);

    public void AddMembership(BusinessMembership membership) => db.BusinessMemberships.Add(membership);
    public void AddAudit(MembershipAuditEntry entry) => db.MembershipAuditEntries.Add(entry);

    public async Task<IReadOnlyList<MembershipAuditDto>> ListAuditAsync(Guid businessId, Guid membershipId,
        CancellationToken cancellationToken)
        => await db.MembershipAuditEntries.AsNoTracking()
            .Where(x => x.BusinessId == businessId && x.MembershipId == membershipId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new MembershipAuditDto(x.Id, x.Action.ToString(), x.ActorUserId, x.OccurredAtUtc,
                x.PreviousState, x.NewState))
            .ToListAsync(cancellationToken);

    public async Task SaveMembershipChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException("CONCURRENCY_CONFLICT",
                "La membresía cambió mientras la editaba. Recargue e intente de nuevo.", 409);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new ApiException("MEMBERSHIP_EXISTS",
                "La cuenta ya tiene una membresía en este establecimiento.", 409);
        }
    }

    private IQueryable<BusinessMemberDto> QueryMembers(Guid businessId, Guid? membershipId = null)
        => from membership in db.BusinessMemberships.AsNoTracking()
           join user in db.Users.AsNoTracking() on membership.UserId equals user.Id
           where membership.BusinessId == businessId &&
                 (!membershipId.HasValue || membership.Id == membershipId.Value)
           select new BusinessMemberDto(membership.Id,
               user.DisplayName == "" ? (user.Email ?? "Cuenta sin nombre") : user.DisplayName,
               user.Email ?? "", membership.IsActive, membership.Role == MembershipRole.Owner,
               new MembershipPermissionsDto(
                   membership.Role == MembershipRole.Owner || membership.CanManageAppointments,
                   membership.Role == MembershipRole.Owner || membership.CanManageConfiguration,
                   membership.Role == MembershipRole.Owner || membership.CanManageMembers),
               membership.CreatedAtUtc, membership.UpdatedAtUtc, membership.Version);

    private sealed class EfApplicationTransaction(IDbContextTransaction transaction) : IApplicationTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => transaction.CommitAsync(cancellationToken);
        public ValueTask DisposeAsync() => transaction.DisposeAsync();
    }
}
