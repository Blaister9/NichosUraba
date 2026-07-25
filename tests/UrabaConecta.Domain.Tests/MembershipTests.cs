using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class MembershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Owner_has_all_permissions_implicitly()
    {
        var owner = Member(MembershipRole.Owner, false, false, false);
        Assert.True(owner.CanManageAppointments);
        Assert.True(owner.CanManageConfiguration);
        Assert.True(owner.CanManageMembers);
    }

    [Fact]
    public void Last_active_owner_cannot_be_removed()
    {
        var owner = Member(MembershipRole.Owner);
        var error = Assert.Throws<DomainException>(() =>
            MembershipAdministrationRules.DemandOwnerCanBeRemoved(owner, 1));
        Assert.Equal("LAST_OWNER_REQUIRED", error.Code);
    }

    [Fact]
    public void Owner_can_be_removed_when_another_active_owner_exists()
        => MembershipAdministrationRules.DemandOwnerCanBeRemoved(Member(MembershipRole.Owner), 2);

    [Fact]
    public void Ownership_transfer_grants_every_permission()
    {
        var worker = Member(MembershipRole.Worker, true, false, false);
        worker.GrantOwnership(Now, 0);
        Assert.Equal(MembershipRole.Owner, worker.Role);
        Assert.True(worker.HasPermission(true, true, true));
    }

    [Fact]
    public void Non_owner_cannot_grant_ownership()
    {
        var error = Assert.Throws<DomainException>(() =>
            MembershipAdministrationRules.DemandOwnerAction(Member(MembershipRole.Worker, true, true, true)));
        Assert.Equal("OWNER_REQUIRED", error.Code);
    }

    [Fact]
    public void Non_owner_cannot_modify_own_permissions()
    {
        var worker = Member(MembershipRole.Worker, true, true, true);
        var error = Assert.Throws<DomainException>(() =>
            MembershipAdministrationRules.DemandCanAssign(worker, worker, true, true, true));
        Assert.Equal("SELF_GRANT_FORBIDDEN", error.Code);
    }

    [Fact]
    public void Non_owner_cannot_assign_a_permission_they_lack()
    {
        var actor = Member(MembershipRole.Worker, true, false, true);
        var target = Member(MembershipRole.Worker);
        var error = Assert.Throws<DomainException>(() =>
            MembershipAdministrationRules.DemandCanAssign(actor, target, false, true, false));
        Assert.Equal("PERMISSION_ESCALATION", error.Code);
    }

    [Fact]
    public void Deactivation_is_logical_and_reversible()
    {
        var worker = Member(MembershipRole.Worker);
        worker.Deactivate(Now, 0);
        Assert.False(worker.IsActive);
        Assert.Equal(Now, worker.DeactivatedAtUtc);
        worker.Activate(Now.AddMinutes(1), 1);
        Assert.True(worker.IsActive);
        Assert.Null(worker.DeactivatedAtUtc);
    }

    [Fact]
    public void Stale_membership_version_is_rejected()
    {
        var worker = Member(MembershipRole.Worker);
        worker.UpdatePermissions(true, false, false, Now, 0);
        var error = Assert.Throws<DomainException>(() =>
            worker.UpdatePermissions(false, false, false, Now, 0));
        Assert.Equal("CONCURRENCY_CONFLICT", error.Code);
    }

    [Fact]
    public void Permissions_are_independent_between_business_memberships()
    {
        var user = Guid.NewGuid();
        var first = new BusinessMembership(Guid.NewGuid(), Guid.NewGuid(), user, MembershipRole.Worker,
            canManageConfiguration: true, canManageAppointments: false);
        var second = new BusinessMembership(Guid.NewGuid(), Guid.NewGuid(), user, MembershipRole.Worker,
            canManageConfiguration: false, canManageAppointments: true);
        Assert.True(first.CanManageConfiguration);
        Assert.False(second.CanManageConfiguration);
        Assert.False(first.CanManageAppointments);
        Assert.True(second.CanManageAppointments);
    }

    [Fact]
    public void Audit_event_preserves_relevant_states_without_a_secret_field()
    {
        var audit = new MembershipAuditEntry(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            MembershipAuditAction.PermissionsChanged, """{"CanManageMembers":false}""",
            """{"CanManageMembers":true}""", Now);
        Assert.Equal(MembershipAuditAction.PermissionsChanged, audit.Action);
        Assert.DoesNotContain("password", audit.NewState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invalid_membership_transitions_are_rejected()
    {
        var member = Member(MembershipRole.Worker);
        Assert.Equal("MEMBERSHIP_ALREADY_ACTIVE",
            Assert.Throws<DomainException>(() => member.Activate(Now, 0)).Code);
        Assert.Equal("NOT_OWNER",
            Assert.Throws<DomainException>(() => member.RevokeOwnership(false, false, false, Now, 0)).Code);
    }

    private static BusinessMembership Member(MembershipRole role, bool appointments = true,
        bool configuration = false, bool members = false)
        => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), role, configuration, appointments, members, Now);
}
