using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class PlatformAdministrationApiTests
{
    [Fact]
    public async Task Reconciliation_is_dry_run_versioned_atomic_and_idempotent()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>(
            "/api/v1/admin/businesses", Json))!;
        var ready = await CreatePublishedQueueBusiness(admin, catalog,
            $"reconcile-ready-{Guid.NewGuid():N}");
        var inherited = await CreatePublishedQueueBusiness(admin, catalog,
            $"reconcile-invalid-{Guid.NewGuid():N}");
        var draftRequest = NewRequest(catalog, $"reconcile-draft-{Guid.NewGuid():N}", true);
        draftRequest.Appointments = false; draftRequest.VirtualQueues = true;
        var draft = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", draftRequest, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

        // Simula una fila heredada anterior al guard: queda publicada sin portada sin ejecutar
        // ninguna mutación HTTP moderna que la reconciliaría automáticamente.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var suppression = db.SuppressOperationalReadinessGuardForSeeding();
            var cover = await db.BusinessImages.SingleAsync(x => x.BusinessId == inherited.Id &&
                x.Kind == BusinessImageKind.Cover && !x.IsDeleted);
            cover.SoftDelete(DateTimeOffset.UtcNow, cover.Version);
            await db.SaveChangesAsync();
        }

        var dryResponse = await admin.PostAsync("/api/v1/admin/readiness-reconciliation/dry-run", null);
        Assert.Equal(HttpStatusCode.OK, dryResponse.StatusCode);
        var dry = (await dryResponse.Content.ReadFromJsonAsync<ReadinessReconciliationPlanDto>(Json))!;
        var readyPlan = Assert.Single(dry.Businesses, x => x.BusinessId == ready.Id);
        Assert.Equal("KeepPublished", readyPlan.ProposedAction);
        Assert.Equal(100, readyPlan.ReadinessPercent);
        var invalidPlan = Assert.Single(dry.Businesses, x => x.BusinessId == inherited.Id);
        Assert.Equal("Active", invalidPlan.CurrentStatus);
        Assert.True(invalidPlan.IsPublished);
        Assert.Equal("MoveToPendingConfiguration", invalidPlan.ProposedAction);
        Assert.Contains(invalidPlan.MissingRequirements, x => x.Contains("portada", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(dry.Businesses, x => x.BusinessId == draft.Id);

        // DRY-RUN no escribió nada.
        var stillPublished = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{inherited.Id}", Json))!;
        Assert.Equal("Active", stillPublished.Status);
        Assert.True(stillPublished.IsPublished);

        var staleResponse = await admin.PostAsJsonAsync("/api/v1/admin/readiness-reconciliation/apply",
            new ApplyReadinessReconciliationRequest { PlanFingerprint = new string('0', 64) }, Json);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
        var afterStale = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{inherited.Id}", Json))!;
        Assert.Equal("Active", afterStale.Status);
        Assert.True(afterStale.IsPublished);

        var appliedResponse = await admin.PostAsJsonAsync("/api/v1/admin/readiness-reconciliation/apply",
            new ApplyReadinessReconciliationRequest { PlanFingerprint = dry.PlanFingerprint }, Json);
        Assert.Equal(HttpStatusCode.OK, appliedResponse.StatusCode);
        var applied = (await appliedResponse.Content
            .ReadFromJsonAsync<ReadinessReconciliationApplyResultDto>(Json))!;
        Assert.Contains(inherited.Id, applied.AffectedBusinessIds);

        var reconciled = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{inherited.Id}", Json))!;
        Assert.Equal("PendingConfiguration", reconciled.Status);
        Assert.False(reconciled.IsPublished);
        Assert.Contains(reconciled.MissingLabels ?? [], x => x.Contains("portada", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.GetAsync($"/api/v1/public/businesses/{inherited.Slug}")).StatusCode);

        var unchanged = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{ready.Id}", Json))!;
        Assert.Equal("Active", unchanged.Status);
        Assert.True(unchanged.IsPublished);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.GetAsync($"/api/v1/public/businesses/{ready.Slug}")).StatusCode);
        var unchangedDraft = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{draft.Id}", Json))!;
        Assert.Equal("Draft", unchangedDraft.Status);

        var second = (await (await admin.PostAsJsonAsync("/api/v1/admin/readiness-reconciliation/apply",
            new ApplyReadinessReconciliationRequest { PlanFingerprint = dry.PlanFingerprint }, Json))
            .Content.ReadFromJsonAsync<ReadinessReconciliationApplyResultDto>(Json))!;
        Assert.Equal(0, second.AppliedCount);

        var audit = (await admin.GetFromJsonAsync<List<PlatformAuditEntryDto>>(
            $"/api/v1/admin/businesses/{inherited.Id}/audit", Json))!;
        Assert.Contains(audit, x => x.Action == "BusinessReadinessReconciled");
    }

    [Fact]
    public async Task Reconciliation_requires_platform_admin()
    {
        using var partner = Client();
        await Login(partner, DevelopmentSeeder.PartnerOperatorEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await partner.PostAsync("/api/v1/admin/readiness-reconciliation/dry-run", null)).StatusCode);
    }

    private static async Task<PlatformBusinessDto> CreatePublishedQueueBusiness(HttpClient admin,
        PlatformBusinessListDto catalog, string slug)
    {
        var request = NewRequest(catalog, slug, true);
        request.Appointments = false; request.VirtualQueues = true; request.PickupOrders = false;
        request.Address = "Calle pública 1 # 1-1"; request.PublicPhone = "3000000000";
        var created = (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", request, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
        foreach (var kind in new[] { "Logo", "Cover" })
            Assert.Equal(HttpStatusCode.Created, (await UploadImageAsync(admin, created.Id, kind,
                "foto.png", "image/png", TinyPng())).StatusCode);
        var ready = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{created.Id}", Json))!;
        Assert.True(ready.IsReady, string.Join(" ", ready.MissingLabels ?? []));
        return (await (await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{created.Id}/activate",
            new PlatformBusinessStateRequest { Version = ready.Version }, Json))
            .Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
    }
}
