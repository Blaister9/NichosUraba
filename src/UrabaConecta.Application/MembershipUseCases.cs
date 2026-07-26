using System.Text.Json;
using System.Net.Mail;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed partial class UrabaUseCases
{
    public async Task<BusinessMemberListDto> ListMembersAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        var actor = await DemandMembershipAdministration(userId, businessId, cancellationToken);
        return new(await membershipStore.ListMembersAsync(businessId, cancellationToken),
            identityAccounts.DevelopmentAccountCreationEnabled, actor.Id);
    }

    public async Task<BusinessMemberDto> GetMemberAsync(Guid userId, Guid businessId, Guid membershipId,
        CancellationToken cancellationToken = default)
    {
        await DemandMembershipAdministration(userId, businessId, cancellationToken);
        return await membershipStore.GetMemberDtoAsync(businessId, membershipId, cancellationToken)
            ?? throw new ApiException("MEMBER_NOT_FOUND", "No encontramos la membresía.", 404);
    }

    public async Task<BusinessMemberDto> LinkExistingMemberAsync(Guid userId, Guid businessId,
        LinkExistingMemberRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEmail(request.Email);
        var account = await identityAccounts.FindByExactEmailAsync(request.Email.Trim(), cancellationToken)
            ?? throw new ApiException("ACCOUNT_NOT_FOUND", "No encontramos una cuenta con ese correo.", 404);
        await using var tx = await membershipStore.BeginTransactionAsync(cancellationToken);
        var memberships = await membershipStore.LockBusinessMembershipsAsync(businessId, cancellationToken);
        var actor = DemandActor(memberships, userId);
        DemandAssign(actor, null, request.CanManageAppointments, request.CanManageConfiguration,
            request.CanManageMembers, request.CanManageQueues, request.CanManageOrders);
        if (memberships.Any(x => x.UserId == account.UserId))
            throw new ApiException("MEMBERSHIP_EXISTS", "La cuenta ya tiene una membresía en este establecimiento.", 409);
        var now = timeProvider.GetUtcNow();
        var member = new BusinessMembership(Guid.NewGuid(), businessId, account.UserId, MembershipRole.Worker,
            request.CanManageConfiguration, request.CanManageAppointments, request.CanManageMembers, now,
            request.CanManageQueues, request.CanManageOrders);
        membershipStore.AddMembership(member);
        AddAudit(member, actor.UserId, MembershipAuditAction.MemberLinked, "{}", Snapshot(member), now);
        await membershipStore.SaveMembershipChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await RequireDto(businessId, member.Id, cancellationToken);
    }

    public async Task<DevelopmentMemberCreatedDto> CreateDevelopmentMemberAsync(Guid userId, Guid businessId,
        CreateDevelopmentMemberRequest request, CancellationToken cancellationToken = default)
    {
        ValidateEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length is < 2 or > 100)
            throw new ApiException("INVALID_DISPLAY_NAME", "Ingrese un nombre visible de 2 a 100 caracteres.");
        if (!identityAccounts.DevelopmentAccountCreationEnabled)
            throw new ApiException("NOT_FOUND", "La función no está disponible.", 404);
        await using var tx = await membershipStore.BeginTransactionAsync(cancellationToken);
        var memberships = await membershipStore.LockBusinessMembershipsAsync(businessId, cancellationToken);
        var actor = DemandActor(memberships, userId);
        DemandAssign(actor, null, request.CanManageAppointments, request.CanManageConfiguration,
            request.CanManageMembers, request.CanManageQueues, request.CanManageOrders);
        if (await identityAccounts.FindByExactEmailAsync(request.Email.Trim(), cancellationToken) is not null)
            throw new ApiException("ACCOUNT_EXISTS", "Ya existe una cuenta con ese correo. Use vincular cuenta.", 409);
        var created = await identityAccounts.CreateDevelopmentAsync(request.DisplayName.Trim(), request.Email.Trim(),
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var member = new BusinessMembership(Guid.NewGuid(), businessId, created.Account.UserId, MembershipRole.Worker,
            request.CanManageConfiguration, request.CanManageAppointments, request.CanManageMembers, now,
            request.CanManageQueues, request.CanManageOrders);
        membershipStore.AddMembership(member);
        AddAudit(member, actor.UserId, MembershipAuditAction.MemberLinked, "{}", Snapshot(member), now);
        await membershipStore.SaveMembershipChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return new(await RequireDto(businessId, member.Id, cancellationToken), created.TemporaryPassword);
    }

    public Task<BusinessMemberDto> UpdateMemberPermissionsAsync(Guid userId, Guid businessId, Guid membershipId,
        UpdateMemberPermissionsRequest request, CancellationToken cancellationToken = default)
        => Mutate(userId, businessId, membershipId, request.Version, MembershipAuditAction.PermissionsChanged,
            (actor, target, now, _) =>
            {
                DemandAssign(actor, target, request.CanManageAppointments, request.CanManageConfiguration,
                    request.CanManageMembers, request.CanManageQueues, request.CanManageOrders);
                target.UpdatePermissions(request.CanManageAppointments, request.CanManageConfiguration,
                    request.CanManageMembers, request.CanManageQueues, request.CanManageOrders, now, request.Version);
            }, cancellationToken);

    public Task<BusinessMemberDto> ActivateMemberAsync(Guid userId, Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => Mutate(userId, businessId, membershipId, version, MembershipAuditAction.MemberActivated,
            (actor, target, now, _) =>
            {
                DemandAssign(actor, target, target.CanManageAppointments, target.CanManageConfiguration,
                    target.CanManageMembers, target.CanManageQueues, target.CanManageOrders);
                target.Activate(now, version);
            }, cancellationToken);

    public Task<BusinessMemberDto> DeactivateMemberAsync(Guid userId, Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => Mutate(userId, businessId, membershipId, version, MembershipAuditAction.MemberDeactivated,
            (actor, target, now, members) =>
            {
                DemandAssign(actor, target, target.CanManageAppointments, target.CanManageConfiguration,
                    target.CanManageMembers, target.CanManageQueues, target.CanManageOrders);
                TryMembershipDomain(() => MembershipAdministrationRules.DemandOwnerCanBeRemoved(target,
                    members.Count(x => x.IsActive && x.Role == MembershipRole.Owner)));
                target.Deactivate(now, version);
            }, cancellationToken);

    public Task<BusinessMemberDto> GrantOwnershipAsync(Guid userId, Guid businessId, Guid membershipId, long version,
        CancellationToken cancellationToken = default)
        => Mutate(userId, businessId, membershipId, version, MembershipAuditAction.OwnerGranted,
            (actor, target, now, _) =>
            {
                TryMembershipDomain(() => MembershipAdministrationRules.DemandOwnerAction(actor));
                target.GrantOwnership(now, version);
            }, cancellationToken);

    public Task<BusinessMemberDto> RevokeOwnershipAsync(Guid userId, Guid businessId, Guid membershipId,
        RevokeOwnershipRequest request, CancellationToken cancellationToken = default)
        => Mutate(userId, businessId, membershipId, request.Version, MembershipAuditAction.OwnerRevoked,
            (actor, target, now, members) =>
            {
                TryMembershipDomain(() => MembershipAdministrationRules.DemandOwnerAction(actor));
                TryMembershipDomain(() => MembershipAdministrationRules.DemandOwnerCanBeRemoved(target,
                    members.Count(x => x.IsActive && x.Role == MembershipRole.Owner)));
                target.RevokeOwnership(request.CanManageAppointments, request.CanManageConfiguration,
                    request.CanManageMembers, request.CanManageQueues, request.CanManageOrders, now, request.Version);
            }, cancellationToken);

    public async Task<IReadOnlyList<MembershipAuditDto>> ListMembershipAuditAsync(Guid userId, Guid businessId,
        Guid membershipId, CancellationToken cancellationToken = default)
    {
        await DemandMembershipAdministration(userId, businessId, cancellationToken);
        if (await membershipStore.GetMemberDtoAsync(businessId, membershipId, cancellationToken) is null)
            throw new ApiException("MEMBER_NOT_FOUND", "No encontramos la membresía.", 404);
        return await membershipStore.ListAuditAsync(businessId, membershipId, cancellationToken);
    }

    private async Task<BusinessMemberDto> Mutate(Guid userId, Guid businessId, Guid membershipId, long version,
        MembershipAuditAction action,
        Action<BusinessMembership, BusinessMembership, DateTimeOffset, IReadOnlyList<BusinessMembership>> change,
        CancellationToken cancellationToken)
    {
        await using var tx = await membershipStore.BeginTransactionAsync(cancellationToken);
        var memberships = await membershipStore.LockBusinessMembershipsAsync(businessId, cancellationToken);
        var actor = DemandActor(memberships, userId);
        var target = memberships.SingleOrDefault(x => x.Id == membershipId)
            ?? throw new ApiException("MEMBER_NOT_FOUND", "No encontramos la membresía.", 404);
        if (target.Version != version)
            throw new ApiException("CONCURRENCY_CONFLICT", "La membresía cambió. Recargue la información.", 409);
        var previous = Snapshot(target);
        var now = timeProvider.GetUtcNow();
        TryMembershipDomain(() => change(actor, target, now, memberships));
        AddAudit(target, actor.UserId, action, previous, Snapshot(target), now);
        await membershipStore.SaveMembershipChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return await RequireDto(businessId, target.Id, cancellationToken);
    }

    private async Task<BusinessMembership> DemandMembershipAdministration(Guid userId, Guid businessId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ApiException("UNAUTHENTICATED", "Debe iniciar sesión.", 401);
        var actor = await membershipStore.GetMembershipByUserAsync(businessId, userId, cancellationToken)
            ?? throw new ApiException("BUSINESS_ACCESS_DENIED", "No tiene acceso a este establecimiento.", 403);
        TryMembershipDomain(() => MembershipAdministrationRules.DemandCanAdminister(actor));
        return actor;
    }

    private static BusinessMembership DemandActor(IReadOnlyList<BusinessMembership> memberships, Guid userId)
    {
        var actor = memberships.SingleOrDefault(x => x.UserId == userId)
            ?? throw new ApiException("BUSINESS_ACCESS_DENIED", "No tiene acceso a este establecimiento.", 403);
        TryMembershipDomain(() => MembershipAdministrationRules.DemandCanAdminister(actor));
        return actor;
    }

    private static void DemandAssign(BusinessMembership actor, BusinessMembership? target,
        bool appointments, bool configuration, bool members, bool queues, bool orders)
    {
        target ??= new BusinessMembership(Guid.NewGuid(), actor.BusinessId, Guid.NewGuid(), MembershipRole.Worker,
            configuration, appointments, members, canManageQueues: queues, canManageOrders: orders);
        TryMembershipDomain(() => MembershipAdministrationRules.DemandCanAssign(actor, target,
            appointments, configuration, members, queues, orders));
    }

    private void AddAudit(BusinessMembership target, Guid actorUserId, MembershipAuditAction action,
        string previous, string current, DateTimeOffset now)
        => membershipStore.AddAudit(new MembershipAuditEntry(Guid.NewGuid(), target.BusinessId, target.Id,
            actorUserId, action, previous, current, now));

    private async Task<BusinessMemberDto> RequireDto(Guid businessId, Guid membershipId, CancellationToken ct)
        => await membershipStore.GetMemberDtoAsync(businessId, membershipId, ct)
           ?? throw new ApiException("MEMBER_NOT_FOUND", "No encontramos la membresía.", 404);

    private static string Snapshot(BusinessMembership member) => JsonSerializer.Serialize(new
    {
        member.IsActive,
        IsOwner = member.Role == MembershipRole.Owner,
        member.CanManageAppointments,
        member.CanManageConfiguration,
        member.CanManageMembers,
        member.CanManageQueues,
        member.CanManageOrders,
        member.Version
    });

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 256 ||
            !MailAddress.TryCreate(email.Trim(), out var parsed) ||
            !string.Equals(parsed.Address, email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ApiException("INVALID_EMAIL", "Ingrese un correo válido.");
    }

    private static void TryMembershipDomain(Action action)
    {
        try { action(); }
        catch (DomainException ex)
        {
            var forbidden = ex.Code is "MEMBERSHIP_FORBIDDEN" or "OWNER_REQUIRED" or "PERMISSION_ESCALATION"
                or "SELF_GRANT_FORBIDDEN";
            throw new ApiException(ex.Code, ex.Message, forbidden ? 403 : 409);
        }
    }
}
