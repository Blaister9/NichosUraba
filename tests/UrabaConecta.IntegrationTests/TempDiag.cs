using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.IntegrationTests;

public sealed class TempDiag(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Diag()
    {
        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false });
        var login = typeof(PlatformAdministrationApiTests).GetMethod("Login",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        await (Task)login.Invoke(null, [admin, DevelopmentSeeder.PlatformAdminEmail])!;
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var slug = $"diag-{Guid.NewGuid():N}";
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", new CreatePlatformBusinessRequest
        {
            Name = "Diag", Slug = slug, MunicipalityId = catalog.Municipalities[0].Id,
            CategoryId = catalog.Categories[0].Id, Description = "d", Appointments = true,
            InitialServiceName = "Corte", ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = true
        }, Json)).Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        var ready = await PlatformAdministrationApiTests.CompleteChecklistAsync(admin, created, catalog);
        var reviewed = (await (await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/submit-review",
            new SubmitForReviewRequest { Version = ready.Version }, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/activate",
            new PlatformBusinessStateRequest { Version = reviewed.Version }, Json);

        var profile = (await admin.GetFromJsonAsync<BusinessProfileDto>($"/api/v1/public/businesses/{slug}", Json))!;
        var serviceId = profile.Services[0].Id;
        SlotListDto? slots = null; DateOnly date = default;
        for (var i = 1; i <= 8 && (slots is null || slots.Slots.Count == 0); i++)
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(i));
            slots = await admin.GetFromJsonAsync<SlotListDto>(
                $"/api/v1/public/businesses/{slug}/appointment-slots?serviceId={serviceId}&date={date:yyyy-MM-dd}", Json);
        }
        Assert.NotEmpty(slots!.Slots);

        using var scope = factory.Services.CreateScope();
        var useCases = scope.ServiceProvider.GetRequiredService<IUrabaUseCases>();
        try
        {
            await useCases.CreateAppointmentAsync(slug, new CreateAppointmentRequest
            {
                ServiceId = serviceId, Start = slots.Slots[0].Start, CustomerAlias = "Cliente",
                Phone = "3009998877", ConsentAccepted = true,
                ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion
            });
        }
        catch (Exception ex)
        {
            Assert.Fail($"{ex.GetType().FullName}: {ex.Message}\n{ex.InnerException?.Message}\n{ex.StackTrace}");
        }
    }
}
