using Microsoft.EntityFrameworkCore;
using UrabaConecta.Application;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// La misma regla que gobierna las pantallas gobierna los grupos en vivo: membresía activa en ESE
/// negocio y el permiso concreto del canal. Sin esto, unirse a un grupo sería una forma de saber
/// cuándo entra un pedido en un negocio ajeno, aunque nunca se viera su contenido.
/// </summary>
public sealed class RealtimeAccessGuard(AppDbContext db, IPublicCodeService codes) : IRealtimeAccessGuard
{
    public async Task<bool> CanSubscribeBusinessAsync(Guid userId, Guid businessId, string channel,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || businessId == Guid.Empty) return false;
        var query = db.BusinessMemberships.AsNoTracking()
            .Where(x => x.UserId == userId && x.BusinessId == businessId && x.IsActive);
        query = channel switch
        {
            RealtimeChannels.Appointments =>
                query.Where(x => x.Role == MembershipRole.Owner || x.CanManageAppointments),
            RealtimeChannels.Orders =>
                query.Where(x => x.Role == MembershipRole.Owner || x.CanManageOrders),
            // La bandeja ya filtra por permiso lo que muestra, así que basta ser miembro activo
            // para enterarse de que hay algo nuevo que mirar.
            RealtimeChannels.Notifications => query,
            _ => query.Where(_ => false)
        };
        return await query.AnyAsync(cancellationToken);
    }

    public async Task<Guid?> ResolveTrackedEntityAsync(string entityType, string publicCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicCode) || publicCode.Length is < 20 or > 128) return null;
        var hash = codes.Hash(publicCode);
        return entityType switch
        {
            TrackedEntities.Appointment => await db.Appointments.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken),
            TrackedEntities.PickupOrder => await db.PickupOrders.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken),
            TrackedEntities.QueueTicket => await db.QueueTickets.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
    }
}
