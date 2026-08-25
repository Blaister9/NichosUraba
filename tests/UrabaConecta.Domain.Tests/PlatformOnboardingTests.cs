using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class PlatformOnboardingTests
{
    private static Business Draft() => Business.CreateDraft(Guid.NewGuid(), "piloto-prueba", "Piloto",
        Guid.NewGuid(), Guid.NewGuid(), "Resumen breve del piloto", "Descripción", "Apartadó", "3000000000",
        null, null, DateTimeOffset.Parse("2026-07-26T12:00:00Z"));

    [Fact]
    public void Draft_is_private_and_slug_is_normalized()
    {
        var business = Business.CreateDraft(Guid.NewGuid(), " Café del Río ", "Café",
            Guid.NewGuid(), Guid.NewGuid(), "Café de origen en el parque", "", null, null, null, null,
            DateTimeOffset.UtcNow);
        Assert.Equal("cafe-del-rio", business.Slug);
        Assert.Equal(BusinessStatus.Draft, business.Status);
        Assert.False(business.IsPublished);
    }

    [Fact]
    public void Activation_requires_readiness_and_publishes_atomically()
    {
        var business = Draft();
        Assert.Equal("BUSINESS_NOT_READY", Assert.Throws<DomainException>(() =>
            business.Activate(false, DateTimeOffset.UtcNow, 0)).Code);
        business.Activate(true, DateTimeOffset.UtcNow, 0);
        Assert.Equal(BusinessStatus.Active, business.Status);
        Assert.True(business.IsPublished);
        Assert.Equal(1, business.Version);
    }

    [Fact]
    public void Suspension_requires_reason_and_removes_publication()
    {
        var business = Draft();
        business.Activate(true, DateTimeOffset.UtcNow, 0);
        Assert.Equal("SUSPENSION_REASON_REQUIRED", Assert.Throws<DomainException>(() =>
            business.Suspend("", DateTimeOffset.UtcNow, 1)).Code);
        business.Suspend("Pausa solicitada", DateTimeOffset.UtcNow, 1);
        Assert.Equal(BusinessStatus.Suspended, business.Status);
        Assert.False(business.IsPublished);
    }

    [Fact]
    public void Configuration_change_unpublishes_active_business_and_detects_stale_version()
    {
        var business = Draft();
        business.Activate(true, DateTimeOffset.UtcNow, 0);
        business.ConfigurationChanged(DateTimeOffset.UtcNow, 1);
        Assert.Equal(BusinessStatus.PendingConfiguration, business.Status);
        Assert.False(business.IsPublished);
        Assert.Equal("CONCURRENCY_CONFLICT", Assert.Throws<DomainException>(() =>
            business.ConfigurationChanged(DateTimeOffset.UtcNow, 1)).Code);
    }

    [Theory]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    public void Readiness_has_concrete_requirements_per_enabled_module(bool appointments, bool queues, bool orders,
        bool hours, bool queueDefinition, bool pickupSettings)
    {
        var modules = new List<BusinessModuleKind>();
        if (appointments) modules.Add(BusinessModuleKind.Appointments);
        if (queues) modules.Add(BusinessModuleKind.VirtualQueues);
        if (orders) modules.Add(BusinessModuleKind.PickupOrders);
        var readiness = BusinessReadinessCalculator.Calculate(true, true, true, true, modules, hours, false,
            queueDefinition, pickupSettings, false, false);
        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Requirements, x => x.IsApplicable && !x.IsComplete);
    }

    [Fact]
    public void Disabled_module_preserves_identity_and_uses_optimistic_concurrency()
    {
        var module = new BusinessModule(Guid.NewGuid(), BusinessModuleKind.VirtualQueues, true, DateTimeOffset.UtcNow);
        module.SetEnabled(false, DateTimeOffset.UtcNow, 0);
        Assert.False(module.IsEnabled);
        Assert.Equal("CONCURRENCY_CONFLICT", Assert.Throws<DomainException>(() =>
            module.SetEnabled(true, DateTimeOffset.UtcNow, 0)).Code);
    }

    // --------------------------------------------- descripción breve desde el alta (V6.4.1)

    [Fact]
    public void Draft_stores_the_short_description_given_at_creation()
    {
        var business = Business.CreateDraft(Guid.NewGuid(), "arepas-del-puerto", "Arepas del Puerto",
            Guid.NewGuid(), Guid.NewGuid(), "  Arepas de maíz pilado frente al muelle.  ", "Descripción larga",
            null, null, null, null, DateTimeOffset.UtcNow);
        // Con trim: lo que la socia escribió es lo que queda guardado, sin espacios de sobra.
        Assert.Equal("Arepas de maíz pilado frente al muelle.", business.ShortDescription);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Draft_requires_a_short_description(string shortDescription)
        => Assert.Equal("INVALID_SHORT_DESCRIPTION", Assert.Throws<DomainException>(() =>
            Business.CreateDraft(Guid.NewGuid(), "sin-breve", "Sin breve", Guid.NewGuid(), Guid.NewGuid(),
                shortDescription, "Descripción larga", null, null, null, null, DateTimeOffset.UtcNow)).Code);

    [Fact]
    public void Draft_short_description_is_bounded_to_the_same_limit_as_the_profile()
    {
        // 160 es el límite del perfil; el alta no puede admitir uno distinto o la ficha rechazaría
        // después lo que el alta aceptó.
        Assert.Equal("INVALID_SHORT_DESCRIPTION", Assert.Throws<DomainException>(() =>
            Business.CreateDraft(Guid.NewGuid(), "muy-larga", "Muy larga", Guid.NewGuid(), Guid.NewGuid(),
                new string('a', 161), "Descripción larga", null, null, null, null, DateTimeOffset.UtcNow)).Code);
        var limite = Business.CreateDraft(Guid.NewGuid(), "en-el-limite", "En el límite", Guid.NewGuid(),
            Guid.NewGuid(), new string('a', 160), "Descripción larga", null, null, null, null,
            DateTimeOffset.UtcNow);
        Assert.Equal(160, limite.ShortDescription.Length);
    }

    [Theory]
    [InlineData(false, true, true, "Falta el nombre del negocio.")]
    [InlineData(true, false, true, "Falta la descripción breve.")]
    [InlineData(true, true, false, "Falta la descripción completa.")]
    public void Readiness_names_exactly_which_piece_of_the_basic_information_is_missing(
        bool hasName, bool hasShortDescription, bool hasDescription, string expected)
    {
        var readiness = BusinessReadinessCalculator.Calculate(hasName, hasShortDescription, hasDescription,
            true, [BusinessModuleKind.VirtualQueues], false, false, true, false, false, false);
        Assert.Contains(expected, readiness.MissingLabels);
        // Y sólo ése: el mensaje agrupado obligaba a la socia a adivinar cuál de los tres era.
        Assert.Single(readiness.MissingLabels);
        Assert.DoesNotContain(readiness.MissingLabels,
            x => x.Contains(" o la descripción", StringComparison.Ordinal));
    }

    // --------------------------------------------- el horario también sostiene los pedidos

    /// <summary>Checklist de un negocio al que sólo le falta decidir horario y módulos.</summary>
    private static BusinessReadiness Checklist(BusinessModuleKind operacion, bool hasHours)
        => BusinessReadinessCalculator.Calculate(true, true, true, true, [operacion], hasHours,
            hasService: true, hasQueueDefinition: true, hasPickupSettings: true,
            hasProductCategory: true, hasProduct: true);

    [Fact]
    public void A_business_that_only_takes_orders_is_still_asked_for_its_opening_hours()
    {
        // Las franjas para recoger salen de cruzar el horario del negocio con la ventana de
        // pedidos. Mientras el horario fue asunto exclusivo de las citas, un negocio de sólo
        // pedidos llegaba al 100 % del checklist y se publicaba sin una sola hora en la que
        // pedirle nada: el cliente abría la pantalla de pedido y no había ninguna franja.
        var sinHorario = Checklist(BusinessModuleKind.PickupOrders, hasHours: false);
        Assert.Contains(sinHorario.Requirements, x => x.Key == "hours" && x.IsApplicable && !x.IsComplete);
        Assert.Contains("Configure el horario de atención.", sinHorario.MissingLabels);
        Assert.False(sinHorario.IsReady);
    }

    [Fact]
    public void With_its_hours_registered_the_order_only_business_completes_the_checklist()
    {
        var conHorario = Checklist(BusinessModuleKind.PickupOrders, hasHours: true);
        Assert.Contains(conHorario.Requirements, x => x.Key == "hours" && x.IsApplicable && x.IsComplete);
        Assert.True(conHorario.IsReady);
        Assert.Equal(100, conHorario.CompletionPercentage);
    }

    [Fact]
    public void A_queue_only_business_is_not_asked_for_opening_hours()
    {
        // La fila se atiende por orden de llegada y no calcula franjas de nada, así que exigirle
        // horario sería inventarle un requisito que no sostiene ninguna operación suya.
        var readiness = Checklist(BusinessModuleKind.VirtualQueues, hasHours: false);
        Assert.Contains(readiness.Requirements, x => x.Key == "hours" && !x.IsApplicable);
        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void Appointments_keep_demanding_the_hours_they_always_demanded()
    {
        var readiness = Checklist(BusinessModuleKind.Appointments, hasHours: false);
        Assert.Contains(readiness.Requirements, x => x.Key == "hours" && x.IsApplicable && !x.IsComplete);
        Assert.False(readiness.IsReady);
    }

    [Theory]
    [InlineData(false, false, true, true)]
    [InlineData(true, false, false, true)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void Appointments_require_eligible_linked_staff_and_duration_compatible_hours(
        bool hasStaff, bool hasLink, bool compatibleHours, bool expectedMissing)
    {
        var readiness = BusinessOperationalReadiness.Evaluate(Facts(
            BusinessCapabilities.Resolve(true, false, false)) with
        {
            HasEligibleStaff = hasStaff && hasLink,
            HasBookableAppointmentConfiguration = hasStaff && hasLink && compatibleHours
        });
        Assert.Equal(expectedMissing, readiness.Requirements.Single(x =>
            x.Key == "appointment-availability").IsComplete == false);
        Assert.Equal(!expectedMissing, readiness.IsReady);
    }

    [Fact]
    public void Virtual_business_does_not_publish_or_require_a_fake_address()
    {
        var readiness = BusinessOperationalReadiness.Evaluate(Facts(
            BusinessCapabilities.Resolve(false, true, false)) with
        {
            LocationMode = BusinessLocationMode.Virtual,
            OrderFulfillmentMode = OrderFulfillmentMode.Coordinated,
            HasLocation = false,
            HasHours = false
        });
        Assert.True(readiness.IsReady);
        Assert.False(readiness.Requirements.Single(x => x.Key == "location").IsApplicable);
        Assert.False(readiness.Requirements.Single(x => x.Key == "hours").IsApplicable);
    }

    [Fact]
    public void Pickup_at_business_requires_a_public_physical_location()
    {
        var readiness = BusinessOperationalReadiness.Evaluate(Facts(
            BusinessCapabilities.Resolve(false, false, true)) with
        {
            LocationMode = BusinessLocationMode.PrivatePhysical,
            OrderFulfillmentMode = OrderFulfillmentMode.PickupAtPublicLocation,
            HasLocation = false
        });
        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Requirements, x => x.Key == "fulfillment" && !x.IsComplete);
    }

    [Fact]
    public void Capability_graph_rejects_independent_material_switches()
    {
        var orders = new[] { BusinessModuleKind.PickupOrders };
        Assert.Equal("CAPABILITY_DEPENDENCY", Assert.Throws<DomainException>(() =>
            BusinessCapabilities.EnsureConsistent(orders, null, false, null)).Code);
        Assert.Equal("CAPABILITY_DEPENDENCY", Assert.Throws<DomainException>(() =>
            BusinessCapabilities.EnsureConsistent(orders, true, null, null)).Code);
        BusinessCapabilities.EnsureConsistent(orders, false, true, false);
    }

    private static BusinessOperationalFacts Facts(IReadOnlySet<BusinessModuleKind> capabilities)
        => new(true, true, true, true, BusinessLocationMode.PublicPhysical,
            OrderFulfillmentMode.PickupAtPublicLocation, true, true, true, true, capabilities,
            true, true, true, true, true, true, true, true, true);
}
