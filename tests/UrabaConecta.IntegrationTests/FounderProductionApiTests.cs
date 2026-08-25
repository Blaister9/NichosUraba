using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Identity;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.IntegrationTests;

public sealed partial class FounderProductionApiTests(PostgresWebFactory factory)
    : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    // ------------------------------------------------------------------ roles

    [Fact]
    public async Task Partner_operator_reaches_the_console_but_a_business_owner_does_not()
    {
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/businesses")).StatusCode);

        using var partner = Client();
        await Login(partner, DevelopmentSeeder.PartnerOperatorEmail);
        Assert.Equal(HttpStatusCode.OK, (await partner.GetAsync("/api/v1/admin/businesses")).StatusCode);
    }

    [Fact]
    public async Task A_partner_operator_cannot_escalate_her_own_privileges()
    {
        using var partner = Client();
        await Login(partner, DevelopmentSeeder.PartnerOperatorEmail);

        // No puede crear otra socia (ni convertirse a sí misma en administradora).
        var escalation = await partner.PostAsJsonAsync("/api/v1/admin/invitations", new CreateInvitationRequest
        {
            Email = $"escalada-{Guid.NewGuid():N}@example.test", DisplayName = "Intento", Grant = "PartnerOperator"
        }, Json);
        Assert.Equal(HttpStatusCode.Forbidden, escalation.StatusCode);

        // No puede administrar socias, reiniciar accesos ni consultar la auditoría global.
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.GetAsync("/api/v1/admin/partner-operators")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.GetAsync("/api/v1/admin/access-audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.PostAsJsonAsync("/api/v1/admin/access-resets",
            new ResetAccessRequest { Email = DevelopmentSeeder.BellaOwnerEmail }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.GetAsync("/api/v1/admin/health")).StatusCode);
    }

    [Fact]
    public async Task A_partner_operator_only_sees_and_edits_the_businesses_she_created()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var foreign = await CreateAsync(admin, catalog, $"ajeno-{Guid.NewGuid():N}");

        using var partner = Client();
        await Login(partner, DevelopmentSeeder.PartnerOperatorEmail);
        var mine = await CreateAsync(partner, catalog, $"propio-{Guid.NewGuid():N}");

        var visible = (await partner.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        Assert.Contains(visible.Items, x => x.Id == mine.Id);
        Assert.DoesNotContain(visible.Items, x => x.Id == foreign.Id);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await partner.GetAsync($"/api/v1/admin/businesses/{foreign.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await partner.GetAsync($"/api/v1/admin/businesses/{mine.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_partner_operator_cannot_publish_suspend_or_archive()
    {
        using var partner = Client();
        await Login(partner, DevelopmentSeeder.PartnerOperatorEmail);
        var catalog = (await partner.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var business = await CreateAsync(partner, catalog, $"revision-{Guid.NewGuid():N}");
        var ready = await PlatformAdministrationApiTests.CompleteChecklistAsync(partner, business, catalog);

        // Puede enviar a revisión…
        var review = await partner.PostAsJsonAsync($"/api/v1/admin/businesses/{business.Id}/submit-review",
            new SubmitForReviewRequest { Version = ready.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        var reviewed = (await review.Content.ReadFromJsonAsync<PlatformBusinessDto>(Json))!;
        Assert.Equal("PendingReview", reviewed.Status);
        Assert.False(reviewed.IsPublished);

        // …pero no puede aprobarla ni rechazarla ella misma.
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.PostAsJsonAsync(
            $"/api/v1/admin/businesses/{business.Id}/activate",
            new PlatformBusinessStateRequest { Version = reviewed.Version }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await partner.PostAsJsonAsync(
            $"/api/v1/admin/businesses/{business.Id}/reject-review",
            new RejectReviewRequest { Version = reviewed.Version, Notes = "Me autorrechazo." }, Json)).StatusCode);
    }

    // ------------------------------------------------------------ invitaciones

    [Fact]
    public async Task An_invitation_link_is_single_use_and_never_reveals_a_password()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var email = $"invitada-{Guid.NewGuid():N}@example.test";
        var issued = await InviteAsync(admin, email, "Socia invitada");
        Assert.Contains("/Account/AcceptInvitation?token=", issued.AcceptPath);

        var token = TokenFrom(issued.AcceptPath);
        // El token en claro no se persiste: sólo su HMAC.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await db.AccessInvitations.SingleAsync(x => x.Id == issued.Id);
            Assert.NotEqual(token, stored.TokenHash);
            Assert.DoesNotContain(token, stored.TokenHash);
        }

        const string password = "ClaveDeLaSocia!2026";
        Assert.True(await AcceptAsync(token, password));
        // Un segundo intento con el mismo enlace ya no sirve.
        Assert.False(await AcceptAsync(token, "OtraClaveDistinta!2026"));

        // La cuenta quedó activa y puede iniciar sesión con la contraseña que ella eligió.
        using var invited = Client();
        await Login(invited, email, password);
        Assert.Equal(HttpStatusCode.OK, (await invited.GetAsync("/api/v1/admin/businesses")).StatusCode);
    }

    [Fact]
    public async Task A_revoked_invitation_cannot_be_accepted()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var issued = await InviteAsync(admin, $"revocada-{Guid.NewGuid():N}@example.test", "Revocada");
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.DeleteAsync($"/api/v1/admin/invitations/{issued.Id}")).StatusCode);
        Assert.False(await AcceptAsync(TokenFrom(issued.AcceptPath), "ClaveQueNoAplica!2026"));
    }

    [Fact]
    public async Task An_expired_invitation_cannot_be_accepted()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var issued = await InviteAsync(admin, $"vencida-{Guid.NewGuid():N}@example.test", "Vencida");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AccessInvitations.Where(x => x.Id == issued.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        Assert.False(await AcceptAsync(TokenFrom(issued.AcceptPath), "ClaveQueNoAplica!2026"));
    }

    [Fact]
    public async Task Resending_an_invitation_invalidates_the_previous_link()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var first = await InviteAsync(admin, $"reenvio-{Guid.NewGuid():N}@example.test", "Reenviada");
        var resent = (await (await admin.PostAsync($"/api/v1/admin/invitations/{first.Id}/resend", null))
            .Content.ReadFromJsonAsync<InvitationIssuedDto>(Json))!;
        Assert.NotEqual(first.Id, resent.Id);
        Assert.False(await AcceptAsync(TokenFrom(first.AcceptPath), "ClaveConEnlaceViejo!2026"));
        Assert.True(await AcceptAsync(TokenFrom(resent.AcceptPath), "ClaveConEnlaceNuevo!2026"));
    }

    [Fact]
    public async Task Owner_invitation_promotes_an_existing_worker_membership_and_synchronizes_identity()
    {
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        var email = $"invited-owner-{Guid.NewGuid():N}@demo.local";
        var memberResponse = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/create-development",
            new CreateDevelopmentMemberRequest { DisplayName = "Owner invitada", Email = email,
                CanManageAppointments = true, CanManageConfiguration = true }, Json);
        Assert.Equal(HttpStatusCode.Created, memberResponse.StatusCode);

        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var invitationResponse = await admin.PostAsJsonAsync("/api/v1/admin/invitations",
            new CreateInvitationRequest { Email = email, DisplayName = "Owner invitada",
                Grant = "BusinessOwner", BusinessId = DevelopmentSeeder.BellaBusinessId }, Json);
        Assert.Equal(HttpStatusCode.Created, invitationResponse.StatusCode);
        var invitation = (await invitationResponse.Content.ReadFromJsonAsync<InvitationIssuedDto>(Json))!;
        Assert.True(await AcceptAsync(TokenFrom(invitation.AcceptPath), "ClaveOwnerInvitada!2026"));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(x => x.Email == email);
        Assert.Equal(MembershipRole.Owner, await db.BusinessMemberships
            .Where(x => x.BusinessId == DevelopmentSeeder.BellaBusinessId && x.UserId == user.Id)
            .Select(x => x.Role).SingleAsync());
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True(await users.IsInRoleAsync(user, "BusinessOwner"));
        Assert.False(await users.IsInRoleAsync(user, "BusinessWorker"));
    }

    [Fact]
    public async Task An_administrative_reset_closes_sessions_and_issues_a_one_time_link()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var email = $"reinicio-{Guid.NewGuid():N}@example.test";
        var invitation = await InviteAsync(admin, email, "Cuenta a reiniciar");
        Assert.True(await AcceptAsync(TokenFrom(invitation.AcceptPath), "PrimeraClave!2026"));

        using var affected = Client();
        await Login(affected, email, "PrimeraClave!2026");
        Assert.Equal(HttpStatusCode.OK, (await affected.GetAsync("/api/v1/admin/businesses")).StatusCode);

        var reset = (await (await admin.PostAsJsonAsync("/api/v1/admin/access-resets",
            new ResetAccessRequest { Email = email }, Json))
            .Content.ReadFromJsonAsync<InvitationIssuedDto>(Json))!;
        Assert.True(await AcceptAsync(TokenFrom(reset.AcceptPath), "SegundaClave!2026"));

        // La contraseña anterior deja de servir y la nueva funciona.
        using var stale = Client();
        Assert.False(await TryLogin(stale, email, "PrimeraClave!2026"));
        using var renewed = Client();
        Assert.True(await TryLogin(renewed, email, "SegundaClave!2026"));
    }

    [Fact]
    public async Task Accepting_a_business_invitation_creates_the_membership_and_is_audited()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var business = await CreateAsync(admin, catalog, $"propietario-{Guid.NewGuid():N}", withOwner: false);
        var email = $"duenio-{Guid.NewGuid():N}@example.test";
        var issued = (await (await admin.PostAsJsonAsync("/api/v1/admin/invitations", new CreateInvitationRequest
        {
            Email = email, DisplayName = "Persona propietaria", Grant = "BusinessOwner", BusinessId = business.Id
        }, Json)).Content.ReadFromJsonAsync<InvitationIssuedDto>(Json))!;
        Assert.True(await AcceptAsync(TokenFrom(issued.AcceptPath), "ClaveDelDuenio!2026"));

        var refreshed = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{business.Id}", Json))!;
        Assert.Equal(email, refreshed.OwnerEmail);

        var audit = (await admin.GetFromJsonAsync<List<PlatformAccessAuditDto>>(
            "/api/v1/admin/access-audit", Json))!;
        Assert.Contains(audit, x => x.Action == "InvitationCreated" && x.Summary.Contains(email));
        Assert.Contains(audit, x => x.Action == "InvitationAccepted" && x.Summary.Contains(email));
        // La auditoría nunca guarda el token ni la contraseña.
        var token = TokenFrom(issued.AcceptPath);
        Assert.DoesNotContain(audit, x => x.Summary.Contains(token) || x.Summary.Contains("ClaveDelDuenio"));
    }

    // ---------------------------------------------------------------- imágenes

    [Fact]
    public async Task An_svg_is_rejected_no_matter_what_the_client_declares()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = await NewBusinessAsync(admin, $"svg-{Guid.NewGuid():N}");
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        // El cliente miente en el nombre y en el content type; manda la firma binaria.
        var response = await PlatformAdministrationApiTests.UploadImageAsync(admin, business.Id, "Logo",
            "logo.png", "image/png", svg);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_executable_disguised_as_an_image_is_rejected()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = await NewBusinessAsync(admin, $"exe-{Guid.NewGuid():N}");
        var executable = new byte[512];
        executable[0] = 0x4D; executable[1] = 0x5A; // Cabecera MZ de un ejecutable de Windows.
        var response = await PlatformAdministrationApiTests.UploadImageAsync(admin, business.Id, "Logo",
            "logo.jpg", "image/jpeg", executable);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_oversized_file_is_rejected()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = await NewBusinessAsync(admin, $"grande-{Guid.NewGuid():N}");
        var oversized = new byte[6 * 1024 * 1024];
        var response = await PlatformAdministrationApiTests.UploadImageAsync(admin, business.Id, "Logo",
            "logo.jpg", "image/jpeg", oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task An_uploaded_photo_loses_its_exif_metadata_and_is_resized()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = await NewBusinessAsync(admin, $"exif-{Guid.NewGuid():N}");

        var original = PhotoWithGpsExif(2400, 1200);
        using (var check = Image.Load(original)) Assert.NotNull(check.Metadata.ExifProfile);

        var response = await PlatformAdministrationApiTests.UploadImageAsync(admin, business.Id, "Cover",
            "vacaciones.jpg", "image/jpeg", original, "Fachada del local");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = (await response.Content.ReadFromJsonAsync<BusinessImageDto>(Json))!;
        // Una portada se muestra a 640 px en la tarjeta y a ancho completo en la ficha: se
        // guarda a 1280 px, no a 1600, y siempre en WebP.
        Assert.Equal(1280, stored.Width);
        Assert.Equal(640, stored.Height);
        Assert.Equal("Fachada del local", stored.AltText);
        Assert.EndsWith(".webp", stored.Url);

        var served = await admin.GetByteArrayAsync(stored.Url);
        using var processed = Image.Load(served);
        Assert.Null(processed.Metadata.ExifProfile);
        Assert.Equal(1280, processed.Width);
    }

    [Fact]
    public async Task The_logo_and_the_cover_stay_unique_and_the_gallery_is_capped_at_eight()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = await NewBusinessAsync(admin, $"galeria-{Guid.NewGuid():N}");
        var png = PlatformAdministrationApiTests.TinyPng();

        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Created, (await PlatformAdministrationApiTests.UploadImageAsync(
                admin, business.Id, "Logo", "logo.png", "image/png", png)).StatusCode);
        var afterReplacement = (await admin.GetFromJsonAsync<List<BusinessImageDto>>(
            $"/api/v1/admin/businesses/{business.Id}/images", Json))!;
        Assert.Single(afterReplacement, x => x.Kind == "Logo");

        for (var i = 0; i < BusinessImage.MaximumGalleryImages; i++)
            Assert.Equal(HttpStatusCode.Created, (await PlatformAdministrationApiTests.UploadImageAsync(
                admin, business.Id, "Gallery", $"foto{i}.png", "image/png", png)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PlatformAdministrationApiTests.UploadImageAsync(
            admin, business.Id, "Gallery", "sobrante.png", "image/png", png)).StatusCode);
    }

    [Fact]
    public async Task Images_are_isolated_by_business()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var mine = await NewBusinessAsync(admin, $"mio-{Guid.NewGuid():N}");

        using var partner = Client();
        await Login(partner, DevelopmentSeeder.PartnerOperatorEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await partner.GetAsync($"/api/v1/admin/businesses/{mine.Id}/images")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PlatformAdministrationApiTests.UploadImageAsync(
            partner, mine.Id, "Logo", "logo.png", "image/png",
            PlatformAdministrationApiTests.TinyPng())).StatusCode);
    }

    // ------------------------------------------- revisión, publicación y estado

    [Fact]
    public async Task The_full_review_cycle_controls_public_visibility()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var slug = $"ciclo-{Guid.NewGuid():N}";
        var business = await CreateAsync(admin, catalog, slug);
        var ready = await PlatformAdministrationApiTests.CompleteChecklistAsync(admin, business, catalog);

        // La vista previa funciona antes de publicar; el directorio público todavía no lo muestra.
        var preview = (await admin.GetFromJsonAsync<BusinessProfileDto>(
            $"/api/v1/admin/businesses/{business.Id}/preview", Json))!;
        Assert.True(preview.IsPreview);
        Assert.Contains(preview.Images!, x => x.Kind == "Logo");
        Assert.DoesNotContain(await PublicDirectory(admin), x => x.Slug == slug);

        var reviewed = await PostAsync<PlatformBusinessDto>(admin,
            $"/api/v1/admin/businesses/{business.Id}/submit-review",
            new SubmitForReviewRequest { Version = ready.Version });
        Assert.Equal("PendingReview", reviewed.Status);
        Assert.DoesNotContain(await PublicDirectory(admin), x => x.Slug == slug);

        var rejected = await PostAsync<PlatformBusinessDto>(admin,
            $"/api/v1/admin/businesses/{business.Id}/reject-review",
            new RejectReviewRequest { Version = reviewed.Version, Notes = "La portada está borrosa." });
        Assert.Equal("PendingConfiguration", rejected.Status);
        Assert.Equal("La portada está borrosa.", rejected.ReviewNotes);

        var resubmitted = await PostAsync<PlatformBusinessDto>(admin,
            $"/api/v1/admin/businesses/{business.Id}/submit-review",
            new SubmitForReviewRequest { Version = rejected.Version });
        var published = await PostAsync<PlatformBusinessDto>(admin,
            $"/api/v1/admin/businesses/{business.Id}/activate",
            new PlatformBusinessStateRequest { Version = resubmitted.Version });
        Assert.Equal("Active", published.Status);
        Assert.Null(published.ReviewNotes);

        var card = Assert.Single(await PublicDirectory(admin), x => x.Slug == slug);
        Assert.NotNull(card.LogoUrl);
        Assert.NotNull(card.CoverUrl);

        var history = (await admin.GetFromJsonAsync<List<BusinessStatusChangeDto>>(
            $"/api/v1/admin/businesses/{business.Id}/status-history", Json))!;
        Assert.Contains(history, x => x.ToStatus == "PendingReview");
        Assert.Contains(history, x => x.Notes == "La portada está borrosa.");
        Assert.Contains(history, x => x.ToStatus == "Active");
    }

    [Fact]
    public async Task An_incomplete_business_cannot_be_sent_to_review()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = await NewBusinessAsync(admin, $"incompleto-{Guid.NewGuid():N}");
        var response = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{business.Id}/submit-review",
            new SubmitForReviewRequest { Version = business.Version }, Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Cargue el logo", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_business_created_from_the_console_is_bookable_once_published()
    {
        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var slug = $"agendable-{Guid.NewGuid():N}";
        var business = await CreateAsync(admin, catalog, slug);
        var ready = await PlatformAdministrationApiTests.CompleteChecklistAsync(admin, business, catalog);
        var reviewed = await PostAsync<PlatformBusinessDto>(admin,
            $"/api/v1/admin/businesses/{business.Id}/submit-review",
            new SubmitForReviewRequest { Version = ready.Version });
        await PostAsync<PlatformBusinessDto>(admin, $"/api/v1/admin/businesses/{business.Id}/activate",
            new PlatformBusinessStateRequest { Version = reviewed.Version });

        // El horario que la consola crea debe ser una jornada real, no un intervalo de microsegundos.
        var profile = (await admin.GetFromJsonAsync<BusinessProfileDto>(
            $"/api/v1/public/businesses/{slug}", Json))!;
        var hours = Assert.Single(profile.Hours, x => x.Day == DayOfWeek.Monday);
        Assert.Equal("08:00", hours.OpensAt);
        Assert.Equal("18:00", hours.ClosesAt);

        var serviceId = Assert.Single(profile.Services).Id;
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));
        while (date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
        var slots = (await admin.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/{slug}/appointment-slots?serviceId={serviceId}&date={date:yyyy-MM-dd}",
            Json))!;
        Assert.NotEmpty(slots.Slots);
    }

    // ------------------------------------------------------------ consentimiento

    [Fact]
    public async Task A_queue_ticket_requires_consent_and_records_the_accepted_version()
    {
        using var client = Client();
        var slug = "barberia-el-corte";
        using var owner = Client();
        await Login(owner, DevelopmentSeeder.CorteOwnerEmail);
        await owner.PostAsync($"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/queue/open", null);

        var refused = await client.PostAsJsonAsync($"/api/v1/public/businesses/{slug}/queue/tickets",
            new CreateQueueTicketRequest { Alias = "Sin aviso", ConsentAccepted = false }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var wrongVersion = await client.PostAsJsonAsync($"/api/v1/public/businesses/{slug}/queue/tickets",
            new CreateQueueTicketRequest
            {
                Alias = "Versión vieja", ConsentAccepted = true, ConsentNoticeVersion = "version-inventada"
            }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, wrongVersion.StatusCode);

        var accepted = await client.PostAsJsonAsync($"/api/v1/public/businesses/{slug}/queue/tickets",
            new CreateQueueTicketRequest
            {
                Alias = "Con aviso", ConsentAccepted = true,
                ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion
            }, Json);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = await db.ConsentReceipts.Where(x => x.QueueTicketId != null)
            .OrderByDescending(x => x.AcceptedAtUtc).FirstAsync();
        Assert.Equal(ConsentPolicyProvider.FallbackVersion, receipt.NoticeVersion);
        Assert.Equal(DevelopmentSeeder.CorteBusinessId, receipt.BusinessId);
    }

    [Fact]
    public async Task The_public_legal_endpoint_reports_the_version_the_server_will_demand()
    {
        using var client = Client();
        var legal = (await client.GetFromJsonAsync<LegalInfoDto>("/api/v1/public/legal", Json))!;
        Assert.Equal(ConsentPolicyProvider.FallbackVersion, legal.PolicyVersion);
    }

    // ------------------------------------------------------------------ salud

    [Fact]
    public async Task The_health_screen_is_private_and_reports_the_installation_state()
    {
        using var anonymous = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/admin/health")).StatusCode);

        using var admin = Client();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var health = (await admin.GetFromJsonAsync<PlatformHealthDto>("/api/v1/admin/health", Json))!;
        Assert.Equal("Development", health.Environment);
        Assert.Contains("Conectada", health.DatabaseStatus);
        Assert.Contains("Disponible", health.ObjectStorageStatus);
        Assert.Equal("Local", health.ObjectStorageProvider);
    }

    // ---------------------------------------------------------------- auxiliares

    private async Task<IReadOnlyList<BusinessCardDto>> PublicDirectory(HttpClient client)
        => (await client.GetFromJsonAsync<List<BusinessCardDto>>("/api/v1/public/businesses", Json))!;

    private async Task<PlatformBusinessDto> NewBusinessAsync(HttpClient client, string slug)
    {
        var catalog = (await client.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        return await CreateAsync(client, catalog, slug);
    }

    private static async Task<PlatformBusinessDto> CreateAsync(HttpClient client, PlatformBusinessListDto catalog,
        string slug, bool withOwner = true)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/businesses", new CreatePlatformBusinessRequest
        {
            Name = $"Negocio {slug[^6..]}", Slug = slug,
            MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
            ShortDescription = "Negocio ficticio de pruebas.",
            Description = "Negocio ficticio de pruebas", Appointments = true,
            InitialServiceName = "Servicio de prueba",
            ExistingOwnerEmail = withOwner ? DevelopmentSeeder.BellaOwnerEmail : null,
            SaveAsDraft = true
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string url, object body)
    {
        var response = await client.PostAsJsonAsync(url, body, Json);
        Assert.True(response.IsSuccessStatusCode,
            $"{url} devolvió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }

    private static async Task<InvitationIssuedDto> InviteAsync(HttpClient admin, string email, string displayName)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/invitations", new CreateInvitationRequest
        {
            Email = email, DisplayName = displayName, Grant = "PartnerOperator"
        }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<InvitationIssuedDto>(Json))!;
    }

    private static string TokenFrom(string acceptPath)
        => Uri.UnescapeDataString(acceptPath.Split("token=", 2)[1]);

    /// <summary>Recorre la pantalla real de aceptación, incluida su protección antifalsificación.</summary>
    private async Task<bool> AcceptAsync(string token, string password)
    {
        using var client = Client();
        var url = $"/Account/AcceptInvitation?token={Uri.EscapeDataString(token)}";
        var html = await client.GetStringAsync(url);
        var match = AntiforgeryRegex().Match(html);
        if (!match.Success) return false;
        var response = await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = match.Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&"),
            ["_handler"] = "accept-invitation",
            ["Input.Password"] = password, ["Input.Confirmation"] = password
        }));
        return response.StatusCode == HttpStatusCode.Redirect;
    }

    /// <summary>JPEG con perfil EXIF que incluye una coordenada, para comprobar que se elimina.</summary>
    private static byte[] PhotoWithGpsExif(int width, int height)
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Make, "CámaraDePrueba");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        image.Metadata.ExifProfile = exif;
        using var buffer = new MemoryStream();
        image.Save(buffer, new JpegEncoder { Quality = 90 });
        return buffer.ToArray();
    }

    private static Task Login(HttpClient client, string email)
        => Login(client, email, DevelopmentSeeder.DemoPassword);

    private static async Task Login(HttpClient client, string email, string password)
        => Assert.True(await TryLogin(client, email, password), $"No fue posible iniciar sesión como {email}.");

    private static async Task<bool> TryLogin(HttpClient client, string email, string password)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = password, ["Input.RememberMe"] = "false"
        }));
        return response.StatusCode == HttpStatusCode.Redirect;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
