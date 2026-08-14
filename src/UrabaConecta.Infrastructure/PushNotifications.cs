using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    IPersonalDataProtector protector, IWebPushTransport transport, IOptions<WebPushOptions> options,
    TimeProvider clock, ILogger<PushNotificationService> logger) : IPushNotificationService
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
        var confirmation = audience switch
        {
            PushAudience.Appointment => new PushMessage("Cita registrada",
                "Te avisaremos aquí cuando haya una novedad útil sobre tu cita.", deepLink,
                $"appointment-{target.EntityId}"),
            PushAudience.QueueTicket => new PushMessage("Turno registrado",
                "Te avisaremos cuando tu turno esté cerca y cuando te llamen.", deepLink,
                $"queue-{target.EntityId}"),
            _ => new PushMessage("Pedido recibido",
                "Te avisaremos cuando tu pedido esté listo para recoger.", deepLink,
                $"order-{target.EntityId}")
        };
        await NotifyClientAsync(audience, target.EntityId, confirmation, cancellationToken);
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

    public async Task NotifyBusinessAsync(Guid businessId, PushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured) return;
        var subscriptions = await db.WebPushSubscriptions.Where(x => x.BusinessId == businessId &&
                x.Audience == PushAudience.Owner && x.IsActive && x.UserId != null &&
                db.BusinessMemberships.Any(m => m.BusinessId == businessId && m.UserId == x.UserId && m.IsActive))
            .ToListAsync(cancellationToken);
        await Deliver(subscriptions, message, cancellationToken);
    }

    public async Task NotifyClientAsync(PushAudience audience, Guid entityId, PushMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!settings.IsConfigured || audience == PushAudience.Owner) return;
        var subscriptions = await db.WebPushSubscriptions
            .Where(x => x.Audience == audience && x.EntityId == entityId && x.IsActive)
            .ToListAsync(cancellationToken);
        foreach (var subscription in subscriptions)
        {
            var link = subscription.ProtectedDeepLink is null
                ? message.Url : protector.Unprotect(subscription.ProtectedDeepLink);
            await Deliver([subscription], message with { Url = link }, cancellationToken);
        }
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

    private async Task Deliver(IReadOnlyList<StoredPushSubscription> subscriptions, PushMessage message,
        CancellationToken ct)
    {
        foreach (var subscription in subscriptions)
        {
            try
            {
                await transport.SendAsync(subscription, message, ct);
                subscription.MarkDelivered(clock.GetUtcNow());
            }
            catch (PushDeliveryException ex)
            {
                var expired = ex.StatusCode is (int)HttpStatusCode.NotFound or (int)HttpStatusCode.Gone;
                subscription.MarkFailed(clock.GetUtcNow(), expired);
                logger.LogWarning(ex, "Web Push rechazado con {StatusCode}; suscripción {SubscriptionId}.",
                    ex.StatusCode, subscription.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                subscription.MarkFailed(clock.GetUtcNow(), false);
                logger.LogWarning(ex, "Falló Web Push para la suscripción {SubscriptionId}.", subscription.Id);
            }
        }
        if (subscriptions.Count > 0) await db.SaveChangesAsync(ct);
    }

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
