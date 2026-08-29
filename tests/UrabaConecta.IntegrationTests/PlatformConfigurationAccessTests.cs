using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Regresión J-ADM-03: la administración asistida opera Servicios y Personal con su rol global,
/// sin suplantar al propietario ni fabricar una membresía en cada negocio.
/// </summary>
public sealed class PlatformConfigurationAccessTests(PostgresWebFactory factory)
    : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Bella = DevelopmentSeeder.BellaBusinessId;

    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });

    [Fact]
    public async Task Platform_admin_manages_services_and_staff_without_a_business_membership()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        await AssertAdminHasNoMembership();

        var createdServiceResponse = await admin.PostAsJsonAsync($"/api/v1/businesses/{Bella}/services",
            new CreateServiceRequest
            {
                Name = $"Asistido {Guid.NewGuid():N}"[..24], DurationMinutes = 60,
                ReferencePrice = 50000
            }, Json);
        Assert.Equal(HttpStatusCode.Created, createdServiceResponse.StatusCode);
        var service = (await createdServiceResponse.Content.ReadFromJsonAsync<ServiceDto>(Json))!;

        var updatedServiceResponse = await admin.PutAsJsonAsync(
            $"/api/v1/businesses/{Bella}/services/{service.Id}",
            new UpdateServiceRequest
            {
                Name = $"{service.Name} editado", DurationMinutes = 90, ReferencePrice = 55000,
                IsActive = true, Version = service.Version
            }, Json);
        Assert.Equal(HttpStatusCode.OK, updatedServiceResponse.StatusCode);

        var createdStaffResponse = await admin.PostAsJsonAsync($"/api/v1/businesses/{Bella}/staff",
            new SaveStaffMemberRequest
            {
                DisplayName = $"Laura {Guid.NewGuid():N}"[..24], IsActive = true,
                ParticipatesInAvailability = true, ServiceIds = [service.Id]
            }, Json);
        Assert.Equal(HttpStatusCode.Created, createdStaffResponse.StatusCode);
        var staff = (await createdStaffResponse.Content.ReadFromJsonAsync<StaffMemberDto>(Json))!;

        var updatedStaffResponse = await admin.PutAsJsonAsync($"/api/v1/businesses/{Bella}/staff/{staff.Id}",
            new SaveStaffMemberRequest
            {
                DisplayName = staff.DisplayName, IsActive = true, ParticipatesInAvailability = true,
                ServiceIds = [service.Id], Version = staff.Version
            }, Json);
        Assert.Equal(HttpStatusCode.OK, updatedStaffResponse.StatusCode);

        Assert.Contains((await admin.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{Bella}/services", Json))!, x => x.Id == service.Id);
        Assert.Contains((await admin.GetFromJsonAsync<List<StaffMemberDto>>(
            $"/api/v1/businesses/{Bella}/staff", Json))!, x => x.Id == staff.Id && x.ServiceIds.Contains(service.Id));
        await AssertAdminHasNoMembership();
    }

    [Fact]
    public async Task Owners_keep_their_scope_and_unauthorized_users_remain_blocked()
    {
        using var owner = Client();
        using var otherOwner = Client();
        using var unprivilegedWorker = Client();
        using var anonymous = Client();
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        await PlatformAdministrationApiTests.Login(otherOwner, DevelopmentSeeder.OtherOwnerEmail);
        await PlatformAdministrationApiTests.Login(unprivilegedWorker, DevelopmentSeeder.BellaWorkerEmail);

        var create = await owner.PostAsJsonAsync($"/api/v1/businesses/{Bella}/services",
            new CreateServiceRequest
            {
                Name = $"Propietaria {Guid.NewGuid():N}"[..24], DurationMinutes = 30,
                ReferencePrice = 20000
            }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var service = (await create.Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{Bella}/services/{service.Id}",
            new UpdateServiceRequest
            {
                Name = $"{service.Name} editado", DurationMinutes = 45, ReferencePrice = 25000,
                IsActive = true, Version = service.Version
            }, Json)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.PutAsJsonAsync(
            $"/api/v1/businesses/{Bella}/services/{service.Id}",
            new UpdateServiceRequest
            {
                Name = "Intento ajeno", DurationMinutes = 30, ReferencePrice = 1,
                IsActive = true, Version = service.Version
            }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await otherOwner.GetAsync(
            $"/api/v1/businesses/{Bella}/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await unprivilegedWorker.GetAsync(
            $"/api/v1/businesses/{Bella}/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(
            $"/api/v1/businesses/{Bella}/services")).StatusCode);
    }

    [Fact]
    public async Task Platform_business_detail_exposes_services_and_staff_links_to_the_admin()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);

        var html = await admin.GetStringAsync($"/admin/negocios/{Bella}");

        Assert.Contains($"/panel/{Bella}/configuracion/servicios", html);
        Assert.Contains($"/panel/{Bella}/configuracion/personal", html);
    }

    private async Task AssertAdminHasNoMembership()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.Users.Where(x => x.Email == DevelopmentSeeder.PlatformAdminEmail)
            .Select(x => x.Id).SingleAsync();
        Assert.False(await db.BusinessMemberships.AnyAsync(x => x.BusinessId == Bella && x.UserId == adminId));
    }
}
