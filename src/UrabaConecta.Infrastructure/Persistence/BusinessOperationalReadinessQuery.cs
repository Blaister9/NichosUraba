using Microsoft.EntityFrameworkCore;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Carga por lote los hechos que consume la política única de operabilidad. No decide si un
/// negocio está listo: esa decisión pertenece a <see cref="BusinessOperationalReadiness"/>.
/// </summary>
public static class BusinessOperationalReadinessQuery
{
    public static async Task<IReadOnlyDictionary<Guid, BusinessOperationalFacts>> LoadAsync(
        AppDbContext db, IEnumerable<Guid> businessIds, CancellationToken cancellationToken)
    {
        var ids = businessIds.Distinct().ToArray();
        if (ids.Length == 0) return new Dictionary<Guid, BusinessOperationalFacts>();

        var businesses = await db.Businesses.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new
            {
                x.Id, x.Name, x.ShortDescription, x.Description, x.PublicPhone, x.WhatsAppUrl,
                x.PublicEmail, x.Address, x.LocationMode, x.OrderFulfillmentMode
            }).ToListAsync(cancellationToken);
        var modules = await db.BusinessModules.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var hours = await db.BusinessHours.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var services = await db.Services.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var staff = await db.StaffMembers.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var links = await db.StaffServices.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var queues = await db.QueueDefinitions.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var settings = await db.PickupOrderSettings.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var categories = await db.ProductCategories.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var products = await db.Products.AsNoTracking().Where(x => ids.Contains(x.BusinessId))
            .ToListAsync(cancellationToken);
        var memberships = await db.BusinessMemberships.AsNoTracking().Where(x => ids.Contains(x.BusinessId) &&
                x.IsActive && x.Role == MembershipRole.Owner)
            .Select(x => x.BusinessId).Distinct().ToListAsync(cancellationToken);
        var images = await db.BusinessImages.AsNoTracking().Where(x => ids.Contains(x.BusinessId) && !x.IsDeleted &&
                (x.Kind == BusinessImageKind.Logo || x.Kind == BusinessImageKind.Cover))
            .Select(x => new { x.BusinessId, x.Kind }).ToListAsync(cancellationToken);

        return businesses.ToDictionary(b => b.Id, b =>
        {
            var businessModules = modules.Where(x => x.BusinessId == b.Id).ToList();
            var capabilities = BusinessCapabilities.Resolve(businessModules);
            var businessHours = hours.Where(x => x.BusinessId == b.Id).ToList();
            var activeServices = services.Where(x => x.BusinessId == b.Id && x.IsActive).ToList();
            var eligibleStaffIds = staff.Where(x => x.BusinessId == b.Id && x.IsActive &&
                    x.ParticipatesInAvailability).Select(x => x.Id).ToHashSet();
            var activeServiceIds = activeServices.Select(x => x.Id).ToHashSet();
            var eligibleLinks = links.Where(x => x.BusinessId == b.Id &&
                eligibleStaffIds.Contains(x.StaffMemberId) && activeServiceIds.Contains(x.ServiceId)).ToList();
            var bookable = eligibleLinks.Any(link =>
            {
                var duration = activeServices.Single(x => x.Id == link.ServiceId).DurationMinutes;
                return businessHours.Any(h => (h.ClosesAt - h.OpensAt).TotalMinutes >= duration);
            });
            var orderSettings = settings.SingleOrDefault(x => x.BusinessId == b.Id && x.IsEnabled);
            var activeCategoryIds = categories.Where(x => x.BusinessId == b.Id && x.IsActive)
                .Select(x => x.Id).ToHashSet();
            var availableProduct = products.Any(x => x.BusinessId == b.Id && x.IsActive && x.IsAvailable &&
                activeCategoryIds.Contains(x.ProductCategoryId));
            var compatiblePickup = orderSettings is not null && businessHours.Any(h =>
            {
                var from = h.OpensAt > orderSettings.ReceivesFrom ? h.OpensAt : orderSettings.ReceivesFrom;
                var until = h.ClosesAt < orderSettings.ReceivesUntil ? h.ClosesAt : orderSettings.ReceivesUntil;
                return until > from && (until - from).TotalMinutes >= orderSettings.SlotIntervalMinutes;
            });

            return new BusinessOperationalFacts(
                !string.IsNullOrWhiteSpace(b.Name), !string.IsNullOrWhiteSpace(b.ShortDescription),
                !string.IsNullOrWhiteSpace(b.Description),
                !string.IsNullOrWhiteSpace(b.PublicPhone) || !string.IsNullOrWhiteSpace(b.WhatsAppUrl) ||
                    !string.IsNullOrWhiteSpace(b.PublicEmail),
                b.LocationMode, b.OrderFulfillmentMode, !string.IsNullOrWhiteSpace(b.Address),
                images.Any(x => x.BusinessId == b.Id && x.Kind == BusinessImageKind.Logo),
                images.Any(x => x.BusinessId == b.Id && x.Kind == BusinessImageKind.Cover),
                memberships.Contains(b.Id), capabilities, businessHours.Count > 0, activeServices.Count > 0,
                eligibleLinks.Count > 0, bookable,
                queues.Any(x => x.BusinessId == b.Id && x.IsActive && x.IsEnabled),
                orderSettings is not null, activeCategoryIds.Count > 0, availableProduct, compatiblePickup);
        });
    }
}
