namespace UrabaConecta.Domain;

public static class MembershipAdministrationRules
{
    public static void DemandCanAdminister(BusinessMembership actor)
    {
        if (!actor.IsActive || !actor.HasPermission(false, false, true))
            throw new DomainException("MEMBERSHIP_FORBIDDEN", "No tiene permiso para administrar el equipo.");
    }

    public static void DemandCanAssign(BusinessMembership actor, BusinessMembership target,
        bool appointments, bool configuration, bool members, bool queues = false)
    {
        DemandCanAdminister(actor);
        if (actor.Role != MembershipRole.Owner && target.Role == MembershipRole.Owner)
            throw new DomainException("OWNER_REQUIRED", "Solo una persona propietaria puede administrar propietarios.");
        if (actor.Role != MembershipRole.Owner && actor.Id == target.Id)
            throw new DomainException("SELF_GRANT_FORBIDDEN", "No puede otorgarse permisos a sí misma.");
        if (actor.Role != MembershipRole.Owner &&
            !actor.HasPermission(appointments, configuration, members, queues))
            throw new DomainException("PERMISSION_ESCALATION", "No puede otorgar permisos que no posee.");
    }

    public static void DemandOwnerAction(BusinessMembership actor)
    {
        if (!actor.IsActive || actor.Role != MembershipRole.Owner)
            throw new DomainException("OWNER_REQUIRED", "Solo una persona propietaria puede realizar esta acción.");
    }

    public static void DemandOwnerCanBeRemoved(BusinessMembership target, int activeOwners)
    {
        if (target.Role == MembershipRole.Owner && target.IsActive && activeOwners <= 1)
            throw new DomainException("LAST_OWNER_REQUIRED", "El establecimiento debe conservar al menos una persona propietaria activa.");
    }
}
