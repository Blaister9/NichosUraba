using System.Text.Json;
using Microsoft.Playwright;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;
using UrabaConecta.Infrastructure.Security;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Recorrido completo de la Fase 14: el administrador técnico crea una socia, la socia
/// configura un negocio desde la interfaz, lo envía a revisión, el administrador lo publica,
/// la persona propietaria opera y el negocio se suspende y se reactiva.
/// </summary>
public sealed class FounderProductionJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private const string TinyPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task A_partner_configures_a_business_from_the_interface_and_the_platform_publishes_it()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var partnerEmail = $"socia-{suffix}@example.test";
        var ownerEmail = $"duenia-{suffix}@example.test";
        const string partnerPassword = "ClaveDeLaSocia!2026";
        const string ownerPassword = "ClaveDeLaDuenia!2026";
        var slug = $"fundador-{suffix}";

        // 1. El administrador técnico invita a la socia y copia el enlace de un solo uso.
        await using var adminContext = await fixture.Browser.NewContextAsync();
        var admin = await adminContext.NewPageAsync();
        await Login(admin, DevelopmentSeeder.PlatformAdminEmail, DevelopmentSeeder.DemoPassword);
        await admin.GotoAsync($"{fixture.BaseUrl}/admin/accesos");
        await WaitForSection(admin, "Invitar a una socia");
        await SubmitUntilNotice(admin, async () =>
        {
            await admin.GetByLabel("Correo", new() { Exact = true }).First.FillAsync(partnerEmail);
            await admin.GetByLabel("Nombre visible").FillAsync("Socia fundadora");
        }, "Generar enlace de invitación", "Enlace generado");
        var partnerLink = await admin.GetByLabel("Enlace generado").InputValueAsync();
        Assert.Contains("/Account/AcceptInvitation?token=", partnerLink);

        // 2. La socia abre el enlace y define su propia contraseña.
        await using var partnerContext = await fixture.Browser.NewContextAsync();
        var partner = await partnerContext.NewPageAsync();
        await AcceptInvitation(partner, partnerLink, partnerPassword);
        await Login(partner, partnerEmail, partnerPassword);

        // 3. La socia crea el negocio y 4-5. completa su perfil desde la interfaz.
        var businessId = await CreateBusiness(partner, slug);
        await partner.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}");
        await WaitForSection(partner, "Perfil comercial");
        await SubmitUntilNotice(partner, async () =>
        {
            await partner.GetByLabel("Descripción breve (máx. 160)")
                .FillAsync("Salón ficticio del recorrido de producción fundadora.");
            await partner.GetByLabel("Descripción completa")
                .FillAsync("Descripción completa del negocio ficticio.");
            await partner.GetByLabel("Dirección").FillAsync("Calle 100 # 00-00");
            await partner.GetByLabel("Punto de referencia").FillAsync("Frente al parque principal");
            await partner.GetByLabel("Teléfono público").FillAsync("3001234567");
            await partner.GetByLabel("Correo público").FillAsync($"contacto-{suffix}@example.test");
        }, "Guardar perfil", "Perfil actualizado.");

        // 6-7. Sube logo, portada y una fotografía de galería.
        foreach (var kind in new[] { "Logo", "Cover", "Gallery" })
            Assert.Equal(201, await UploadImage(partner, businessId, kind));

        // 8. Configura horarios y una excepción desde el onboarding de la socia.
        var schedule = partner.GetByTestId("platform-schedule");
        await Expect(schedule).ToBeVisibleAsync();
        var monday = schedule.GetByTestId("platform-hour-row").Filter(new() { HasText = "Lunes" });
        var mondayOpening = monday.GetByLabel("Apertura Lunes");
        await Expect(mondayOpening).ToBeEnabledAsync();
        await mondayOpening.FillAsync("18:00");
        await monday.GetByLabel("Cierre Lunes").FillAsync("08:00");
        await monday.GetByRole(AriaRole.Button, new() { Name = "Guardar Lunes" }).ClickAsync();
        await Expect(schedule.GetByText("La apertura debe ser anterior al cierre.")).ToBeVisibleAsync();
        await monday.GetByLabel("Apertura Lunes").FillAsync("09:00");
        await monday.GetByLabel("Cierre Lunes").FillAsync("17:00");
        await monday.GetByRole(AriaRole.Button, new() { Name = "Guardar Lunes" }).ClickAsync();
        await Expect(schedule.GetByText("Horario guardado.")).ToBeVisibleAsync();
        await schedule.GetByRole(AriaRole.Button, new() { Name = "Guardar excepción" }).ClickAsync();
        await Expect(schedule.GetByText("Excepción guardada.")).ToBeVisibleAsync();
        await Expect(schedule.GetByTestId("platform-exception-row")).ToBeVisibleAsync();

        // 9. Invita a la persona propietaria y copia su enlace.
        await partner.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}");
        await WaitForSection(partner, "Personas con acceso");
        await SubmitUntilNotice(partner, async () =>
        {
            await partner.GetByLabel("Correo de la persona").FillAsync(ownerEmail);
            await partner.GetByLabel("Nombre visible").FillAsync("Persona propietaria");
            await partner.GetByLabel("Tipo de acceso").SelectOptionAsync("BusinessOwner");
        }, "Generar enlace de invitación", "Enlace generado");
        var ownerLink = await partner.GetByLabel("Enlace de invitación").InputValueAsync();

        await using var ownerContext = await fixture.Browser.NewContextAsync();
        var owner = await ownerContext.NewPageAsync();
        await AcceptInvitation(owner, ownerLink, ownerPassword);

        // 10. Previsualiza la ficha antes de publicar.
        await partner.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}/vista-previa");
        await Expect(partner.GetByRole(AriaRole.Heading, new() { Name = $"Piloto {slug}" })).ToBeVisibleAsync();
        await Expect(partner.GetByText("Salón ficticio del recorrido de producción fundadora."))
            .ToBeVisibleAsync();

        // El negocio todavía no aparece en el directorio público.
        await using var visitorContext = await fixture.Browser.NewContextAsync();
        var visitor = await visitorContext.NewPageAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/");
        await Expect(visitor.GetByText($"Piloto {slug}", new() { Exact = true })).ToHaveCountAsync(0);

        // 10. La socia envía a revisión; no puede publicar por sí misma.
        await partner.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}");
        await WaitForSection(partner, "Revisión y publicación");
        await ClickUntilNotice(partner, "Enviar a revisión", "Enviado a revisión.");
        await Expect(partner.Locator("span.tag").Filter(new() { HasText = "En revisión" })).ToBeVisibleAsync();
        await Expect(partner.GetByRole(AriaRole.Button, new() { Name = "Aprobar y publicar" })).ToHaveCountAsync(0);

        // 11. El administrador devuelve con observaciones y la socia lo reenvía.
        await admin.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}");
        await WaitForSection(admin, "Revisión y publicación");
        await SubmitUntilNotice(admin, async () =>
        {
            await admin.GetByLabel("Observaciones para la socia").FillAsync("Ajuste el punto de referencia.");
            // @bind se propaga al perder el foco: se fuerza el blur antes de pulsar el botón.
            await admin.Keyboard.PressAsync("Tab");
        }, "Devolver con observaciones", "Devuelto a la socia con observaciones.");
        await Expect(admin.Locator("div.notice").Filter(new() { HasText = "Ajuste el punto de referencia." }))
            .ToBeVisibleAsync();

        await partner.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}");
        await WaitForSection(partner, "Revisión y publicación");
        await ClickUntilNotice(partner, "Enviar a revisión", "Enviado a revisión.");
        await Expect(partner.Locator("span.tag").Filter(new() { HasText = "En revisión" })).ToBeVisibleAsync();

        // 12. El administrador aprueba y publica.
        await admin.GotoAsync($"{fixture.BaseUrl}/admin/negocios/{businessId}");
        await WaitForSection(admin, "Revisión y publicación");
        await ClickUntilNotice(admin, "Aprobar y publicar", "Negocio publicado.");
        await Expect(admin.Locator("span.tag").Filter(new() { HasText = "Publicado" })).ToBeVisibleAsync();

        // 13. El negocio aparece públicamente, con su logo y su ficha completa.
        await visitor.GotoAsync($"{fixture.BaseUrl}/");
        await Expect(visitor.GetByText($"Piloto {slug}", new() { Exact = true })).ToBeVisibleAsync();
        await visitor.GotoAsync($"{fixture.BaseUrl}/negocios/{slug}");
        await Expect(visitor.GetByAltText("Imagen ficticia de prueba").First).ToBeVisibleAsync();
        await Expect(visitor.GetByText("Frente al parque principal")).ToBeVisibleAsync();

        // 14. La persona propietaria entra con su propia contraseña y ve su negocio.
        await Login(owner, ownerEmail, ownerPassword);
        await owner.GotoAsync($"{fixture.BaseUrl}/panel/{businessId}/configuracion");
        await Expect(owner.GetByRole(AriaRole.Heading, new() { Name = "Configuración del negocio" }))
            .ToBeVisibleAsync();

        // 15. Aislamiento: no alcanza otro negocio ni la administración global.
        var foreign = await Fetch(owner, "GET",
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services");
        Assert.Equal(403, foreign.Status);
        Assert.Equal(403, (await Fetch(owner, "GET", "/api/v1/admin/businesses")).Status);

        // 16. Se crea una operación pública real y la persona propietaria la procesa.
        // El horario ficticio cubre de lunes a sábado: se busca el primer día con cupos disponibles.
        // El visitante elige el servicio desde la ficha pública, igual que una persona real.
        var publicProfile = JsonSerializer.Deserialize<BusinessProfileDto>(
            (await Fetch(visitor, "GET", $"/api/v1/public/businesses/{slug}")).Body, Json)!;
        var serviceId = Assert.Single(publicProfile.Services).Id;
        var (date, slots) = await FirstDayWithSlots(visitor, slug, serviceId);

        var legal = JsonSerializer.Deserialize<LegalInfoDto>(
            (await Fetch(visitor, "GET", "/api/v1/public/legal")).Body, Json)!;
        var appointmentResponse = await Fetch(visitor, "POST",
            $"/api/v1/public/businesses/{slug}/appointments",
            AppointmentBody(serviceId, slots.Slots[0].Start, "Cliente ficticio", legal.PolicyVersion));
        Assert.True(appointmentResponse.Status == 201,
            $"La cita devolvió {appointmentResponse.Status}: {appointmentResponse.Body}" +
            Environment.NewLine + fixture.RecentLog);

        var appointments = JsonSerializer.Deserialize<List<AppointmentAdminDto>>(
            (await Fetch(owner, "GET", $"/api/v1/businesses/{businessId}/appointments")).Body, Json)!;
        var appointment = Assert.Single(appointments);
        Assert.Equal(legal.PolicyVersion, appointment.ConsentNoticeVersion);
        var confirmed = await Fetch(owner, "POST",
            $"/api/v1/businesses/{businessId}/appointments/{appointment.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Confirmed" });
        Assert.Equal(200, confirmed.Status);

        // 17. Suspensión: sale del directorio y no admite operaciones nuevas.
        var current = JsonSerializer.Deserialize<PlatformBusinessDto>(
            (await Fetch(admin, "GET", $"/api/v1/admin/businesses/{businessId}")).Body, Json)!;
        var suspended = JsonSerializer.Deserialize<PlatformBusinessDto>((await Fetch(admin, "POST",
            $"/api/v1/admin/businesses/{businessId}/suspend",
            new PlatformBusinessStateRequest { Version = current.Version, Reason = "Pausa del recorrido" })).Body,
            Json)!;
        Assert.Equal("Suspended", suspended.Status);
        await visitor.GotoAsync($"{fixture.BaseUrl}/");
        await Expect(visitor.GetByText($"Piloto {slug}", new() { Exact = true })).ToHaveCountAsync(0);
        var blocked = await Fetch(visitor, "POST", $"/api/v1/public/businesses/{slug}/appointments",
            AppointmentBody(serviceId, slots.Slots[^1].Start, "Cliente bloqueado", legal.PolicyVersion));
        Assert.Equal(404, blocked.Status);

        // 18. Reactivación: vuelve a aparecer y el historial de estados quedó registrado.
        var reactivated = await Fetch(admin, "POST", $"/api/v1/admin/businesses/{businessId}/reactivate",
            new PlatformBusinessStateRequest { Version = suspended.Version });
        Assert.Equal(200, reactivated.Status);
        await visitor.GotoAsync($"{fixture.BaseUrl}/");
        await Expect(visitor.GetByText($"Piloto {slug}", new() { Exact = true })).ToBeVisibleAsync();

        var history = JsonSerializer.Deserialize<List<BusinessStatusChangeDto>>(
            (await Fetch(admin, "GET", $"/api/v1/admin/businesses/{businessId}/status-history")).Body, Json)!;
        Assert.Contains(history, x => x.ToStatus == "PendingReview");
        Assert.Contains(history, x => x.Notes == "Ajuste el punto de referencia.");
        Assert.Contains(history, x => x.ToStatus == "Suspended");
        Assert.Contains(history, x => x.ToStatus == "Active");

        // 19. La auditoría de accesos registró la invitación y su aceptación, sin secretos.
        var audit = JsonSerializer.Deserialize<List<PlatformAccessAuditDto>>(
            (await Fetch(admin, "GET", "/api/v1/admin/access-audit")).Body, Json)!;
        Assert.Contains(audit, x => x.Action == "PartnerOperatorCreated" && x.Summary.Contains(partnerEmail));
        Assert.Contains(audit, x => x.Action == "InvitationAccepted" && x.Summary.Contains(ownerEmail));
        Assert.DoesNotContain(audit, x => x.Summary.Contains(partnerPassword) || x.Summary.Contains(ownerPassword));
    }

    // ---------------------------------------------------------------- auxiliares

    /// <summary>
    /// Cuerpo de la solicitud de cita. La fecha va como texto ISO porque el puente de Playwright
    /// serializa un DateTimeOffset como objeto y el enlazador del servidor lo rechazaría.
    /// </summary>
    private static object AppointmentBody(Guid serviceId, DateTimeOffset start, string alias, string policyVersion)
        => new
        {
            serviceId, start = start.ToString("O"), customerAlias = alias, phone = "3009998877",
            consentAccepted = true, consentNoticeVersion = policyVersion
        };

    /// <summary>Busca el primer día de la próxima semana con cupos para el servicio indicado.</summary>
    private static async Task<(DateOnly Date, SlotListDto Slots)> FirstDayWithSlots(IPage page, string slug,
        Guid serviceId)
    {
        var attempts = new List<string>();
        for (var offset = 1; offset <= 8; offset++)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(offset));
            var response = await Fetch(page, "GET",
                $"/api/v1/public/businesses/{slug}/appointment-slots?serviceId={serviceId}&date={date:yyyy-MM-dd}");
            attempts.Add($"{date:yyyy-MM-dd}={response.Status}:{response.Body}");
            if (response.Status != 200) continue;
            var slots = JsonSerializer.Deserialize<SlotListDto>(response.Body, Json)!;
            if (slots.Slots.Count > 0) return (date, slots);
        }
        Assert.Fail($"El negocio publicado no ofrece cupos: {string.Join(" | ", attempts)}");
        throw new InvalidOperationException();
    }

    /// <summary>
    /// Espera a que el circuito interactivo habilite la acción, la ejecuta una sola vez y comprueba
    /// su efecto observable. Repetir clics ocultaría regresiones reales y no representa al usuario.
    /// </summary>
    private async Task<string> SubmitUntilNotice(IPage page, Func<Task> fill, string button,
        string expectedPrefix)
    {
        var notice = page.Locator("p.notice[role=alert]");
        var action = page.GetByRole(AriaRole.Button, new() { Name = button });
        await Assertions.Expect(action).ToBeEnabledAsync();
        await fill();
        await action.ClickAsync();
        try { await Assertions.Expect(notice).ToBeVisibleAsync(); }
        catch (PlaywrightException)
        {
            Assert.Fail($"La acción «{button}» no produjo aviso.{Environment.NewLine}{fixture.RecentLog}");
        }
        var text = (await notice.TextContentAsync())!.Trim();
        Assert.StartsWith(expectedPrefix, text);
        return text;
    }

    private Task<string> ClickUntilNotice(IPage page, string button, string expectedPrefix)
        => SubmitUntilNotice(page, () => Task.CompletedTask, button, expectedPrefix);

    /// <summary>Espera a que la sección esté renderizada antes de interactuar con ella.</summary>
    private static Task WaitForSection(IPage page, string heading)
        => Expect(page.GetByRole(AriaRole.Heading, new() { Name = heading })).ToBeVisibleAsync();

    private async Task<Guid> CreateBusiness(IPage partner, string slug)
    {
        var catalog = JsonSerializer.Deserialize<PlatformBusinessListDto>(
            (await Fetch(partner, "GET", "/api/v1/admin/businesses")).Body, Json)!;
        var response = await Fetch(partner, "POST", "/api/v1/admin/businesses", new CreatePlatformBusinessRequest
        {
            Name = $"Piloto {slug}", Slug = slug, MunicipalityId = catalog.Municipalities[0].Id,
            CategoryId = catalog.Categories[0].Id, Description = "Negocio ficticio del recorrido",
            Appointments = true, InitialServiceName = "Corte ficticio", SaveAsDraft = true
        });
        Assert.Equal(201, response.Status);
        return JsonSerializer.Deserialize<PlatformBusinessCreatedDto>(response.Body, Json)!.Business.Id;
    }

    private static async Task<int> UploadImage(IPage page, Guid businessId, string kind)
        => await page.EvaluateAsync<int>(
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

    private async Task AcceptInvitation(IPage page, string relativeLink, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}{relativeLink}");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Crea tu contraseña" })).ToBeVisibleAsync();
        await page.GetByLabel("Contraseña nueva").FillAsync(password);
        await page.GetByLabel("Confirma la contraseña").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Activar mi acceso" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/Account/Login"));
    }

    private async Task Login(IPage page, string email, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel") || url.Contains("/Account/ChangeTemporaryPassword"));
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

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private sealed class FetchResult
    {
        public int Status { get; set; }
        public string Body { get; set; } = "null";
    }
}
