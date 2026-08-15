using System.Text.Json;
using Microsoft.Playwright;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.EndToEndTests;

public sealed class PlatformOnboardingJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Scenario_1_create_appointment_pilot_activate_and_owner_enters_configuration()
    {
        await using var adminContext = await fixture.Browser.NewContextAsync();
        var admin = await adminContext.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        var slug = Unique("salon");
        var pilotEmail = $"{slug}@example.test";
        var created = await CreateWithWizard(admin, slug, pilotEmail);
        var published = await CompleteAndPublish(admin, created.Business);
        Assert.Equal("Active", published.Status);
        await admin.GotoAsync($"{fixture.BaseUrl}/explorar?q={Uri.EscapeDataString(created.Business.Name)}");
        await Expect(admin.GetByText(created.Business.Name, new() { Exact = true })).ToBeVisibleAsync();

        await using var ownerContext = await fixture.Browser.NewContextAsync();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, pilotEmail, created.TemporaryPassword!);
        await owner.WaitForURLAsync(url => url.Contains("/Account/ChangeTemporaryPassword"));
        var permanent = "NuevaClave!2026A";
        await ChangeTemporaryPassword(owner, created.TemporaryPassword!, permanent);
        await owner.GotoAsync($"{fixture.BaseUrl}/panel/{created.Business.Id}/configuracion");
        await Expect(owner.GetByRole(AriaRole.Heading, new() { Name = "Configuración del negocio" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Scenario_2_create_queue_pilot_open_queue_and_show_public_flow()
    {
        await using var adminContext = await fixture.Browser.NewContextAsync();
        var admin = await adminContext.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        var created = await Create(admin, Unique("barberia"), queues: true, saveAsDraft: false);
        Assert.Equal("Active", (await CompleteAndPublish(admin, created.Business)).Status);
        await using var ownerContext = await fixture.Browser.NewContextAsync();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        await owner.GotoAsync($"{fixture.BaseUrl}/panel/{created.Business.Id}/turnos");
        await owner.GetByRole(AriaRole.Button, new() { Name = "Abrir jornada" }).ClickAsync();
        await using var publicContext = await fixture.Browser.NewContextAsync();
        var publicPage = await publicContext.NewPageAsync();
        await publicPage.GotoAsync($"{fixture.BaseUrl}/negocios/{created.Business.Slug}/turnos");
        await Expect(publicPage.GetByRole(AriaRole.Heading, new() { Name = "Tomar un turno" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Scenario_3_create_restaurant_and_publish_pickup_menu()
    {
        await using var context = await fixture.Browser.NewContextAsync(new()
        {
            ViewportSize = new() { Width = 390, Height = 844 }
        });
        var admin = await context.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        var created = await Create(admin, Unique("restaurante"), orders: true, product: "Bowl piloto",
            productCategory: "Almuerzos", saveAsDraft: false);
        Assert.Equal("Active", (await CompleteAndPublish(admin, created.Business)).Status);
        await admin.GotoAsync($"{fixture.BaseUrl}/negocios/{created.Business.Slug}/pedidos");
        await Expect(admin.GetByText("Bowl piloto", new() { Exact = true })).ToBeVisibleAsync();
        Assert.False(await admin.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth"));
    }

    [Fact]
    public async Task Scenario_4_incomplete_configuration_blocks_activation_until_service_exists()
    {
        await using var adminContext = await fixture.Browser.NewContextAsync();
        var admin = await adminContext.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        var created = await Create(admin, Unique("incompleto"), appointments: true, saveAsDraft: true);
        await admin.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{created.Business.Id}");
        await Expect(admin.Locator(".checklist-item").Filter(new() { HasText = "Servicios" })).ToBeVisibleAsync();
        await Expect(admin.GetByRole(AriaRole.Button, new() { Name = "Enviar a revisión" })).ToBeDisabledAsync();

        await using var ownerContext = await fixture.Browser.NewContextAsync();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        var serviceResponse = await Fetch(owner, "POST", $"/api/v1/businesses/{created.Business.Id}/services",
            new CreateServiceRequest { Name = "Servicio completado", DurationMinutes = 30, ReferencePrice = 0 });
        Assert.Equal(201, serviceResponse.Status);
        var refreshed = await Fetch(admin, "GET", $"/api/v1/admin/businesses/{created.Business.Id}");
        var withService = JsonSerializer.Deserialize<PlatformBusinessDto>(refreshed.Body, Json)!;
        // El servicio ya existe, pero la identidad visual sigue faltando: aún no se puede enviar a revisión.
        Assert.False(withService.IsReady);
        Assert.Contains(withService.Readiness, x => x.Key == "logo" && !x.IsComplete);
        Assert.Equal("Active", (await CompleteAndPublish(admin, withService)).Status);
        await admin.ReloadAsync();
        await Expect(admin.Locator("[data-testid=ficha-estado]")).ToHaveTextAsync("Publicado");
    }

    [Fact]
    public async Task Scenario_5_suspension_hides_public_entry_preserves_tracking_and_reactivation()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var admin = await context.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        var created = await Create(admin, Unique("suspension"), queues: true, saveAsDraft: false);
        var published = await CompleteAndPublish(admin, created.Business);
        await using var ownerContext = await fixture.Browser.NewContextAsync();
        var owner = await ownerContext.NewPageAsync();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        var open = await Fetch(owner, "POST", $"/api/v1/businesses/{created.Business.Id}/queue/open");
        Assert.Equal(200, open.Status);
        var ticketResponse = await Fetch(admin, "POST",
            $"/api/v1/public/businesses/{created.Business.Slug}/queue/tickets",
            new CreateQueueTicketRequest { Alias = "Historia E2E", ConsentAccepted = true, ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion });
        Assert.Equal(201, ticketResponse.Status);
        var ticket = JsonSerializer.Deserialize<QueueTicketCreatedDto>(ticketResponse.Body, Json)!;
        var version = published.Version;
        var suspended = await Fetch(admin, "POST", $"/api/v1/admin/businesses/{created.Business.Id}/suspend",
            new PlatformBusinessStateRequest { Version = version, Reason = "Pausa E2E" });
        Assert.Equal(200, suspended.Status);
        await admin.GotoAsync($"{fixture.BaseUrl}/explorar?q={Uri.EscapeDataString(created.Business.Name)}");
        await Expect(admin.GetByText(created.Business.Name, new() { Exact = true })).ToHaveCountAsync(0);
        var publicWrite = await Fetch(admin, "POST",
            $"/api/v1/public/businesses/{created.Business.Slug}/queue/tickets",
            new CreateQueueTicketRequest { Alias = "Bloqueado", ConsentAccepted = true, ConsentNoticeVersion = ConsentPolicyProvider.FallbackVersion });
        Assert.True(publicWrite.Status == 404, $"La operación pública devolvió {publicWrite.Status}: {publicWrite.Body}");
        var historical = await Fetch(admin, "GET", $"/api/v1/public/queue/tickets/{ticket.TrackingCode}");
        Assert.Equal(200, historical.Status);
        var suspendedDto = JsonSerializer.Deserialize<PlatformBusinessDto>(suspended.Body, Json)!;
        var reactivated = await Fetch(admin, "POST", $"/api/v1/admin/businesses/{created.Business.Id}/reactivate",
            new PlatformBusinessStateRequest { Version = suspendedDto.Version });
        Assert.Equal(200, reactivated.Status);
        await admin.ReloadAsync();
        await Expect(admin.GetByText(created.Business.Name, new() { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Scenario_6_normal_owner_cannot_open_or_modify_global_administration()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var owner = await context.NewPageAsync();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        await owner.GotoAsync($"{fixture.BaseUrl}/admin/negocios");
        await owner.WaitForURLAsync(url => url.Contains("/Account/AccessDenied"));
        var response = await Fetch(owner, "PUT", $"/api/v1/admin/businesses/{DevelopmentSeeder.BellaBusinessId}/modules",
            new UpdatePlatformModulesRequest { Appointments = true, Version = 0 });
        Assert.Equal(403, response.Status);
    }

    [Fact]
    public async Task Scenario_7_temporary_password_blocks_panel_changes_once_and_cannot_be_reused()
    {
        await using var adminContext = await fixture.Browser.NewContextAsync();
        var admin = await adminContext.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        var slug = Unique("clave");
        var email = $"{slug}@example.test";
        var created = await Create(admin, slug, appointments: true, pilotEmail: email,
            service: "Servicio", saveAsDraft: false);
        var temporary = created.TemporaryPassword!;
        await using var pilotContext = await fixture.Browser.NewContextAsync();
        var pilot = await pilotContext.NewPageAsync();
        await Login(pilot, email, temporary);
        await pilot.GotoAsync($"{fixture.BaseUrl}/panel");
        await pilot.WaitForURLAsync(url => url.Contains("/Account/ChangeTemporaryPassword"));
        const string permanent = "ClaveDefinitiva!2026";
        await ChangeTemporaryPassword(pilot, temporary, permanent);
        await pilot.WaitForURLAsync(url => url.EndsWith("/panel"));

        await using var oldContext = await fixture.Browser.NewContextAsync();
        var old = await oldContext.NewPageAsync();
        await old.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await old.GetByLabel("Correo").FillAsync(email);
        await old.GetByLabel("Contraseña").FillAsync(temporary);
        await old.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await Expect(old.GetByText("Correo o contraseña incorrectos.")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Desde V5 la publicación exige el checklist completo (descripción breve, contacto, ubicación,
    /// logo y portada) y pasar por revisión. Este auxiliar recorre ese camino y devuelve el negocio publicado.
    /// </summary>
    private static async Task<PlatformBusinessDto> CompleteAndPublish(IPage admin, PlatformBusinessDto business)
    {
        var catalog = JsonSerializer.Deserialize<PlatformBusinessListDto>(
            (await Fetch(admin, "GET", "/api/v1/admin/businesses")).Body, Json)!;
        var savedResponse = await Fetch(admin, "PUT", $"/api/v1/admin/businesses/{business.Id}/profile",
            new SaveBusinessProfileRequest
            {
                Name = business.Name, Slug = business.Slug,
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                ShortDescription = "Negocio ficticio de la prueba de extremo a extremo.",
                Description = "Descripción completa del negocio ficticio.",
                Address = "Calle 1 # 1-1", PublicPhone = "3000000000", Version = business.Version
            });
        Assert.Equal(200, savedResponse.Status);
        foreach (var kind in new[] { "Logo", "Cover" })
            Assert.Equal(201, await UploadImage(admin, business.Id, kind));

        var ready = JsonSerializer.Deserialize<PlatformBusinessDto>(
            (await Fetch(admin, "GET", $"/api/v1/admin/businesses/{business.Id}")).Body, Json)!;
        Assert.True(ready.IsReady, string.Join(" ", ready.MissingLabels ?? []));
        var review = await Fetch(admin, "POST", $"/api/v1/admin/businesses/{business.Id}/submit-review",
            new SubmitForReviewRequest { Version = ready.Version });
        Assert.Equal(200, review.Status);
        var reviewed = JsonSerializer.Deserialize<PlatformBusinessDto>(review.Body, Json)!;
        Assert.Equal("PendingReview", reviewed.Status);
        var published = await Fetch(admin, "POST", $"/api/v1/admin/businesses/{business.Id}/activate",
            new PlatformBusinessStateRequest { Version = reviewed.Version });
        Assert.Equal(200, published.Status);
        return JsonSerializer.Deserialize<PlatformBusinessDto>(published.Body, Json)!;
    }

    /// <summary>Sube un PNG de 1x1 real como multipart, tal como lo hace el formulario del navegador.</summary>
    private static async Task<int> UploadImage(IPage admin, Guid businessId, string kind)
        => await admin.EvaluateAsync<int>(
            """
            async ({ businessId, kind, base64 }) => {
              const binary = atob(base64);
              const bytes = new Uint8Array(binary.length);
              for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
              const form = new FormData();
              form.append('file', new Blob([bytes], { type: 'image/png' }), 'foto.png');
              form.append('kind', kind);
              form.append('altText', 'Imagen ficticia de prueba');
              const response = await fetch(`/api/v1/admin/businesses/${businessId}/images`,
                { method: 'POST', credentials: 'same-origin', body: form });
              return response.status;
            }
            """, new { businessId, kind, base64 = TinyPngBase64 });

    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    private async Task<PlatformBusinessCreatedDto> Create(IPage admin, string slug, bool appointments = false,
        bool queues = false, bool orders = false, string? pilotEmail = null, string? service = null,
        string? product = null, string? productCategory = null, bool saveAsDraft = true)
    {
        var catalogResponse = await Fetch(admin, "GET", "/api/v1/admin/businesses");
        var catalog = JsonSerializer.Deserialize<PlatformBusinessListDto>(catalogResponse.Body, Json)!;
        var request = new CreatePlatformBusinessRequest
        {
            Name = $"Piloto {slug}", Slug = slug, MunicipalityId = catalog.Municipalities[0].Id,
            ShortDescription = "Piloto ficticio del recorrido E2E.",
            CategoryId = catalog.Categories[0].Id, Description = "Negocio piloto E2E",
            Appointments = appointments, VirtualQueues = queues, PickupOrders = orders,
            InitialServiceName = service, InitialProductName = product,
            InitialProductCategory = productCategory, InitialProductPrice = 18000,
            ExistingOwnerEmail = pilotEmail is null ? DevelopmentSeeder.BellaOwnerEmail : null,
            PilotEmail = pilotEmail, PilotDisplayName = pilotEmail is null ? null : "Propietaria piloto",
            SaveAsDraft = saveAsDraft
        };
        var response = await Fetch(admin, "POST", "/api/v1/admin/businesses", request);
        Assert.Equal(201, response.Status);
        return JsonSerializer.Deserialize<PlatformBusinessCreatedDto>(response.Body, Json)!;
    }

    private async Task<PlatformBusinessCreatedDto> CreateWithWizard(IPage admin, string slug, string pilotEmail)
    {
        // Los selectores van por data-testid y no por el texto visible: el copy de este asistente
        // cambia con el trabajo de experiencia y no debe romper el recorrido que verifica el alta.
        await admin.GotoAsync($"{fixture.BaseUrl}/admin/negocios/nuevo");
        await admin.Locator("[data-testid=campo-nombre]").FillAsync($"Piloto {slug}");
        await admin.Locator("[data-testid=campo-slug]").FillAsync(slug);
        await admin.Locator("[data-testid=campo-municipio]").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await admin.Locator("[data-testid=campo-categoria]").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await admin.Locator("[data-testid=campo-descripcion-breve]").FillAsync("Piloto ficticio del recorrido E2E.");
        await admin.Locator("[data-testid=campo-descripcion]").FillAsync("Negocio piloto E2E");
        await admin.Locator("[data-testid=continuar]").ClickAsync();
        await admin.GetByLabel("Servicio inicial").FillAsync("Corte piloto");
        await admin.Locator("[data-testid=continuar]").ClickAsync();
        // Crea una cuenta nueva (devuelve contraseña temporal), no vincula una existente.
        await admin.Locator("[data-testid=campo-propietario-nuevo-nombre]").FillAsync("Propietaria piloto");
        await admin.Locator("[data-testid=campo-propietario-nuevo]").FillAsync(pilotEmail);
        await admin.Locator("[data-testid=continuar]").ClickAsync();
        // El asistente ya no publica: siempre nace como borrador y la publicación pasa por la
        // lista de preparación y la revisión.
        await admin.Locator("[data-testid=crear]").ClickAsync();
        await Expect(admin.Locator("[data-testid=negocio-creado]")).ToBeVisibleAsync();
        var temporary = (await admin.Locator("p.temporary-secret").TextContentAsync())!.Trim();
        var href = await admin.Locator("[data-testid=continuar-configuracion]").GetAttributeAsync("href");
        var businessId = Guid.Parse(href!.Split('/').Last());
        var detailResponse = await Fetch(admin, "GET", $"/api/v1/admin/businesses/{businessId}");
        return new(JsonSerializer.Deserialize<PlatformBusinessDto>(detailResponse.Body, Json)!, temporary);
    }

    private async Task Login(IPage page, string email, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel") || url.Contains("/Account/ChangeTemporaryPassword"));
    }

    private static async Task ChangeTemporaryPassword(IPage page, string temporary, string permanent)
    {
        await page.GetByLabel("Contraseña temporal").FillAsync(temporary);
        await page.GetByLabel("Nueva contraseña", new() { Exact = true }).FillAsync(permanent);
        await page.GetByLabel("Confirma la nueva contraseña").FillAsync(permanent);
        await page.GetByRole(AriaRole.Button, new() { Name = "Guardar y continuar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/panel"));
    }

    private static async Task<FetchResult> Fetch(IPage page, string method, string path, object? body = null)
        => await page.EvaluateAsync<FetchResult>(
            """
            async ({ method, path, body }) => {
              const response = await fetch(path, {
                method,
                credentials: 'same-origin',
                headers: body === null ? {} : { 'content-type': 'application/json' },
                body: body === null ? undefined : JSON.stringify(body)
              });
              let json = null;
              try { json = await response.json(); } catch {}
              return { Status: response.status, Body: JSON.stringify(json) };
            }
            """, new { method, path, body });

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
    private sealed class FetchResult
    {
        public int Status { get; set; }
        public string Body { get; set; } = "null";
    }
}
