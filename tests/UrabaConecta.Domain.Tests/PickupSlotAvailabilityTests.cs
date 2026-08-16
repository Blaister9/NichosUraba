using UrabaConecta.Application;
using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

/// <summary>
/// La disponibilidad pública de recogida. Lo que se fija aquí es que construir muchas franjas
/// cueste una sola lectura de ocupación, y que esa lectura devuelva lo mismo que devolvía el COUNT
/// por franja que había antes. La tienda falsa cuenta cómo se le pregunta, así que la desaparición
/// del N+1 se comprueba por comportamiento y no por el SQL: de eso se encargan las pruebas de
/// integración, que además son las que ejercen el bloqueo real al confirmar un pedido.
/// </summary>
public sealed class PickupSlotAvailabilityTests
{
    // Domingo 26 de julio de 2026, 11:00 en Bogotá. La tienda abre a las 9:00, así que el primer
    // día ya empieza recortado: es el caso normal y conviene que el escenario lo tenga.
    private static readonly DateTimeOffset Ahora = new(2026, 7, 26, 16, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Lunes = new(2026, 7, 27);
    /// <summary>Las 9:00 del lunes en Bogotá, que es la primera franja de esa jornada.</summary>
    private static readonly DateTimeOffset PrimeraDelLunes = new(2026, 7, 27, 14, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------------ desaparición del N+1

    [Fact]
    public async Task Seven_days_of_slots_cost_a_single_occupancy_read()
    {
        var store = new FakeOrderingStore();

        var slots = await Sut(store).GetSlotsAsync("tienda");

        // Horario de 9 a 18 y franjas de 15 minutos: siete días pasan de doscientas franjas.
        Assert.True(slots.Slots.Count > 200, $"El escenario sólo generó {slots.Slots.Count} franjas.");
        Assert.Equal(1, store.BatchReads);
        // Ni una sola consulta puntual. Ése era exactamente el N+1.
        Assert.Equal(0, store.PointReads);
    }

    [Fact]
    public async Task Asking_for_more_days_does_not_add_reads()
    {
        var unDia = new FakeOrderingStore();
        var sieteDias = new FakeOrderingStore();

        var deUnDia = await Sut(unDia).GetSlotsAsync("tienda", Lunes);
        var deSieteDias = await Sut(sieteDias).GetSlotsAsync("tienda");

        // El coste no depende del número de franjas: el mismo para una jornada y para siete.
        Assert.Equal(1, unDia.BatchReads);
        Assert.Equal(1, sieteDias.BatchReads);
        Assert.Equal(0, unDia.PointReads + sieteDias.PointReads);
        Assert.True(deSieteDias.Slots.Count > deUnDia.Slots.Count * 5,
            "Siete días deberían devolver bastantes más franjas que uno.");
    }

    [Fact]
    public async Task The_single_read_covers_the_whole_range_instead_of_one_call_per_day()
    {
        var store = new FakeOrderingStore();

        var slots = await Sut(store).GetSlotsAsync("tienda");

        var rango = Assert.Single(store.RangesRequested);
        Assert.Equal(slots.Slots[0].Start, rango.From);
        Assert.Equal(slots.Slots[^1].Start, rango.To);
        // Más de cinco días entre los extremos: no se resolvió día por día.
        Assert.True(rango.To - rango.From > TimeSpan.FromDays(5), $"El rango leído fue de {rango.To - rango.From}.");
    }

    [Fact]
    public async Task A_closed_week_asks_for_no_occupancy_at_all()
    {
        var store = new FakeOrderingStore { Closed = true };

        var slots = await Sut(store).GetSlotsAsync("tienda");

        Assert.Empty(slots.Slots);
        // Sin franjas candidatas no hay rango que leer, así que tampoco hay consulta.
        Assert.Equal(0, store.BatchReads);
    }

    // ------------------------------------------------------------------ ocupación

    [Fact]
    public async Task Occupancy_is_applied_to_the_slot_it_belongs_to()
    {
        var store = new FakeOrderingStore { Active = { [PrimeraDelLunes] = 2 } };

        var slots = await Sut(store).GetSlotsAsync("tienda", Lunes);

        // Capacidad 3: la franja con dos pedidos queda con uno.
        Assert.Equal(1, slots.Slots.Single(x => x.Start == PrimeraDelLunes).RemainingCapacity);
        // Y la de al lado sigue entera: la ocupación no se derrama.
        Assert.Equal(3, slots.Slots.Single(x => x.Start == PrimeraDelLunes.AddMinutes(15)).RemainingCapacity);
    }

    [Fact]
    public async Task A_slot_without_orders_reports_the_full_capacity()
    {
        var slots = await Sut(new FakeOrderingStore()).GetSlotsAsync("tienda", Lunes);

        // El diccionario no trae las franjas vacías, y ausente tiene que seguir significando cero.
        Assert.NotEmpty(slots.Slots);
        Assert.All(slots.Slots, slot => Assert.Equal(3, slot.RemainingCapacity));
    }

    [Fact]
    public async Task A_full_slot_disappears_from_the_availability()
    {
        var store = new FakeOrderingStore { Active = { [PrimeraDelLunes] = 3 } };

        var slots = await Sut(store).GetSlotsAsync("tienda", Lunes);

        Assert.DoesNotContain(slots.Slots, x => x.Start == PrimeraDelLunes);
        Assert.Contains(slots.Slots, x => x.Start == PrimeraDelLunes.AddMinutes(15));
    }

    [Fact]
    public async Task Occupancy_over_the_capacity_does_not_bring_the_slot_back()
    {
        var store = new FakeOrderingStore { Active = { [PrimeraDelLunes] = 9 } };

        var slots = await Sut(store).GetSlotsAsync("tienda", Lunes);

        Assert.DoesNotContain(slots.Slots, x => x.Start == PrimeraDelLunes);
    }

    [Fact]
    public async Task Every_day_of_the_range_keeps_its_own_occupancy()
    {
        var miercoles = PrimeraDelLunes.AddDays(2);
        var store = new FakeOrderingStore { Active = { [PrimeraDelLunes] = 3, [miercoles] = 1 } };

        var slots = await Sut(store).GetSlotsAsync("tienda");

        // El lunes a esa hora está lleno y desaparece; el miércoles a la misma hora sigue.
        Assert.DoesNotContain(slots.Slots, x => x.Start == PrimeraDelLunes);
        Assert.Equal(2, slots.Slots.Single(x => x.Start == miercoles).RemainingCapacity);
        // Y el martes, que no tiene nada, conserva la capacidad completa.
        Assert.Equal(3, slots.Slots.Single(x => x.Start == PrimeraDelLunes.AddDays(1)).RemainingCapacity);
    }

    [Fact]
    public async Task Occupancy_outside_the_requested_range_is_ignored()
    {
        // Un pedido de la semana siguiente no puede restar cupo a la jornada que se está mirando.
        var store = new FakeOrderingStore { Active = { [PrimeraDelLunes.AddDays(20)] = 3 } };

        var slots = await Sut(store).GetSlotsAsync("tienda", Lunes);

        Assert.All(slots.Slots, slot => Assert.Equal(3, slot.RemainingCapacity));
    }

    // ------------------------------------------------------------------ armado

    /// <summary>
    /// Sólo la tienda y el reloj: <c>GetSlotsAsync</c> no toca códigos, cifrado, consentimiento,
    /// almacenamiento ni avisos, y pasarlos como nulos deja que la prueba falle a gritos si algún
    /// día empieza a tocarlos.
    /// </summary>
    private static OrderingUseCases Sut(FakeOrderingStore store)
        => new(store, null!, null!, null!, null!, null!, new FrozenClock(Ahora));

    private sealed class FrozenClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>
    /// Tienda en memoria que cuenta cómo se le pregunta. Distingue las dos lecturas que el cambio
    /// separa: la agrupada, que arma la disponibilidad que se enseña, y la puntual, que autoriza
    /// bajo bloqueo. Sólo se implementa de verdad lo que esta ruta usa.
    /// </summary>
    private sealed class FakeOrderingStore : IOrderingStore
    {
        private static readonly Guid BusinessId = Guid.NewGuid();
        private readonly Business business = new(BusinessId, "tienda", "Tienda", Guid.NewGuid(), Guid.NewGuid(),
            "Tienda ficticia para medir disponibilidad.", "Centro", "3000000000");
        private readonly PickupOrderSettings settings = new(Guid.NewGuid(), BusinessId, true, null,
            minimumPreparationMinutes: 30, slotIntervalMinutes: 15, maximumActivePerSlot: 3,
            new TimeOnly(9, 0), new TimeOnly(18, 0));

        public Dictionary<DateTimeOffset, int> Active { get; } = [];
        /// <summary>Una semana sin horario publicado: no hay ninguna franja candidata.</summary>
        public bool Closed { get; init; }
        public int BatchReads { get; private set; }
        public int PointReads { get; private set; }
        public List<(DateTimeOffset From, DateTimeOffset To)> RangesRequested { get; } = [];

        public Task<(Business Business, PickupOrderSettings Settings)?> GetPublicContextAsync(string slug,
            CancellationToken ct) => Task.FromResult<(Business, PickupOrderSettings)?>((business, settings));

        public Task<IReadOnlyList<BusinessHour>> GetHoursAsync(Guid businessId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BusinessHour>>(Closed ? [] : Enum.GetValues<DayOfWeek>()
                .Select(day => new BusinessHour(Guid.NewGuid(), businessId, day,
                    new TimeOnly(9, 0), new TimeOnly(18, 0))).ToList());

        public Task<IReadOnlyDictionary<DateTimeOffset, int>> GetActiveSlotCountsAsync(Guid businessId,
            DateTimeOffset rangeStart, DateTimeOffset rangeEnd, CancellationToken ct)
        {
            BatchReads++;
            RangesRequested.Add((rangeStart, rangeEnd));
            // Se respeta el rango pedido, igual que hace el WHERE de la consulta agrupada.
            return Task.FromResult<IReadOnlyDictionary<DateTimeOffset, int>>(Active
                .Where(x => x.Key >= rangeStart && x.Key <= rangeEnd)
                .ToDictionary(x => x.Key, x => x.Value));
        }

        public Task<int> CountActiveInSlotAsync(Guid businessId, DateTimeOffset start, CancellationToken ct)
        {
            PointReads++;
            return Task.FromResult(Active.GetValueOrDefault(start));
        }

        // El resto del contrato no participa en esta ruta.
        public Task<IApplicationTransaction> BeginTransactionAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<PickupOrderSettings?> GetSettingsAsync(Guid businessId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PickupOrderSettings?> LockSettingsAsync(Guid businessId, CancellationToken ct) => throw new NotSupportedException();
        public Task LockSlotAsync(Guid businessId, DateTimeOffset start, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductCategory>> GetCategoriesAsync(Guid businessId, bool activeOnly, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<Product>> GetProductsAsync(Guid businessId, bool activeOnly, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, CatalogPhoto>> GetProductPhotosAsync(Guid businessId, CancellationToken ct) => throw new NotSupportedException();
        public Task<ProductCategory?> GetCategoryAsync(Guid businessId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Product?> GetProductAsync(Guid businessId, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<Business?> GetBusinessAsync(Guid businessId, CancellationToken ct) => throw new NotSupportedException();
        public Task<PickupOrder?> FindByCodeAsync(string hash, CancellationToken ct) => throw new NotSupportedException();
        public Task<PickupOrder?> GetOrderAsync(Guid businessId, Guid orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<PickupOrder>> ListOrdersAsync(Guid businessId, string? status, DateOnly? date, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CanManageOrdersAsync(Guid userId, Guid businessId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CanManageConfigurationAsync(Guid userId, Guid businessId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsModuleEnabledAsync(Guid businessId, BusinessModuleKind module, CancellationToken ct) => throw new NotSupportedException();
        public void AddCategory(ProductCategory category) => throw new NotSupportedException();
        public void AddProduct(Product product) => throw new NotSupportedException();
        public void AddSettings(PickupOrderSettings settings) => throw new NotSupportedException();
        public void AddOrder(PickupOrder order) => throw new NotSupportedException();
        public void AddConsent(ConsentReceipt consent) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct) => throw new NotSupportedException();
    }
}
