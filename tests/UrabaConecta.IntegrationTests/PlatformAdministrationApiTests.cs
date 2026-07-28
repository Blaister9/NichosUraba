using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class PlatformAdministrationApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    [Fact]
    public async Task Global_administration_requires_platform_role()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/v1/admin/businesses")).StatusCode);
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync("/api/v1/admin/businesses")).StatusCode);
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync("/api/v1/admin/businesses")).StatusCode);
    }

    [Fact]
    public async Task Draft_creation_assigns_existing_owner_without_assigning_admin_membership()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json);
        var request = NewRequest(catalog!, $"draft-{Guid.NewGuid():N}", saveAsDraft: true);
        var response = await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!;
        Assert.Equal("Draft", created.Business.Status);
        Assert.Contains("Appointments", created.Business.Modules);
        Assert.Null(created.TemporaryPassword);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminId = await db.Users.Where(x => x.Email == DevelopmentSeeder.PlatformAdminEmail).Select(x => x.Id).SingleAsync();
        Assert.False(await db.BusinessMemberships.AnyAsync(x =>
            x.BusinessId == created.Business.Id && x.UserId == adminId));
        var updatedResponse = await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Business.Id}",
            new UpdatePlatformBusinessRequest
            {
                Name = "Nombre editado", Slug = created.Business.Slug,
                MunicipalityId = catalog!.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                Description = "Información actualizada", Version = created.Business.Version
            }, Json);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        var updated = (await updatedResponse.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Equal("Nombre editado", updated.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(
            $"/api/v1/admin/businesses/{created.Business.Id}",
            new UpdatePlatformBusinessRequest
            {
                Name = "Escritura vieja", Slug = created.Business.Slug,
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                Description = "No aplica", Version = created.Business.Version
            }, Json)).StatusCode);
    }

    [Fact]
    public async Task Pilot_password_is_random_one_time_and_forces_change()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json);
        var request = NewRequest(catalog!, $"pilot-{Guid.NewGuid():N}", saveAsDraft: true);
        request.ExistingOwnerEmail = null;
        request.PilotDisplayName = "Propietaria piloto";
        request.PilotEmail = $"pilot-{Guid.NewGuid():N}@example.test";
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!;
        Assert.NotNull(created.TemporaryPassword);
        Assert.NotEqual(DevelopmentSeeder.DemoPassword, created.TemporaryPassword);
        var fetched = await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{created.Business.Id}", Json);
        Assert.Equal(request.PilotEmail, fetched!.OwnerEmail);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Users.Where(x => x.Email == request.PilotEmail)
            .Select(x => x.MustChangePassword).SingleAsync());
    }

    [Fact]
    public async Task Activation_suspension_module_preservation_and_stale_version_are_enforced()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json);
        var slug = $"ready-{Guid.NewGuid():N}";
        var request = NewRequest(catalog!, slug, saveAsDraft: true);
        request.InitialServiceName = "Consulta";
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        // Desde V5 la identidad visual y el contacto forman parte del checklist obligatorio.
        Assert.False(created.IsReady);
        Assert.Contains(created.Readiness, x => x.Key == "logo" && !x.IsComplete);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/businesses/{created.Id}/activate",
            new PlatformBusinessStateRequest { Version = created.Version }, Json)).StatusCode);

        var ready = await CompleteChecklistAsync(admin, created, catalog!);
        Assert.True(ready.IsReady);
        Assert.Equal(100, ready.CompletionPercentage);
        var activatedResponse = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/activate",
            new PlatformBusinessStateRequest { Version = ready.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, activatedResponse.StatusCode);
        var activated = (await activatedResponse.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Contains((await admin.GetFromJsonAsync<List<BusinessCardDto>>("/api/v1/public/businesses", Json))!,
            x => x.Slug == slug);
        var suspendedResponse = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/suspend",
            new PlatformBusinessStateRequest { Version = activated.Version, Reason = "Pausa de prueba" }, Json);
        Assert.Equal(HttpStatusCode.OK, suspendedResponse.StatusCode);
        var suspended = (await suspendedResponse.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<BusinessCardDto>>("/api/v1/public/businesses", Json))!,
            x => x.Slug == slug);
        var reactivatedResponse = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/reactivate",
            new PlatformBusinessStateRequest { Version = suspended.Version }, Json);
        var reactivated = (await reactivatedResponse.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Equal("Active", reactivated.Status);
        var modulesResponse = await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/modules",
            new UpdatePlatformModulesRequest { Appointments = true, PickupOrders = true, Version = reactivated.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, modulesResponse.StatusCode);
        var changed = (await modulesResponse.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Equal("PendingConfiguration", changed.Status);
        Assert.Contains("Appointments", changed.Modules);
        Assert.Contains("PickupOrders", changed.Modules);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(
            $"/api/v1/admin/businesses/{created.Id}/modules",
            new UpdatePlatformModulesRequest { Appointments = true, Version = reactivated.Version }, Json)).StatusCode);
    }

    [Fact]
    public async Task Safe_delete_removes_only_operation_free_draft()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json);
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses",
            NewRequest(catalog!, $"delete-{Guid.NewGuid():N}", saveAsDraft: true), Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        var deleted = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/delete",
            new PlatformBusinessStateRequest { Version = created.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/admin/businesses/{created.Id}")).StatusCode);
    }

    /// <summary>PNG válido de 1x1 con firma real, para probar la carga de imágenes.</summary>
    internal static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>
    /// Completa los requisitos que V5 añadió al checklist: descripción breve, contacto,
    /// ubicación, logo y portada. Devuelve el negocio ya listo para enviar a revisión.
    /// </summary>
    internal static async Task<PlatformBusinessDto> CompleteChecklistAsync(HttpClient admin,
        PlatformBusinessDto business, PlatformBusinessListDto catalog)
    {
        var saved = (await (await admin.PutAsJsonAsync($"/api/v1/admin/businesses/{business.Id}/profile",
            new SaveBusinessProfileRequest
            {
                Name = business.Name, Slug = business.Slug,
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                ShortDescription = "Negocio ficticio para pruebas automatizadas.",
                Description = "Descripción completa del negocio ficticio.",
                Address = "Calle 1 # 1-1", PublicPhone = "3000000000",
                Version = business.Version
            }, Json)).Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        foreach (var kind in new[] { "Logo", "Cover" })
            Assert.Equal(HttpStatusCode.Created,
                (await UploadImageAsync(admin, saved.Id, kind, "foto.png", "image/png", TinyPng())).StatusCode);
        return (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{saved.Id}", Json))!;
    }

    internal static Task<HttpResponseMessage> UploadImageAsync(HttpClient client, Guid businessId, string kind,
        string fileName, string contentType, byte[] content, string? altText = null)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        if (altText is not null) form.Add(new StringContent(altText), "altText");
        return client.PostAsync($"/api/v1/admin/businesses/{businessId}/images", form);
    }

    private static CreatePlatformBusinessRequest NewRequest(PlatformBusinessListDto catalog, string slug, bool saveAsDraft)
        => new()
        {
            Name = $"Negocio {slug[^6..]}", Slug = slug,
            MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
            Description = "Piloto de prueba", Appointments = true,
            ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = saveAsDraft
        };

    private static async Task Login(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = DevelopmentSeeder.DemoPassword,
            ["Input.RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
