using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed class OrderingUseCases(IOrderingStore store, IPublicCodeService codes,
    IPersonalDataProtector protector, IConsentPolicyProvider consentPolicy,
    IObjectStorage storage, IPushNotificationService push, TimeProvider clock) : IOrderingUseCases
{
    public async Task<PickupMenuDto?> GetMenuAsync(string slug, CancellationToken ct = default)
    {
        var context = await store.GetPublicContextAsync(slug, ct);
        if (context is null) return null;
        var found = context.Value;
        var categories = await store.GetCategoriesAsync(found.Business.Id, true, ct);
        var products = await store.GetProductsAsync(found.Business.Id, true, ct);
        var photos = await store.GetProductPhotosAsync(found.Business.Id, ct);
        return new(found.Business.Name, slug, found.Settings.PublicMessage,
            categories.Select(CategoryDto).ToList(), products.Select(x => ProductDto(x, photos)).ToList());
    }

    public async Task<PickupSlotListDto> GetSlotsAsync(string slug, DateOnly? date = null, CancellationToken ct = default)
    {
        var context = await store.GetPublicContextAsync(slug, ct)
            ?? throw new ApiException("ORDERING_NOT_AVAILABLE", "Los pedidos para recoger no están disponibles.", 404);
        var business = context.Business;
        var settings = context.Settings;
        var hours = await store.GetHoursAsync(business.Id, ct);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var now = clock.GetUtcNow();
        var earliest = now.AddMinutes(settings.MinimumPreparationMinutes);
        var firstDate = date ?? DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        var dates = date.HasValue ? [firstDate] : Enumerable.Range(0, 7).Select(firstDate.AddDays).ToArray();
        var result = new List<PickupSlotDto>();
        foreach (var day in dates)
        {
            // Un día puede tener varios tramos. Se recorre cada uno por separado, así que entre
            // 14:00 y 17:00 —la pausa— no se genera ninguna franja.
            var intervals = BusinessSchedule.Normalize(hours.Where(x => x.Day == day.DayOfWeek)
                .Select(x => new ScheduleInterval(x.OpensAt, x.ClosesAt)));
            foreach (var interval in intervals)
            {
                var from = interval.OpensAt > settings.ReceivesFrom ? interval.OpensAt : settings.ReceivesFrom;
                var until = interval.ClosesAt < settings.ReceivesUntil ? interval.ClosesAt : settings.ReceivesUntil;
                if (until <= from) continue;
                for (var local = day.ToDateTime(from); local.AddMinutes(settings.SlotIntervalMinutes) <= day.ToDateTime(until);
                     local = local.AddMinutes(settings.SlotIntervalMinutes))
                {
                    var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), zone));
                    if (start < earliest) continue;
                    var count = await store.CountActiveInSlotAsync(business.Id, start, ct);
                    if (count < settings.MaximumActivePerSlot)
                        result.Add(new(start, start.AddMinutes(settings.SlotIntervalMinutes), settings.MaximumActivePerSlot - count));
                }
            }
        }
        return new(business.TimeZoneId, result);
    }

    public async Task<PickupOrderCreatedDto> CreateAsync(string slug, CreatePickupOrderRequest request,
        CancellationToken ct = default)
    {
        if (!request.ConsentAccepted || request.ConsentNoticeVersion != consentPolicy.CurrentVersion)
            throw new ApiException("CONSENT_REQUIRED", "Debe aceptar la versión vigente del aviso de tratamiento de datos.");
        if (request.Lines.Count == 0) throw new ApiException("ORDER_LINES_REQUIRED", "Agregue al menos un producto.");
        var context = await store.GetPublicContextAsync(slug, ct)
            ?? throw new ApiException("ORDERING_NOT_AVAILABLE", "Los pedidos para recoger no están disponibles.", 404);
        await using var tx = await store.BeginTransactionAsync(ct);
        var settings = await store.LockSettingsAsync(context.Business.Id, ct)
            ?? throw new ApiException("ORDERING_NOT_AVAILABLE", "Los pedidos para recoger no están disponibles.", 404);
        await store.LockSlotAsync(context.Business.Id, request.PickupStart, ct);
        var validSlots = await GetSlotsAsync(slug, DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(request.PickupStart, TimeZoneInfo.FindSystemTimeZoneById(context.Business.TimeZoneId)).Date), ct);
        var slot = validSlots.Slots.SingleOrDefault(x => x.Start == request.PickupStart);
        if (slot is null) throw new ApiException("PICKUP_SLOT_UNAVAILABLE", "La franja seleccionada ya no está disponible.", 409);
        if (await store.CountActiveInSlotAsync(context.Business.Id, request.PickupStart, ct) >= settings.MaximumActivePerSlot)
            throw new ApiException("PICKUP_SLOT_FULL", "La franja alcanzó su capacidad.", 409);
        var requested = request.Lines.GroupBy(x => x.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity),
                Notes = string.Join(" · ", g.Select(x => x.Notes).Where(x => !string.IsNullOrWhiteSpace(x))) }).ToList();
        if (requested.Any(x => x.Quantity is < 1 or > 20))
            throw new ApiException("INVALID_ORDER_QUANTITY", "Cada producto admite entre 1 y 20 unidades.");
        var products = await store.GetProductsAsync(context.Business.Id, false, ct);
        var orderId = Guid.NewGuid();
        var lines = requested.Select(x =>
        {
            var product = products.SingleOrDefault(p => p.Id == x.ProductId)
                ?? throw new ApiException("PRODUCT_NOT_FOUND", "Un producto no pertenece a este establecimiento.", 404);
            TryDomain(product.EnsureAvailable);
            return new PickupOrderLine(Guid.NewGuid(), context.Business.Id, orderId, product.Id,
                product.Name, product.ReferencePrice, x.Quantity,
                string.IsNullOrWhiteSpace(x.Notes) ? null : protector.Protect(x.Notes));
        }).ToList();
        var code = codes.Generate();
        var now = clock.GetUtcNow();
        var phoneDigits = new string(request.Phone.Where(char.IsDigit).ToArray());
        var order = TryDomain(() => new PickupOrder(orderId, context.Business.Id, settings.AllocateNumber(),
            slot.Start, slot.End, protector.Protect(request.CustomerAlias.Trim()), protector.Protect(request.Phone.Trim()),
            phoneDigits[^4..], string.IsNullOrWhiteSpace(request.Notes) ? null : protector.Protect(request.Notes.Trim()),
            code.Hash, request.ConsentNoticeVersion, now, now, lines));
        var consent = new ConsentReceipt(Guid.NewGuid(), order.BusinessId, request.ConsentNoticeVersion,
            "Gestionar el pedido para recoger y contactar a la persona solicitante.", now);
        consent.LinkPickupOrder(order.Id);
        store.AddOrder(order); store.AddConsent(consent);
        await store.SaveChangesAsync(ct); await tx.CommitAsync(ct);
        await push.NotifyBusinessAsync(order.BusinessId, new("Nuevo pedido para recoger",
            $"Pedido #{order.PublicOrderNumber} por {order.Total:C0}.",
            $"/panel/{order.BusinessId}/pedidos#order-{order.Id}",
            $"business-order-{order.Id}"), ct);
        return new(order.PublicOrderNumber, code.PlainText, order.Status.ToString(), order.Total, order.PickupStartUtc);
    }

    public async Task<PickupOrderTrackingDto?> TrackAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var order = await store.FindByCodeAsync(codes.Hash(code), ct);
        if (order is null) return null;
        var business = await store.GetBusinessAsync(order.BusinessId, ct);
        return TrackingDto(order, business!.Name);
    }

    public async Task CancelPublicAsync(string code, long version, CancellationToken ct = default)
    {
        var order = await store.FindByCodeAsync(codes.Hash(code), ct)
            ?? throw new ApiException("ORDER_NOT_FOUND", "No encontramos el pedido.", 404);
        if (!order.CanPublicCancel)
            throw new ApiException("ORDER_CANNOT_CANCEL", "El pedido ya no puede cancelarse desde el enlace público.", 409);
        TryDomain(() => order.Transition(PickupOrderStatus.Cancelled, clock.GetUtcNow(), version, "Cancelado por el cliente"));
        await store.SaveChangesAsync(ct);
    }

    public async Task<PickupOrderSettingsDto> GetSettingsAsync(Guid userId, Guid businessId, CancellationToken ct = default)
    {
        await DemandConfiguration(userId, businessId, ct);
        return SettingsDto(await store.GetSettingsAsync(businessId, ct)
            ?? throw new ApiException("ORDERING_NOT_CONFIGURED", "El módulo no está configurado.", 404));
    }

    public async Task<PickupOrderSettingsDto> SaveSettingsAsync(Guid userId, Guid businessId,
        SavePickupOrderSettingsRequest request, CancellationToken ct = default)
    {
        await DemandConfiguration(userId, businessId, ct);
        var settings = await store.GetSettingsAsync(businessId, ct);
        if (settings is null)
        {
            settings = TryDomain(() => new PickupOrderSettings(Guid.NewGuid(), businessId, request.IsEnabled,
                request.PublicMessage, request.MinimumPreparationMinutes, request.SlotIntervalMinutes,
                request.MaximumActivePerSlot, request.ReceivesFrom, request.ReceivesUntil));
            store.AddSettings(settings);
        }
        else TryDomain(() => settings.Update(request.IsEnabled, request.PublicMessage,
            request.MinimumPreparationMinutes, request.SlotIntervalMinutes, request.MaximumActivePerSlot,
            request.ReceivesFrom, request.ReceivesUntil, request.Version));
        await store.SaveChangesAsync(ct); return SettingsDto(settings);
    }

    public async Task<IReadOnlyList<ProductCategoryDto>> GetCategoriesAsync(Guid userId, Guid businessId,
        CancellationToken ct = default)
    {
        await DemandConfiguration(userId, businessId, ct);
        return (await store.GetCategoriesAsync(businessId, false, ct)).Select(CategoryDto).ToList();
    }
    public async Task<ProductCategoryDto> SaveCategoryAsync(Guid userId, Guid businessId, Guid? categoryId,
        SaveProductCategoryRequest request, CancellationToken ct = default)
    {
        await DemandConfiguration(userId, businessId, ct);
        ProductCategory category;
        if (categoryId is null) { category = TryDomain(() => new ProductCategory(Guid.NewGuid(), businessId, request.Name, request.DisplayOrder)); store.AddCategory(category); }
        else
        {
            category = await store.GetCategoryAsync(businessId, categoryId.Value, ct)
                ?? throw new ApiException("PRODUCT_CATEGORY_NOT_FOUND", "No encontramos la categoría.", 404);
            TryDomain(() => category.Update(request.Name, request.DisplayOrder, request.IsActive, request.Version));
        }
        await store.SaveChangesAsync(ct); return CategoryDto(category);
    }
    public async Task<IReadOnlyList<ProductDto>> GetProductsAsync(Guid userId, Guid businessId,
        CancellationToken ct = default)
    {
        await DemandConfiguration(userId, businessId, ct);
        var photos = await store.GetProductPhotosAsync(businessId, ct);
        return (await store.GetProductsAsync(businessId, false, ct))
            .Select(x => ProductDto(x, photos)).ToList();
    }
    public async Task<ProductDto> SaveProductAsync(Guid userId, Guid businessId, Guid? productId,
        SaveProductRequest request, CancellationToken ct = default)
    {
        await DemandConfiguration(userId, businessId, ct);
        var category = await store.GetCategoryAsync(businessId, request.CategoryId, ct);
        if (category is null) throw new ApiException("PRODUCT_CATEGORY_NOT_FOUND", "La categoría no pertenece al establecimiento.", 404);
        Product product;
        if (productId is null) { product = TryDomain(() => new Product(Guid.NewGuid(), businessId, category.Id, request.Name, request.Description, request.ReferencePrice, request.DisplayOrder)); store.AddProduct(product); }
        else
        {
            product = await store.GetProductAsync(businessId, productId.Value, ct)
                ?? throw new ApiException("PRODUCT_NOT_FOUND", "No encontramos el producto.", 404);
            TryDomain(() => product.Update(category.Id, request.Name, request.Description, request.ReferencePrice,
                request.DisplayOrder, request.IsActive, request.Version));
        }
        await store.SaveChangesAsync(ct); return ProductDto(product);
    }

    public async Task<PickupOrderBoardDto> ListOrdersAsync(Guid userId, Guid businessId,
        string? status, DateOnly? date, CancellationToken ct = default)
    {
        await DemandOrders(userId, businessId, ct);
        // A diferencia de citas y turnos, aquí el negocio no estaba resuelto: es una lectura más por
        // pantalla —no por pedido— y es lo que permite que quien entra directo por dirección sepa a
        // qué establecimiento pertenecen estos pedidos y en qué hora está leyendo las recogidas.
        var business = await store.GetBusinessAsync(businessId, ct)
            ?? throw new ApiException("BUSINESS_NOT_FOUND", "No encontramos el establecimiento.", 404);
        var orders = await store.ListOrdersAsync(businessId, status, date, ct);
        return new(businessId, business.Name, business.TimeZoneId, orders.Select(AdminDto).ToList());
    }
    public async Task<PickupOrderAdminDto> ChangeStatusAsync(Guid userId, Guid businessId, Guid orderId,
        string action, PickupOrderCommandRequest request, CancellationToken ct = default)
    {
        await DemandOrders(userId, businessId, ct);
        var order = await store.GetOrderAsync(businessId, orderId, ct)
            ?? throw new ApiException("ORDER_NOT_FOUND", "No encontramos el pedido en este establecimiento.", 404);
        var target = action.ToLowerInvariant() switch
        {
            "accept" => PickupOrderStatus.Accepted, "reject" => PickupOrderStatus.Rejected,
            "prepare" => PickupOrderStatus.Preparing, "ready" => PickupOrderStatus.ReadyForPickup,
            "deliver" => PickupOrderStatus.Delivered, "cancel" => PickupOrderStatus.Cancelled,
            _ => throw new ApiException("INVALID_ORDER_ACTION", "La acción no es válida.")
        };
        TryDomain(() => order.Transition(target, clock.GetUtcNow(), request.Version, request.Reason));
        await store.SaveChangesAsync(ct);
        if (target == PickupOrderStatus.ReadyForPickup)
            await push.NotifyClientAsync(PushAudience.PickupOrder, order.Id,
                new("Pedido listo para recoger", $"Tu pedido #{order.PublicOrderNumber} ya está listo.", "",
                    $"order-{order.Id}", true), ct);
        return AdminDto(order);
    }

    private PickupOrderAdminDto AdminDto(PickupOrder o) => new(o.Id, o.PublicOrderNumber, o.Status.ToString(),
        protector.Unprotect(o.ProtectedCustomerAlias), protector.Unprotect(o.ProtectedCustomerPhone),
        o.ProtectedNotes is null ? null : protector.Unprotect(o.ProtectedNotes), o.PickupStartUtc, o.Total,
        o.Lines.Select(LineDto).ToList(), o.CancellationReason, o.CreatedAtUtc, o.UpdatedAtUtc, o.Version);
    private PickupOrderTrackingDto TrackingDto(PickupOrder o, string businessName) => new(o.PublicOrderNumber,
        o.Status.ToString(), StatusLabel(o.Status), businessName, o.PickupStartUtc, o.Total, $"***{o.PhoneLast4}",
        o.Lines.Select(x => new PickupOrderLineDto(x.ProductId, x.ProductNameSnapshot, x.UnitPriceSnapshot,
            x.Quantity, x.LineTotal, null)).ToList(), o.CanPublicCancel, o.UpdatedAtUtc, o.Version);
    private PickupOrderLineDto LineDto(PickupOrderLine x) => new(x.ProductId, x.ProductNameSnapshot,
        x.UnitPriceSnapshot, x.Quantity, x.LineTotal,
        x.ProtectedNotes is null ? null : protector.Unprotect(x.ProtectedNotes));
    private static string StatusLabel(PickupOrderStatus s) => s switch
    {
        PickupOrderStatus.Pending => "Pendiente", PickupOrderStatus.Accepted => "Aceptado",
        PickupOrderStatus.Rejected => "Rechazado", PickupOrderStatus.Preparing => "En preparación",
        PickupOrderStatus.ReadyForPickup => "Listo para recoger", PickupOrderStatus.Delivered => "Entregado",
        _ => "Cancelado"
    };
    private async Task DemandOrders(Guid userId, Guid businessId, CancellationToken ct)
    {
        if (!await store.CanManageOrdersAsync(userId, businessId, ct))
            throw new ApiException("MEMBERSHIP_FORBIDDEN", "No tiene permiso para administrar pedidos.", 403);
        await DemandModule(businessId, ct);
    }
    private async Task DemandConfiguration(Guid userId, Guid businessId, CancellationToken ct)
    {
        if (!await store.CanManageConfigurationAsync(userId, businessId, ct))
            throw new ApiException("MEMBERSHIP_FORBIDDEN", "No tiene permiso para configurar el catálogo.", 403);
        // El catálogo pertenece a pedidos: sin el módulo no hay nada que configurar.
        await DemandModule(businessId, ct);
    }
    /// <summary>Ocultar el botón no basta: una URL directa llegaba igual al módulo no habilitado.</summary>
    private async Task DemandModule(Guid businessId, CancellationToken ct)
    {
        if (!await store.IsModuleEnabledAsync(businessId, BusinessModuleKind.PickupOrders, ct))
            throw new ApiException("MODULE_DISABLED", "Este establecimiento no tiene pedidos habilitados.", 403);
    }
    private static ProductCategoryDto CategoryDto(ProductCategory x) => new(x.Id, x.Name, x.DisplayOrder, x.IsActive, x.Version);
    private ProductDto ProductDto(Product x, IReadOnlyDictionary<Guid, CatalogPhoto>? photos = null)
    {
        var photo = photos?.GetValueOrDefault(x.Id);
        return new(x.Id, x.ProductCategoryId, x.Name, x.Description, x.ReferencePrice, x.DisplayOrder,
            x.IsActive, x.Version,
            photo is null ? null : storage.PublicUrl(photo.StorageKey), photo?.AltText);
    }
    private static PickupOrderSettingsDto SettingsDto(PickupOrderSettings x) => new(x.Id, x.BusinessId,
        x.IsEnabled, x.PublicMessage, x.MinimumPreparationMinutes, x.SlotIntervalMinutes,
        x.MaximumActivePerSlot, x.ReceivesFrom, x.ReceivesUntil, x.NextOrderNumber, x.Version);
    private static void TryDomain(Action action) { try { action(); } catch (DomainException e) { throw Convert(e); } }
    private static T TryDomain<T>(Func<T> action) { try { return action(); } catch (DomainException e) { throw Convert(e); } }
    private static ApiException Convert(DomainException e)
        => new(e.Code, e.Message, e.Code is "CONCURRENCY_CONFLICT" or "INVALID_ORDER_TRANSITION" ? 409 : 400);
}

