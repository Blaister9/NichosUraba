using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class AccessInvitationUseCases(
    IAccessInvitationStore store,
    IInvitationIdentityGateway identity,
    IInvitationTokenService tokens,
    IPlatformAdministrationStore businesses,
    IIdentityAccountManager membershipRoles,
    TimeProvider timeProvider) : IAccessInvitationUseCases
{
    public const string PartnerOperatorRole = "PartnerOperator";
    public const string BusinessOwnerRole = "BusinessOwner";
    public const string BusinessWorkerRole = "BusinessWorker";

    public async Task<InvitationIssuedDto> InviteAsync(PlatformActor actor, CreateInvitationRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOperator(actor);
        var grant = ParseGrant(request.Grant);
        // Sólo el administrador técnico crea socias: impide que una socia se promueva a sí misma.
        if (grant == AccessGrantKind.PartnerOperator && !actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma crea cuentas de socia.", 403);
        if (grant != AccessGrantKind.PartnerOperator)
        {
            if (request.BusinessId is not { } businessId)
                throw new ApiException("BUSINESS_REQUIRED", "Indique el negocio al que pertenece el acceso.");
            await EnsureBusinessScopeAsync(actor, businessId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        if (await store.HasPendingAsync(request.Email, request.BusinessId, now, cancellationToken))
            throw new ApiException("INVITATION_PENDING",
                "Ya existe una invitación vigente para ese correo. Revóquela o reenvíela.", 409);

        var issued = Issue(actor, request.Email, request.DisplayName, grant, request.BusinessId,
            AccessInvitationPurpose.Invitation, TimeSpan.FromHours(request.LifetimeHours), now,
            PlatformAccessAction.InvitationCreated);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return issued;
    }

    public async Task<InvitationIssuedDto> ResendAsync(PlatformActor actor, Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        EnsureOperator(actor);
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var existing = await store.GetAsync(invitationId, cancellationToken)
            ?? throw new ApiException("INVITATION_NOT_FOUND", "No encontramos la invitación.", 404);
        if (existing.AcceptedAtUtc is not null)
            throw new ApiException("INVITATION_ALREADY_ACCEPTED", "La invitación ya fue aceptada.", 409);
        if (existing.Grant == AccessGrantKind.PartnerOperator && !actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "No tiene permiso sobre esta invitación.", 403);
        if (existing.BusinessId is { } scope) await EnsureBusinessScopeAsync(actor, scope, cancellationToken);

        // Reenviar invalida el enlace anterior: se revoca y se emite uno nuevo.
        if (existing.RevokedAtUtc is null) existing.Revoke(actor.UserId, now);
        var lifetime = existing.ExpiresAtUtc - existing.CreatedAtUtc;
        var issued = Issue(actor, existing.Email, existing.DisplayName, existing.Grant,
            existing.BusinessId, existing.Purpose, lifetime, now, PlatformAccessAction.InvitationResent);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return issued;
    }

    public async Task RevokeAsync(PlatformActor actor, Guid invitationId, CancellationToken cancellationToken = default)
    {
        EnsureOperator(actor);
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var invitation = await store.GetAsync(invitationId, cancellationToken)
            ?? throw new ApiException("INVITATION_NOT_FOUND", "No encontramos la invitación.", 404);
        if (invitation.Grant == AccessGrantKind.PartnerOperator && !actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "No tiene permiso sobre esta invitación.", 403);
        if (invitation.BusinessId is { } scope) await EnsureBusinessScopeAsync(actor, scope, cancellationToken);
        TryDomain(() => { invitation.Revoke(actor.UserId, now); return true; });
        Audit(actor, PlatformAccessAction.InvitationRevoked, invitation, "Invitación revocada.", now);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<InvitationDto>> ListAsync(PlatformActor actor, Guid? businessId,
        CancellationToken cancellationToken = default)
    {
        EnsureOperator(actor);
        if (businessId is { } scope) await EnsureBusinessScopeAsync(actor, scope, cancellationToken);
        // Una socia sólo ve las invitaciones que ella misma emitió.
        var createdBy = actor.IsPlatformAdmin ? (Guid?)null : actor.UserId;
        var now = timeProvider.GetUtcNow();
        return (await store.ListAsync(businessId, createdBy, cancellationToken))
            .Select(r => new InvitationDto(r.Invitation.Id, r.Invitation.Email, r.Invitation.DisplayName,
                r.Invitation.Grant.ToString(), r.Invitation.Purpose.ToString(), r.Invitation.BusinessId,
                r.BusinessName, r.Invitation.StatusFor(now), r.Invitation.CreatedAtUtc,
                r.Invitation.ExpiresAtUtc, r.Invitation.AcceptedAtUtc))
            .ToList();
    }

    public async Task<InvitationPreviewDto> PreviewAsync(string token, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var invitation = await FindUsableAsync(token, now, cancellationToken);
        var businessName = invitation.BusinessId is { } id
            ? await store.GetBusinessNameAsync(id, cancellationToken) : null;
        return new(invitation.Email, invitation.DisplayName, invitation.Grant.ToString(),
            invitation.Purpose.ToString(), businessName, invitation.ExpiresAtUtc);
    }

    public async Task AcceptAsync(string token, string password, string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new ApiException("WEAK_PASSWORD", "La contraseña debe tener al menos 10 caracteres.");
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        var invitation = await FindUsableAsync(token, now, cancellationToken);

        var account = await identity.FindByExactEmailAsync(invitation.Email, cancellationToken)
            ?? await identity.CreatePendingAsync(invitation.DisplayName, invitation.Email,
                RoleFor(invitation.Grant), cancellationToken);

        if (invitation.Purpose == AccessInvitationPurpose.Invitation)
        {
            await identity.EnsureRoleAsync(account.UserId, RoleFor(invitation.Grant), cancellationToken);
            if (invitation.BusinessId is { } businessId)
            {
                var isOwner = invitation.Grant == AccessGrantKind.BusinessOwner;
                var membership = await store.GetMembershipByUserAsync(businessId, account.UserId,
                    cancellationToken);
                if (membership is null)
                    store.AddMembership(new BusinessMembership(Guid.NewGuid(), businessId, account.UserId,
                        isOwner ? MembershipRole.Owner : MembershipRole.Worker,
                        canManageConfiguration: isOwner, canManageAppointments: true,
                        canManageMembers: isOwner, createdAtUtc: now,
                        canManageQueues: isOwner, canManageOrders: isOwner));
                else
                {
                    if (!membership.IsActive) membership.Activate(now, membership.Version);
                    if (isOwner && membership.Role != MembershipRole.Owner)
                        membership.GrantOwnership(now, membership.Version);
                }
            }
        }

        // Fija la contraseña elegida, limpia el cambio obligatorio y cierra cualquier sesión previa.
        await identity.SetPasswordAndActivateAsync(account.UserId, password, cancellationToken);
        TryDomain(() => { invitation.Accept(account.UserId, now); return true; });
        store.AddAudit(new PlatformAccessAudit(Guid.NewGuid(), account.UserId,
            invitation.Purpose == AccessInvitationPurpose.PasswordReset
                ? PlatformAccessAction.PasswordChanged
                : PlatformAccessAction.InvitationAccepted,
            nameof(AccessInvitation), invitation.Id.ToString(), invitation.BusinessId,
            $"Acceso activado para {invitation.Email} ({invitation.Grant}).", ipAddress, now));
        await store.SaveChangesAsync(cancellationToken);
        if (invitation.Purpose == AccessInvitationPurpose.Invitation && invitation.BusinessId is not null)
            await membershipRoles.SynchronizeMembershipRolesAsync(account.UserId, cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<InvitationIssuedDto> ResetAccessAsync(PlatformActor actor, ResetAccessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma reinicia accesos.", 403);
        var account = await identity.FindByExactEmailAsync(request.Email, cancellationToken)
            ?? throw new ApiException("ACCOUNT_NOT_FOUND", "No encontramos esa cuenta.", 404);
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        // Cierra las sesiones abiertas antes de emitir el enlace de un solo uso.
        await identity.RevokeSessionsAsync(account.UserId, cancellationToken);
        var issued = Issue(actor, account.Email, account.DisplayName, AccessGrantKind.PartnerOperator,
            null, AccessInvitationPurpose.PasswordReset, TimeSpan.FromHours(request.LifetimeHours), now,
            PlatformAccessAction.AdministrativeAccessReset);
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return issued;
    }

    public async Task<IReadOnlyList<PlatformAccessAuditDto>> ListAuditAsync(PlatformActor actor, Guid? businessId,
        CancellationToken cancellationToken = default)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma consulta la auditoría.", 403);
        return await store.ListAuditAsync(businessId, 200, cancellationToken);
    }

    public async Task<IReadOnlyList<PlatformAccountDto>> ListPartnerOperatorsAsync(PlatformActor actor,
        CancellationToken cancellationToken = default)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma administra socias.", 403);
        return await identity.ListByRoleAsync(PartnerOperatorRole, cancellationToken);
    }

    public async Task RevokePartnerOperatorAsync(PlatformActor actor, Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!actor.IsPlatformAdmin)
            throw new ApiException("FORBIDDEN", "Solo la administración de plataforma administra socias.", 403);
        if (userId == actor.UserId)
            throw new ApiException("SELF_REVOKE_FORBIDDEN", "No puede revocar su propio acceso.", 409);
        var now = timeProvider.GetUtcNow();
        await using var tx = await store.BeginTransactionAsync(cancellationToken);
        await identity.RemoveRoleAsync(userId, PartnerOperatorRole, cancellationToken);
        await identity.RevokeSessionsAsync(userId, cancellationToken);
        store.AddAudit(new PlatformAccessAudit(Guid.NewGuid(), actor.UserId,
            PlatformAccessAction.PartnerOperatorRevoked, "ApplicationUser", userId.ToString(), null,
            "Se revocó el rol de socia y se cerraron sus sesiones.", actor.IpAddress, now, actor.CorrelationId));
        await store.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    // -----------------------------------------------------------------------

    private InvitationIssuedDto Issue(PlatformActor actor, string email, string displayName,
        AccessGrantKind grant, Guid? businessId, AccessInvitationPurpose purpose, TimeSpan lifetime,
        DateTimeOffset now, PlatformAccessAction action)
    {
        var (plainText, hash) = tokens.Generate();
        var invitation = TryDomain(() => new AccessInvitation(Guid.NewGuid(), email, displayName, grant,
            businessId, hash, actor.UserId, now, lifetime, purpose));
        store.Add(invitation);
        if (grant == AccessGrantKind.PartnerOperator && purpose == AccessInvitationPurpose.Invitation)
            store.AddAudit(new PlatformAccessAudit(Guid.NewGuid(), actor.UserId,
                PlatformAccessAction.PartnerOperatorCreated, nameof(AccessInvitation), invitation.Id.ToString(),
                null, $"Se invitó a {invitation.Email} como socia.", actor.IpAddress, now, actor.CorrelationId));
        Audit(actor, action, invitation, $"Enlace emitido para {invitation.Email} ({grant}).", now);
        return new(invitation.Id, invitation.Email, grant.ToString(),
            $"/Account/AcceptInvitation?token={Uri.EscapeDataString(plainText)}", invitation.ExpiresAtUtc);
    }

    private async Task<AccessInvitation> FindUsableAsync(string token, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ApiException("INVITATION_INVALID", "El enlace no es válido.", 404);
        var invitation = await store.FindByHashAsync(tokens.Hash(token), cancellationToken)
            ?? throw new ApiException("INVITATION_INVALID", "El enlace no es válido.", 404);
        if (!invitation.IsPending(now))
            throw new ApiException("INVITATION_INVALID",
                invitation.StatusFor(now) switch
                {
                    "Accepted" => "Este enlace ya fue utilizado.",
                    "Revoked" => "Este enlace fue revocado.",
                    _ => "Este enlace expiró. Solicite uno nuevo."
                }, 410);
        if (invitation.LockedUntilUtc is { } locked && locked > now)
            throw new ApiException("INVITATION_LOCKED", "Demasiados intentos. Espere unos minutos.", 429);
        return invitation;
    }

    private async Task EnsureBusinessScopeAsync(PlatformActor actor, Guid businessId,
        CancellationToken cancellationToken)
    {
        if (actor.IsPlatformAdmin) return;
        var record = await businesses.GetAsync(businessId, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el negocio.", 404);
        if (record.Business.CreatedByUserId != actor.UserId)
            throw new ApiException("FORBIDDEN", "El negocio no está a su cargo.", 403);
    }

    private void Audit(PlatformActor actor, PlatformAccessAction action, AccessInvitation invitation,
        string summary, DateTimeOffset now)
        => store.AddAudit(new PlatformAccessAudit(Guid.NewGuid(), actor.UserId, action,
            nameof(AccessInvitation), invitation.Id.ToString(), invitation.BusinessId, summary,
            actor.IpAddress, now, actor.CorrelationId));

    private static void EnsureOperator(PlatformActor actor)
    {
        if (!actor.CanOperate)
            throw new ApiException("FORBIDDEN", "No tiene permiso para administrar accesos.", 403);
    }

    internal static string RoleFor(AccessGrantKind grant) => grant switch
    {
        AccessGrantKind.PartnerOperator => PartnerOperatorRole,
        AccessGrantKind.BusinessOwner => BusinessOwnerRole,
        _ => BusinessWorkerRole
    };

    private static AccessGrantKind ParseGrant(string value)
        => Enum.TryParse<AccessGrantKind>(value, ignoreCase: true, out var grant)
            ? grant
            : throw new ApiException("INVALID_GRANT", "El tipo de acceso no es válido.");

    private static T TryDomain<T>(Func<T> action)
    {
        try { return action(); } catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 400); }
    }
}
