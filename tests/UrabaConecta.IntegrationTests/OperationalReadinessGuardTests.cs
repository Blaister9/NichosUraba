using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed class OperationalReadinessGuardTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    [Theory]
    [InlineData("cover")]
    [InlineData("hours")]
    [InlineData("product")]
    [InlineData("service")]
    [InlineData("staff")]
    [InlineData("settings")]
    public async Task Published_business_is_atomically_unpublished_when_a_prerequisite_is_lost(string mutation)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fixture = await CreatePublishedAsync(db);

        switch (mutation)
        {
            case "cover": fixture.Cover.SoftDelete(DateTimeOffset.UtcNow, fixture.Cover.Version); break;
            case "hours": db.BusinessHours.RemoveRange(fixture.Hours); break;
            case "product": fixture.Product.Update(fixture.Category.Id, fixture.Product.Name, "", 1000, 0,
                active: false, available: false, fixture.Product.Version); break;
            case "service": fixture.Service.Update(fixture.Service.Name, 60, 0, active: false,
                expectedVersion: fixture.Service.Version); break;
            case "staff": fixture.Staff.Update(fixture.Staff.DisplayName, active: false,
                expectedVersion: fixture.Staff.Version); break;
            case "settings": fixture.Settings.Update(false, fixture.Settings.PublicMessage, 30, 15, 5,
                new TimeOnly(8, 0), new TimeOnly(18, 0), fixture.Settings.Version); break;
        }

        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var business = await db.Businesses.SingleAsync(x => x.Id == fixture.Business.Id);
        Assert.Equal(BusinessStatus.PendingConfiguration, business.Status);
        Assert.False(business.IsPublished);
    }

    [Fact]
    public async Task Appointment_readiness_detects_missing_link_inactive_staff_and_incompatible_duration()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var fixture = await CreatePublishedAsync(db);
        db.StaffServices.Remove(fixture.Link);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var business = await db.Businesses.SingleAsync(x => x.Id == fixture.Business.Id);
        Assert.Equal(BusinessStatus.PendingConfiguration, business.Status);

        var facts = await BusinessOperationalReadinessQuery.LoadAsync(db, [business.Id], default);
        var readiness = BusinessOperationalReadiness.Evaluate(facts[business.Id]);
        Assert.Contains(readiness.Requirements, x => x.Key == "eligible-staff" && !x.IsComplete);
        Assert.Contains(readiness.Requirements, x => x.Key == "appointment-availability" && !x.IsComplete);
    }

    private static async Task<Fixture> CreatePublishedAsync(AppDbContext db)
    {
        var municipalityId = await db.Municipalities.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var categoryId = await db.Categories.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var ownerId = await db.Users.Where(x => x.Email == DevelopmentSeeder.BellaOwnerEmail)
            .Select(x => x.Id).SingleAsync();
        var now = DateTimeOffset.UtcNow;
        var business = Business.CreateDraft(Guid.NewGuid(), $"readiness-{Guid.NewGuid():N}", "Readiness fixture",
            municipalityId, categoryId, "Fixture aislado para readiness", "Descripción completa",
            "Calle 1 # 1-1", "3000000000", null, null, now);
        var modules = new[]
        {
            BusinessModuleKind.Appointments, BusinessModuleKind.PickupOrders,
            BusinessModuleKind.Services, BusinessModuleKind.Products, BusinessModuleKind.Staff
        }.Select(x => new BusinessModule(business.Id, x, true, now)).ToList();
        var hours = new[] { new BusinessHour(Guid.NewGuid(), business.Id, DayOfWeek.Monday,
            new TimeOnly(8, 0), new TimeOnly(18, 0)) };
        var service = new Service(Guid.NewGuid(), business.Id, "Consulta", 60, 0);
        var staff = new StaffMember(Guid.NewGuid(), business.Id, "Profesional");
        var link = new StaffService(business.Id, staff.Id, service.Id);
        var settings = new PickupOrderSettings(Guid.NewGuid(), business.Id, true, "Coordina tu pedido.",
            30, 15, 5, new TimeOnly(8, 0), new TimeOnly(18, 0));
        var category = new ProductCategory(Guid.NewGuid(), business.Id, "Catálogo", 0);
        var product = new Product(Guid.NewGuid(), business.Id, category.Id, "Producto", "", 1000);
        var logo = Image(business.Id, BusinessImageKind.Logo, now);
        var cover = Image(business.Id, BusinessImageKind.Cover, now);
        var owner = new BusinessMembership(Guid.NewGuid(), business.Id, ownerId, MembershipRole.Owner,
            true, true, true, now, true, true);
        db.AddRange(business); db.AddRange(modules); db.AddRange(hours); db.AddRange(service, staff, link,
            settings, category, product, logo, cover, owner);
        await db.SaveChangesAsync();
        business.Activate(true, now.AddSeconds(1), business.Version);
        await db.SaveChangesAsync();
        Assert.True(business.IsPublished);
        return new(business, hours, service, staff, link, settings, category, product, cover);
    }

    private static BusinessImage Image(Guid businessId, BusinessImageKind kind, DateTimeOffset now)
        => new(Guid.NewGuid(), businessId, kind, $"fixtures/{businessId:N}/{kind}.png", "image/png",
            100, 100, 100, kind.ToString(), 0, now);

    private sealed record Fixture(Business Business, IReadOnlyList<BusinessHour> Hours, Service Service,
        StaffMember Staff, StaffService Link, PickupOrderSettings Settings, ProductCategory Category,
        Product Product, BusinessImage Cover);
}
