namespace UrabaConecta.Domain;

public enum PickupOrderStatus
{
    Pending, Accepted, Rejected, Preparing, ReadyForPickup, Delivered, Cancelled
}

public sealed class ProductCategory : IBusinessOwned
{
    private ProductCategory() { }
    public ProductCategory(Guid id, Guid businessId, string name, int displayOrder = 0)
    {
        Validate(name, displayOrder);
        (Id, BusinessId, Name, DisplayOrder) = (id, businessId, name.Trim(), displayOrder);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = "";
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    public long Version { get; private set; }
    public void Update(string name, int displayOrder, bool isActive, long expectedVersion)
    {
        EnsureVersion(expectedVersion); Validate(name, displayOrder);
        Name = name.Trim(); DisplayOrder = displayOrder; IsActive = isActive; Version++;
    }
    private static void Validate(string name, int order)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100 || order < 0)
            throw new DomainException("INVALID_PRODUCT_CATEGORY", "Los datos de la categoría no son válidos.");
    }
    private void EnsureVersion(long expected)
    {
        if (expected != Version) throw new DomainException("CONCURRENCY_CONFLICT", "La categoría cambió. Recargue la información.");
    }
}

public sealed class Product : IBusinessOwned
{
    private Product() { }
    public Product(Guid id, Guid businessId, Guid productCategoryId, string name, string? description,
        decimal referencePrice, int displayOrder = 0, bool isAvailable = true)
    {
        Validate(name, description, referencePrice, displayOrder);
        (Id, BusinessId, ProductCategoryId, Name, Description, ReferencePrice, DisplayOrder, IsAvailable) =
            (id, businessId, productCategoryId, name.Trim(), description?.Trim() ?? "", referencePrice,
                displayOrder, isAvailable);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid ProductCategoryId { get; private set; }
    public string Name { get; private set; } = "";
    public string Description { get; private set; } = "";
    public decimal ReferencePrice { get; private set; }
    public int DisplayOrder { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>
    /// Visible y disponible son decisiones distintas: un producto agotado debe seguir en la tienda
    /// para que una persona pueda pedir que le avisemos cuando vuelva.
    /// </summary>
    public bool IsAvailable { get; private set; } = true;
    public long Version { get; private set; }
    public void EnsureAvailable()
    {
        if (!IsActive || !IsAvailable)
            throw new DomainException("PRODUCT_UNAVAILABLE", $"{Name} ya no está disponible.");
    }
    public void Update(Guid categoryId, string name, string? description, decimal price, int order,
        bool active, bool available, long expectedVersion)
    {
        if (expectedVersion != Version) throw new DomainException("CONCURRENCY_CONFLICT", "El producto cambió. Recargue la información.");
        Validate(name, description, price, order);
        ProductCategoryId = categoryId; Name = name.Trim(); Description = description?.Trim() ?? "";
        ReferencePrice = price; DisplayOrder = order; IsActive = active; IsAvailable = available; Version++;
    }
    private static void Validate(string name, string? description, decimal price, int order)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || (description?.Length ?? 0) > 500 ||
            price < 0 || price > 100_000_000 || order < 0)
            throw new DomainException("INVALID_PRODUCT", "Los datos del producto no son válidos.");
    }
}

public sealed class PickupOrderSettings : IBusinessOwned
{
    private PickupOrderSettings() { }
    public PickupOrderSettings(Guid id, Guid businessId, bool isEnabled, string? publicMessage,
        int minimumPreparationMinutes, int slotIntervalMinutes, int maximumActivePerSlot,
        TimeOnly receivesFrom, TimeOnly receivesUntil, int nextOrderNumber = 1001)
    {
        Validate(publicMessage, minimumPreparationMinutes, slotIntervalMinutes, maximumActivePerSlot,
            receivesFrom, receivesUntil, nextOrderNumber);
        (Id, BusinessId, IsEnabled, PublicMessage, MinimumPreparationMinutes, SlotIntervalMinutes,
            MaximumActivePerSlot, ReceivesFrom, ReceivesUntil, NextOrderNumber) =
            (id, businessId, isEnabled, publicMessage?.Trim() ?? "", minimumPreparationMinutes,
                slotIntervalMinutes, maximumActivePerSlot, receivesFrom, receivesUntil, nextOrderNumber);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public bool IsEnabled { get; private set; }
    public string PublicMessage { get; private set; } = "";
    public int MinimumPreparationMinutes { get; private set; }
    public int SlotIntervalMinutes { get; private set; }
    public int MaximumActivePerSlot { get; private set; }
    public TimeOnly ReceivesFrom { get; private set; }
    public TimeOnly ReceivesUntil { get; private set; }
    public int NextOrderNumber { get; private set; }
    public long Version { get; private set; }
    public int AllocateNumber() { var result = NextOrderNumber; NextOrderNumber++; Version++; return result; }
    public void Update(bool enabled, string? message, int preparation, int interval, int capacity,
        TimeOnly from, TimeOnly until, long expectedVersion)
    {
        if (expectedVersion != Version) throw new DomainException("CONCURRENCY_CONFLICT", "La configuración cambió.");
        Validate(message, preparation, interval, capacity, from, until, NextOrderNumber);
        IsEnabled = enabled; PublicMessage = message?.Trim() ?? ""; MinimumPreparationMinutes = preparation;
        SlotIntervalMinutes = interval; MaximumActivePerSlot = capacity; ReceivesFrom = from; ReceivesUntil = until; Version++;
    }
    private static void Validate(string? message, int prep, int interval, int capacity, TimeOnly from, TimeOnly until, int next)
    {
        if ((message?.Length ?? 0) > 200 || prep is < 0 or > 1440 || interval is < 5 or > 240 ||
            capacity is < 1 or > 500 || until <= from || next < 1)
            throw new DomainException("INVALID_ORDER_SETTINGS", "La configuración de pedidos no es válida.");
    }
}

public sealed class PickupOrder : IBusinessOwned
{
    private PickupOrder() { }
    public PickupOrder(Guid id, Guid businessId, int number, DateTimeOffset pickupStartUtc,
        DateTimeOffset pickupEndUtc, string protectedAlias, string protectedPhone, string phoneLast4,
        string? protectedNotes, string publicCodeHash, string consentVersion, DateTimeOffset acceptedAtUtc,
        DateTimeOffset createdAtUtc, IEnumerable<PickupOrderLine> lines)
    {
        var list = lines.ToList();
        if (number < 1 || pickupEndUtc <= pickupStartUtc || list.Count == 0 ||
            string.IsNullOrWhiteSpace(protectedAlias) || string.IsNullOrWhiteSpace(protectedPhone) ||
            phoneLast4.Length != 4 || string.IsNullOrWhiteSpace(publicCodeHash) ||
            string.IsNullOrWhiteSpace(consentVersion))
            throw new DomainException("INVALID_PICKUP_ORDER", "Los datos del pedido no son válidos.");
        (Id, BusinessId, PublicOrderNumber, PickupStartUtc, PickupEndUtc, ProtectedCustomerAlias,
            ProtectedCustomerPhone, PhoneLast4, ProtectedNotes, PublicCodeHash, ConsentVersion,
            ConsentAcceptedAtUtc, CreatedAtUtc, UpdatedAtUtc) =
            (id, businessId, number, pickupStartUtc, pickupEndUtc, protectedAlias, protectedPhone,
                phoneLast4, protectedNotes, publicCodeHash, consentVersion, acceptedAtUtc, createdAtUtc, createdAtUtc);
        _lines.AddRange(list);
        Subtotal = Total = list.Sum(x => x.LineTotal);
    }
    private readonly List<PickupOrderLine> _lines = [];
    public IReadOnlyCollection<PickupOrderLine> Lines => _lines;
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public int PublicOrderNumber { get; private set; }
    public DateTimeOffset PickupStartUtc { get; private set; }
    public DateTimeOffset PickupEndUtc { get; private set; }
    public string ProtectedCustomerAlias { get; private set; } = "";
    public string ProtectedCustomerPhone { get; private set; } = "";
    public string PhoneLast4 { get; private set; } = "";
    public string? ProtectedNotes { get; private set; }
    public string PublicCodeHash { get; private set; } = "";
    public string ConsentVersion { get; private set; } = "";
    public DateTimeOffset ConsentAcceptedAtUtc { get; private set; }
    public PickupOrderStatus Status { get; private set; } = PickupOrderStatus.Pending;
    public decimal Subtotal { get; private set; }
    public decimal Total { get; private set; }
    public string? CancellationReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }
    public bool CanPublicCancel => Status is PickupOrderStatus.Pending or PickupOrderStatus.Accepted;

    public void Transition(PickupOrderStatus target, DateTimeOffset now, long expectedVersion, string? reason = null)
    {
        if (expectedVersion != Version) throw new DomainException("CONCURRENCY_CONFLICT", "El pedido cambió. Recargue la información.");
        var allowed = (Status, target) switch
        {
            (PickupOrderStatus.Pending, PickupOrderStatus.Accepted or PickupOrderStatus.Rejected or PickupOrderStatus.Cancelled) => true,
            (PickupOrderStatus.Accepted, PickupOrderStatus.Preparing or PickupOrderStatus.Cancelled) => true,
            (PickupOrderStatus.Preparing, PickupOrderStatus.ReadyForPickup or PickupOrderStatus.Cancelled) => true,
            (PickupOrderStatus.ReadyForPickup, PickupOrderStatus.Delivered or PickupOrderStatus.Cancelled) => true,
            _ => false
        };
        if (!allowed) throw new DomainException("INVALID_ORDER_TRANSITION", $"No se puede pasar de {Status} a {target}.");
        if (target is PickupOrderStatus.Rejected or PickupOrderStatus.Cancelled &&
            string.IsNullOrWhiteSpace(reason))
            throw new DomainException("ORDER_REASON_REQUIRED", "Registre el motivo.");
        if ((reason?.Length ?? 0) > 160) throw new DomainException("INVALID_ORDER_REASON", "El motivo supera 160 caracteres.");
        Status = target; CancellationReason = reason?.Trim(); UpdatedAtUtc = now; Version++;
    }
}

public sealed class PickupOrderLine : IBusinessOwned
{
    private PickupOrderLine() { }
    public PickupOrderLine(Guid id, Guid businessId, Guid orderId, Guid productId, string productName,
        decimal unitPrice, int quantity, string? protectedNotes = null)
    {
        if (string.IsNullOrWhiteSpace(productName) || unitPrice < 0 || quantity is < 1 or > 20)
            throw new DomainException("INVALID_ORDER_LINE", "La línea del pedido no es válida.");
        (Id, BusinessId, PickupOrderId, ProductId, ProductNameSnapshot, UnitPriceSnapshot, Quantity,
            ProtectedNotes, LineTotal) = (id, businessId, orderId, productId, productName.Trim(), unitPrice,
                quantity, protectedNotes, unitPrice * quantity);
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid PickupOrderId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string ProductNameSnapshot { get; private set; } = "";
    public decimal UnitPriceSnapshot { get; private set; }
    public int Quantity { get; private set; }
    public string? ProtectedNotes { get; private set; }
    public decimal LineTotal { get; private set; }
}
