using System.Text.Json;
using Microsoft.Playwright;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Recorrido completo de una socia. Verifica lo que la auditoría V6 encontró roto: que su panel
/// era un callejón sin salida y que su trabajo sólo se alcanzaba descubriendo "Administración"
/// en la barra superior.
///
/// Una sola prueba, y no varias, porque la primera afirmación depende de que la socia todavía
/// no tenga negocios: partirla dejaría a las demás compartiendo el estado que la primera crea.
/// </summary>
public sealed class PartnerOperatorJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_partner_operator_creates_continues_and_submits_a_business_from_her_panel()
    {
        await using var context = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = 1366, Height = 768 } });
        var socia = await context.NewPageAsync();
        await Login(socia, DevelopmentSeeder.PartnerOperatorEmail, DevelopmentSeeder.DemoPassword);

        // --- 1. El panel es su centro de trabajo, no un mensaje sin salida -------------------
        await socia.GotoAsync($"{fixture.BaseUrl}/panel");
        await Expect(socia.Locator("[data-testid=panel-vacio-socia]")).ToBeVisibleAsync();
        await Expect(socia.Locator("[data-testid=crear-negocio]")).ToBeVisibleAsync();

        // La acción principal vive en su propia pantalla: no hace falta pasar por "Administración".
        await socia.Locator("[data-testid=crear-negocio]").ClickAsync();
        await socia.WaitForURLAsync(url => url.Contains("/admin/negocios/nuevo"));

        // --- 2. El asistente dice en qué paso está ------------------------------------------
        await Expect(socia.Locator("[data-testid=paso-1]")).ToHaveAttributeAsync("aria-current", "step");

        var nombre = $"Panadería {Unique("espiga")}";
        await socia.Locator("[data-testid=campo-nombre]").FillAsync(nombre);
        // El enlace de Blazor viaja al servidor, así que la propuesta llega en una segunda vuelta.
        await socia.Locator("[data-testid=campo-nombre]").BlurAsync();
        // La dirección web se propone sola: la socia no tiene que inventar un identificador.
        await Expect(socia.Locator("[data-testid=campo-slug]")).Not.ToHaveValueAsync("");
        var propuesta = await socia.Locator("[data-testid=campo-slug]").InputValueAsync();
        Assert.DoesNotContain(' ', propuesta);
        Assert.Equal(propuesta.ToLowerInvariant(), propuesta);

        await socia.Locator("[data-testid=campo-municipio]").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await socia.Locator("[data-testid=campo-categoria]").SelectOptionAsync(new SelectOptionValue { Index = 1 });
        await socia.Locator("[data-testid=campo-descripcion]").FillAsync("Panadería de barrio con producto del día.");
        await socia.Locator("[data-testid=campo-direccion]").FillAsync("Calle 50 # 20-15");
        await socia.Locator("[data-testid=campo-telefono]").FillAsync("3005557788");

        await socia.Locator("[data-testid=continuar]").ClickAsync();
        await Expect(socia.Locator("[data-testid=paso-2]")).ToHaveAttributeAsync("aria-current", "step");
        await Expect(socia.Locator("[data-testid=paso-1]")).ToHaveAttributeAsync("data-done", "true");
        await socia.GetByLabel("Servicio inicial").FillAsync("Pan del día");

        await socia.Locator("[data-testid=continuar]").ClickAsync();
        await socia.Locator("[data-testid=campo-propietario]").FillAsync(DevelopmentSeeder.BellaOwnerEmail);

        // --- 3. Guardar y salir: el trabajo a medias no se pierde ---------------------------
        await socia.Locator("[data-testid=guardar-salir]").ClickAsync();
        await socia.WaitForURLAsync(url => url.EndsWith("/panel"));

        // --- 4. Vuelve a entrar en una sesión nueva y el borrador sigue ahí -----------------
        await using var vuelta = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = 1366, Height = 768 } });
        var regreso = await vuelta.NewPageAsync();
        await Login(regreso, DevelopmentSeeder.PartnerOperatorEmail, DevelopmentSeeder.DemoPassword);
        await regreso.GotoAsync($"{fixture.BaseUrl}/panel");

        var tarjeta = regreso.Locator("[data-testid=negocio-administrado]").Filter(new() { HasText = nombre });
        await Expect(tarjeta).ToBeVisibleAsync();
        // Estado en español, nunca el nombre del enum.
        await Expect(tarjeta.Locator("[data-testid=negocio-estado]")).ToHaveTextAsync("Borrador");
        // Avance y pendientes calculados por el servidor, mostrados donde ella trabaja.
        await Expect(tarjeta.Locator("[data-testid=negocio-avance]")).ToBeVisibleAsync();
        await Expect(tarjeta.Locator("[data-testid=negocio-faltantes]")).ToBeVisibleAsync();
        Assert.DoesNotContain("Draft", await regreso.Locator("body").InnerTextAsync());

        // --- 5. Continuar el negocio incompleto --------------------------------------------
        await tarjeta.Locator("[data-testid=negocio-cta]").ClickAsync();
        await regreso.WaitForURLAsync(url => url.Contains("/admin/negocios/"));
        await Expect(regreso.Locator("[data-testid=ficha-nombre]")).ToHaveTextAsync(nombre);
        await Expect(regreso.Locator("[data-testid=ficha-estado]")).ToHaveTextAsync("Borrador");

        var negocio = await Load(regreso, regreso.Url.Split('/').Last());
        Assert.False(negocio.IsReady);

        // --- 6. Completar lo que falta -----------------------------------------------------
        var catalogo = JsonSerializer.Deserialize<PlatformBusinessListDto>(
            (await Fetch(regreso, "GET", "/api/v1/admin/businesses")).Body, Json)!;
        var perfil = await Fetch(regreso, "PUT", $"/api/v1/admin/businesses/{negocio.Id}/profile",
            new SaveBusinessProfileRequest
            {
                Name = negocio.Name, Slug = negocio.Slug,
                MunicipalityId = catalogo.Municipalities[0].Id, CategoryId = catalogo.Categories[0].Id,
                ShortDescription = "Panadería de barrio con producto del día.",
                Description = "Panadería de barrio con producto del día y encargos por anticipado.",
                Address = "Calle 50 # 20-15", PublicPhone = "3005557788", Version = negocio.Version
            });
        Assert.Equal(200, perfil.Status);
        foreach (var tipo in new[] { "Logo", "Cover" })
            Assert.Equal(201, await UploadImage(regreso, negocio.Id, tipo));

        // --- 7. Vista previa antes de enviar ------------------------------------------------
        await regreso.ReloadAsync();
        await regreso.Locator("[data-testid=ver-vista-previa]").ClickAsync();
        await regreso.WaitForURLAsync(url => url.Contains("/vista-previa"));
        await Expect(regreso.GetByText(nombre).First).ToBeVisibleAsync();

        // --- 8. Enviar a revisión desde la interfaz ----------------------------------------
        await regreso.GoBackAsync();
        await regreso.ReloadAsync();
        var enviar = regreso.Locator("[data-testid=enviar-revision]");
        await Expect(enviar).ToBeEnabledAsync();
        await enviar.ClickAsync();
        await Expect(regreso.Locator("[data-testid=ficha-estado]")).ToHaveTextAsync("En revisión");

        // El panel refleja el nuevo estado y cambia la acción que le toca a la socia.
        await regreso.GotoAsync($"{fixture.BaseUrl}/panel");
        var enviada = regreso.Locator("[data-testid=negocio-administrado]").Filter(new() { HasText = nombre });
        await Expect(enviada.Locator("[data-testid=negocio-estado]")).ToHaveTextAsync("En revisión");
        await Expect(enviada.Locator("[data-testid=negocio-cta]")).ToHaveTextAsync("Ver solicitud");

        // --- 9. El recorrido también cabe en un teléfono ------------------------------------
        await regreso.SetViewportSizeAsync(375, 812);
        await regreso.ReloadAsync();
        Assert.False(await regreso.EvaluateAsync<bool>(
            "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"));
        await Expect(regreso.Locator("[data-testid=crear-negocio]")).ToBeVisibleAsync();
    }

    private async Task<PlatformBusinessDto> Load(IPage page, string businessId)
        => JsonSerializer.Deserialize<PlatformBusinessDto>(
            (await Fetch(page, "GET", $"/api/v1/admin/businesses/{businessId}")).Body, Json)!;

    private async Task Login(IPage page, string email, string password)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel") || url.Contains("/Account/ChangeTemporaryPassword"));
    }

    /// <summary>Sube un PNG de 1x1 real como multipart, tal como lo hace el formulario del navegador.</summary>
    private static async Task<int> UploadImage(IPage page, Guid businessId, string kind)
        => await page.EvaluateAsync<int>(
            """
            async ({ businessId, kind }) => {
              const base64 = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==';
              const binary = atob(base64);
              const bytes = new Uint8Array(binary.length);
              for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
              const form = new FormData();
              form.append('file', new Blob([bytes], { type: 'image/png' }), 'imagen.png');
              form.append('kind', kind);
              form.append('altText', 'Imagen de la prueba');
              const response = await fetch(`/api/v1/admin/businesses/${businessId}/images`,
                { method: 'POST', credentials: 'same-origin', body: form });
              return response.status;
            }
            """, new { businessId = businessId.ToString(), kind });

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

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..18];
    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);

    private sealed class FetchResult
    {
        public int Status { get; set; }
        public string Body { get; set; } = "null";
    }
}
