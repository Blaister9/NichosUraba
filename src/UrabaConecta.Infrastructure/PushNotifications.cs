using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;
using WebPush;
using BrowserPushSubscription = WebPush.PushSubscription;
using StoredPushSubscription = UrabaConecta.Domain.WebPushSubscription;

namespace UrabaConecta.Infrastructure;

public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";
    public string Subject { get; set; } = "";
    public string PublicKey { get; set; } = "";
    public string PrivateKey { get; set; } = "";
    public string NormalizedSubject => Normalize(Subject);
    public string NormalizedPublicKey => Normalize(PublicKey);
    public string NormalizedPrivateKey => Normalize(PrivateKey);
    public bool IsConfigured => NormalizedSubject.Length > 0 &&
        NormalizedPublicKey.Length > 0 && NormalizedPrivateKey.Length > 0;

    // Los valores suelen llegar desde archivos o CLI de variables. Un BOM invisible al inicio
    // convierte una clave base64url válida en una que PushManager/atob no puede decodificar.
    private static string Normalize(string? value) => (value ?? "").Trim().TrimStart('\uFEFF').Trim();
}

public interface IWebPushTransport
{
    Task SendAsync(StoredPushSubscription subscription, PushMessage message,
        CancellationToken cancellationToken = default);
}

public sealed class PushDeliveryException(int? statusCode, Exception innerException)
    : Exception("El servicio Web Push rechazó la entrega.", innerException)
{
    public int? StatusCode { get; } = statusCode;
}

public sealed class WebPushTransport(IOptions<WebPushOptions> options) : IWebPushTransport
{
    private readonly WebPushOptions settings = options.Value;

    public async Task SendAsync(StoredPushSubscription subscription, PushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured) return;
        var payload = JsonSerializer.Serialize(new
        {
            title = message.Title, body = message.Body, url = message.Url,
            tag = message.Tag, renotify = message.Renotify
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var target = new BrowserPushSubscription(subscription.Endpoint, subscription.P256dh, subscription.Auth);
        var vapid = new VapidDetails(settings.NormalizedSubject, settings.NormalizedPublicKey,
            settings.NormalizedPrivateKey);
        using var client = new WebPushClient();
        try { await client.SendNotificationAsync(target, payload, vapid, cancellationToken); }
        catch (WebPushException ex)
        {
            throw new PushDeliveryException((int?)ex.StatusCode, ex);
        }
    }
}

public sealed class PushNotificationService(AppDbContext db, IPublicCodeService codes,
    IPersonalDataProtector protector, INotificationPublisher notifications,
    IOptions<WebPushOptions> options, IObjectStorage storage, TimeProvider clock)
    : IPushNotificationService
{
    private readonly WebPushOptions settings = options.Value;
    public PushConfigurationDto Configuration => new(settings.IsConfigured,
        settings.IsConfigured ? settings.NormalizedPublicKey : null);

    public async Task<WebPushSubscriptionDto> RegisterOwnerAsync(Guid userId, Guid businessId,
        WebPushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        DemandConfigured(); Validate(request);
        var authorized = await db.BusinessMemberships.AsNoTracking().AnyAsync(x =>
            x.UserId == userId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (!authorized)
            throw new ApiException("MEMBERSHIP_FORBIDDEN",
                "No tiene permiso para activar avisos de este negocio.", 403);
        return await Upsert(businessId, PushAudience.Owner, businessId.ToString("N"), request,
            userId, null, null, cancellationToken);
    }

    public async Task<WebPushSubscriptionDto> RegisterClientAsync(PushAudience audience, string publicCode,
        WebPushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        DemandConfigured(); Validate(request);
        var target = await ResolveClient(audience, publicCode, cancellationToken);
        var deepLink = audience switch
        {
            PushAudience.Appointment => $"/seguimiento/citas/{Uri.EscapeDataString(publicCode)}",
            PushAudience.QueueTicket => $"/seguimiento/turnos/{Uri.EscapeDataString(publicCode)}",
            PushAudience.PickupOrder => $"/seguimiento/pedidos/{Uri.EscapeDataString(publicCode)}",
            _ => throw new ApiException("INVALID_PUSH_SCOPE", "El tipo de aviso no es válido.")
        };
        var saved = await Upsert(target.BusinessId, audience, target.EntityId.ToString("N"), request,
            null, target.EntityId, protector.Protect(deepLink), cancellationToken);
        // La confirmación pasa por el mismo buzón que el resto: es el aviso que le dice a la
        // persona que el registro funcionó, y si sale por un camino aparte no se puede diagnosticar
        // junto a los demás cuando alguien reporta que "no me llega nada".
        var (title, body, entityType) = audience switch
        {
            PushAudience.Appointment => ("Cita registrada",
                "Te avisaremos aquí cuando haya una novedad útil sobre tu cita.",
                TrackedEntities.Appointment),
            PushAudience.QueueTicket => ("Turno registrado",
                "Te avisaremos cuando tu turno esté cerca y cuando te llamen.",
                TrackedEntities.QueueTicket),
            _ => ("Pedido recibido", "Te avisaremos cuando tu pedido esté listo para recoger.",
                TrackedEntities.PickupOrder)
        };
        await notifications.PublishAsync(new(target.BusinessId, NotificationAudience.Customer,
            NotificationKind.TrackingSubscribed, title, body, null, entityType, target.EntityId,
            Notification.Key(NotificationAudience.Customer, NotificationKind.TrackingSubscribed,
                target.EntityId, Hash(request.Endpoint)[..16]),
            audience), cancellationToken);
        return saved;
    }

    public async Task UnregisterOwnerAsync(Guid userId, Guid businessId, WebPushUnsubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var authorized = await db.BusinessMemberships.AsNoTracking().AnyAsync(x =>
            x.UserId == userId && x.BusinessId == businessId && x.IsActive, cancellationToken);
        if (!authorized) throw new ApiException("MEMBERSHIP_FORBIDDEN", "No tiene acceso a este negocio.", 403);
        await Deactivate(PushAudience.Owner, businessId.ToString("N"), request.Endpoint, cancellationToken);
    }

    public async Task UnregisterClientAsync(PushAudience audience, string publicCode,
        WebPushUnsubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var target = await ResolveClient(audience, publicCode, cancellationToken);
        await Deactivate(audience, target.EntityId.ToString("N"), request.Endpoint, cancellationToken);
    }

    public async Task<WebPushSubscriptionDto> RegisterProductRestockAsync(string businessSlug, Guid productId,
        WebPushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        DemandConfigured(); Validate(request);
        var target = await (from product in db.Products.AsNoTracking()
            join business in db.Businesses.AsNoTracking() on product.BusinessId equals business.Id
            where business.Slug == businessSlug && business.IsPublished &&
                  business.Status == BusinessStatus.Active && product.Id == productId && product.IsActive
            select new { business.Id, business.Slug, ProductId = product.Id, product.IsAvailable })
            .SingleOrDefaultAsync(cancellationToken);
        if (target is null)
            throw new ApiException("PRODUCT_NOT_FOUND", "No encontramos ese producto.", 404);
        if (target.IsAvailable)
            throw new ApiException("PRODUCT_ALREADY_AVAILABLE", "Este producto ya está disponible.", 409);
        var deepLink = $"/negocios/{Uri.EscapeDataString(target.Slug)}/pedidos#producto-{target.ProductId}";
        return await Upsert(target.Id, PushAudience.ProductRestock, target.ProductId.ToString("N"), request,
            null, target.ProductId, protector.Protect(deepLink), cancellationToken);
    }

    public async Task UnregisterProductRestockAsync(string businessSlug, Guid productId,
        WebPushUnsubscribeRequest request, CancellationToken cancellationToken = default)
    {
        var exists = await (from product in db.Products.AsNoTracking()
            join business in db.Businesses.AsNoTracking() on product.BusinessId equals business.Id
            where business.Slug == businessSlug && product.Id == productId
            select product.Id).AnyAsync(cancellationToken);
        if (!exists) throw new ApiException("PRODUCT_NOT_FOUND", "No encontramos ese producto.", 404);
        await Deactivate(PushAudience.ProductRestock, productId.ToString("N"), request.Endpoint,
            cancellationToken);
    }

    public async Task<WebPushSubscriptionDto> RegisterBusinessFollowerAsync(string businessSlug,
        WebPushSubscriptionRequest request, CancellationToken cancellationToken = default)
    {
        DemandConfigured(); Validate(request);
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == businessSlug &&
            x.IsPublished && x.Status == BusinessStatus.Active, cancellationToken)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos ese negocio.", 404);
        var deepLink = $"/negocios/{Uri.EscapeDataString(business.Slug)}";
        return await Upsert(business.Id, PushAudience.BusinessFollower, business.Id.ToString("N"), request,
            null, business.Id, protector.Protect(deepLink), cancellationToken);
    }

    public async Task UnregisterBusinessFollowerAsync(string businessSlug, WebPushUnsubscribeRequest request,
        CancellationToken cancellationToken = default)
    {
        var businessId = await db.Businesses.AsNoTracking().Where(x => x.Slug == businessSlug)
            .Select(x => x.Id).SingleOrDefaultAsync(cancellationToken);
        if (businessId == Guid.Empty)
            throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos ese negocio.", 404);
        await Deactivate(PushAudience.BusinessFollower, businessId.ToString("N"), request.Endpoint,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessPromotionDto>> GetPublicPromotionsAsync(
        CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var promotions = await db.BusinessPromotions.AsNoTracking()
            .Where(x => x.IsActive && x.StartsAtUtc <= now && x.EndsAtUtc > now)
            .OrderByDescending(x => x.StartsAtUtc).Take(40).ToListAsync(cancellationToken);
        return await ToPromotionDtos(promotions, publishedOnly: true, cancellationToken);
    }

    public async Task<IReadOnlyList<BusinessPromotionDto>> GetBusinessPromotionsAsync(Guid userId,
        Guid businessId, CancellationToken cancellationToken = default)
    {
        await DemandPromotionAccess(userId, businessId, cancellationToken);
        var promotions = await db.BusinessPromotions.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderByDescending(x => x.CreatedAtUtc).Take(50).ToListAsync(cancellationToken);
        return await ToPromotionDtos(promotions, publishedOnly: false, cancellationToken);
    }

    public async Task<BusinessPromotionSaveResultDto> SavePromotionAsync(Guid userId, Guid businessId,
        Guid? promotionId, SaveBusinessPromotionRequest request, CancellationToken cancellationToken = default)
    {
        await DemandPromotionAccess(userId, businessId, cancellationToken);
        var now = clock.GetUtcNow();
        BusinessPromotion promotion;
        try
        {
            if (promotionId is null)
            {
                promotion = new BusinessPromotion(Guid.NewGuid(), businessId, request.Headline, request.Body,
                    request.CtaLabel, request.DeepLink, request.StartsAtUtc, request.EndsAtUtc,
                    request.IsActive, now);
                db.BusinessPromotions.Add(promotion);
            }
            else
            {
                promotion = await db.BusinessPromotions.SingleOrDefaultAsync(x => x.BusinessId == businessId &&
                    x.Id == promotionId, cancellationToken)
                    ?? throw new ApiException("PROMOTION_NOT_FOUND", "No encontramos esa promoción.", 404);
                promotion.Update(request.Headline, request.Body, request.CtaLabel, request.DeepLink,
                    request.StartsAtUtc, request.EndsAtUtc, request.IsActive, request.Version, now);
            }
        }
        catch (DomainException ex)
        {
            throw new ApiException(ex.Code, ex.Message, ex.Code == "CONCURRENCY_CONFLICT" ? 409 : 400);
        }
        await db.SaveChangesAsync(cancellationToken);

        var sent = false;
        DateTimeOffset? nextAllowed = null;
        var message = "Promoción guardada. Sólo aparece durante su vigencia.";
        if (request.NotifyFollowers)
        {
            var lastSent = await db.BusinessPromotions.AsNoTracking()
                .Where(x => x.BusinessId == businessId && x.PushSentAtUtc != null)
                .MaxAsync(x => x.PushSentAtUtc, cancellationToken);
            nextAllowed = lastSent?.AddHours(24);
            if (nextAllowed > now)
            {
                message = $"Promoción guardada. El próximo aviso puede enviarse después de {nextAllowed:dd/MM HH:mm}.";
            }
            else if (!settings.IsConfigured)
            {
                message = "Promoción guardada. Push no está configurado en este entorno.";
            }
            else
            {
                var followers = await db.WebPushSubscriptions.AsNoTracking()
                    .CountAsync(x => x.BusinessId == businessId &&
                        x.Audience == PushAudience.BusinessFollower && x.EntityId == businessId &&
                        x.IsActive, cancellationToken);
                if (followers == 0)
                {
                    message = "Promoción guardada. El negocio todavía no tiene seguidores con avisos activos.";
                }
                else
                {
                    // Se encola, no se envía aquí. Antes, un proveedor lento dejaba el formulario
                    // colgado tantos segundos como seguidores hubiera, y un fallo a mitad enviaba a
                    // unos sí y a otros no sin dejar rastro de a quiénes.
                    await notifications.PublishAsync(new(businessId, NotificationAudience.Customer,
                        NotificationKind.PromotionPublished, promotion.Headline,
                        string.IsNullOrWhiteSpace(promotion.Body)
                            ? "Hay una novedad en el negocio que sigues." : promotion.Body,
                        promotion.DeepLink, TrackedEntities.Business, businessId,
                        Notification.Key(NotificationAudience.Customer,
                            NotificationKind.PromotionPublished, promotion.Id,
                            now.ToUnixTimeSeconds().ToString()),
                        PushAudience.BusinessFollower, Renotify: true), cancellationToken);
                    promotion.MarkPushSent(now);
                    await db.SaveChangesAsync(cancellationToken);
                    sent = true; nextAllowed = now.AddHours(24);
                    message = $"Promoción guardada y en camino a {followers} dispositivo(s) seguidor(es).";
                }
            }
        }

        var dto = (await ToPromotionDtos([promotion], publishedOnly: false, cancellationToken)).Single();
        return new(dto, sent, message, nextAllowed);
    }

    private async Task<WebPushSubscriptionDto> Upsert(Guid businessId, PushAudience audience, string scopeKey,
        WebPushSubscriptionRequest request, Guid? userId, Guid? entityId, string? protectedDeepLink,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var hash = Hash(request.Endpoint);
        var subscription = await db.WebPushSubscriptions.SingleOrDefaultAsync(x =>
            x.EndpointHash == hash && x.Audience == audience && x.ScopeKey == scopeKey, ct);
        if (subscription is null)
        {
            subscription = new StoredPushSubscription(Guid.NewGuid(), businessId, audience, scopeKey, hash,
                request.Endpoint.Trim(), request.Keys.P256dh.Trim(), request.Keys.Auth.Trim(), userId,
                entityId, protectedDeepLink, now);
            db.WebPushSubscriptions.Add(subscription);
        }
        else subscription.Refresh(request.Endpoint.Trim(), request.Keys.P256dh.Trim(), request.Keys.Auth.Trim(),
            userId, protectedDeepLink, now);
        await db.SaveChangesAsync(ct);
        return new(subscription.IsActive, subscription.UpdatedAtUtc);
    }

    private async Task Deactivate(PushAudience audience, string scopeKey, string endpoint, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        var hash = Hash(endpoint);
        var subscription = await db.WebPushSubscriptions.SingleOrDefaultAsync(x =>
            x.EndpointHash == hash && x.Audience == audience && x.ScopeKey == scopeKey, ct);
        if (subscription is null) return;
        subscription.Deactivate(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
    }

    private async Task DemandPromotionAccess(Guid userId, Guid businessId, CancellationToken ct)
    {
        var allowed = await db.BusinessMemberships.AsNoTracking().AnyAsync(x => x.UserId == userId &&
            x.BusinessId == businessId && x.IsActive &&
            (x.Role == MembershipRole.Owner || x.CanManageConfiguration), ct);
        if (!allowed)
            throw new ApiException("MEMBERSHIP_FORBIDDEN", "No tienes permiso para publicar promociones.", 403);
    }

    private async Task<IReadOnlyList<BusinessPromotionDto>> ToPromotionDtos(
        IReadOnlyList<BusinessPromotion> promotions, bool publishedOnly, CancellationToken ct)
    {
        if (promotions.Count == 0) return [];
        var businessIds = promotions.Select(x => x.BusinessId).Distinct().ToArray();
        var businesses = await (from business in db.Businesses.AsNoTracking()
            join municipality in db.Municipalities.AsNoTracking() on business.MunicipalityId equals municipality.Id
            join category in db.Categories.AsNoTracking() on business.CategoryId equals category.Id
            where businessIds.Contains(business.Id) && (!publishedOnly ||
                business.IsPublished && business.Status == BusinessStatus.Active)
            select new PromotionBusinessInfo(business.Id, business.Slug, business.Name,
                municipality.Slug, municipality.Name, category.Slug, category.Name))
            .ToDictionaryAsync(x => x.Id, ct);
        var covers = await db.BusinessImages.AsNoTracking()
            .Where(x => businessIds.Contains(x.BusinessId) && x.Kind == BusinessImageKind.Cover && !x.IsDeleted)
            .Select(x => new { x.BusinessId, x.StorageKey }).ToDictionaryAsync(x => x.BusinessId, ct);
        return promotions.Where(x => businesses.ContainsKey(x.BusinessId)).Select(x =>
        {
            var business = businesses[x.BusinessId];
            var imageUrl = covers.TryGetValue(x.BusinessId, out var cover) ? storage.PublicUrl(cover.StorageKey) : null;
            return new BusinessPromotionDto(x.Id, x.BusinessId, business.Slug, business.Name,
                new OptionDto(business.MunicipalitySlug, business.MunicipalityName),
                new OptionDto(business.CategorySlug, business.CategoryName), x.Headline, x.Body,
                x.CtaLabel, x.DeepLink, x.StartsAtUtc, x.EndsAtUtc, x.IsActive, x.PushSentAtUtc,
                imageUrl, x.Version);
        }).ToArray();
    }

    private sealed record PromotionBusinessInfo(Guid Id, string Slug, string Name,
        string MunicipalitySlug, string MunicipalityName, string CategorySlug, string CategoryName);

    private async Task<(Guid BusinessId, Guid EntityId)> ResolveClient(PushAudience audience, string code,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 20 or > 128)
            throw new ApiException("TRACKING_NOT_FOUND", "No encontramos esta operación.", 404);
        var hash = codes.Hash(code);
        return audience switch
        {
            PushAudience.Appointment => await db.Appointments.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => new ValueTuple<Guid, Guid>(x.BusinessId, x.Id))
                .SingleOrDefaultAsync(ct) is var a && a.Item2 != Guid.Empty ? a : NotFound(),
            PushAudience.QueueTicket => await db.QueueTickets.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => new ValueTuple<Guid, Guid>(x.BusinessId, x.Id))
                .SingleOrDefaultAsync(ct) is var q && q.Item2 != Guid.Empty ? q : NotFound(),
            PushAudience.PickupOrder => await db.PickupOrders.AsNoTracking()
                .Where(x => x.PublicCodeHash == hash).Select(x => new ValueTuple<Guid, Guid>(x.BusinessId, x.Id))
                .SingleOrDefaultAsync(ct) is var o && o.Item2 != Guid.Empty ? o : NotFound(),
            _ => throw new ApiException("INVALID_PUSH_SCOPE", "El tipo de aviso no es válido.")
        };
    }

    private static (Guid, Guid) NotFound()
        => throw new ApiException("TRACKING_NOT_FOUND", "No encontramos esta operación.", 404);

    private static void Validate(WebPushSubscriptionRequest request)
    {
        if (request.Keys is null || string.IsNullOrWhiteSpace(request.Endpoint) ||
            request.Endpoint.Length > 2048 ||
            !Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrWhiteSpace(request.Keys.P256dh) || request.Keys.P256dh.Length > 256 ||
            string.IsNullOrWhiteSpace(request.Keys.Auth) || request.Keys.Auth.Length > 256)
            throw new ApiException("INVALID_PUSH_SUBSCRIPTION", "La suscripción del navegador no es válida.");
    }

    private void DemandConfigured()
    {
        if (!settings.IsConfigured)
            throw new ApiException("PUSH_NOT_CONFIGURED", "Los avisos todavía no están disponibles.", 503);
    }

    private static string Hash(string endpoint)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint.Trim()))).ToLowerInvariant();
}
