using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class PlatformOnboardingTests
{
    private static Business Draft() => Business.CreateDraft(Guid.NewGuid(), "piloto-prueba", "Piloto",
        Guid.NewGuid(), Guid.NewGuid(), "Descripción", "Apartadó", "3000000000", null, null,
        DateTimeOffset.Parse("2026-07-26T12:00:00Z"));

    [Fact]
    public void Draft_is_private_and_slug_is_normalized()
    {
        var business = Business.CreateDraft(Guid.NewGuid(), " Café del Río ", "Café",
            Guid.NewGuid(), Guid.NewGuid(), "", null, null, null, null, DateTimeOffset.UtcNow);
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
        var readiness = BusinessReadinessCalculator.Calculate(true, true, modules, hours, false,
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
}
