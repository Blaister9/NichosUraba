using Microsoft.Playwright;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// P0-2 de la auditoría V6: la propietaria podía operar su negocio pero no administrarlo. Para editar
/// su ficha o cambiar su logo tenía que descubrir "Administración" en la barra superior, un menú que
/// habla de revisiones y suspensiones y que no es suyo.
///
/// Este recorrido comprueba lo único que cierra ese criterio: que llega a su perfil y a sus imágenes
/// desde su panel, y que lo que guarda se queda guardado.
/// </summary>
public sealed class OwnerConfigurationJourneyTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    private static readonly Guid Bella = DevelopmentSeeder.BellaBusinessId;

    [Fact]
    public async Task An_owner_edits_her_profile_and_images_without_entering_administration()
    {
        await using var context = await fixture.Browser.NewContextAsync(
            new() { ViewportSize = new() { Width = 1366, Height = 768 } });
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.BellaOwnerEmail);

        // --- 1. Desde el panel, sin pasar por "Administración" -----------------------------
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        await page.Locator($"a[href='/panel/{Bella}/configuracion']").First.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/configuracion"));
        await Assertions.Expect(page.Locator("[data-testid=config-perfil]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=config-imagenes]")).ToBeVisibleAsync();

        await page.Locator("[data-testid=config-perfil]").ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/configuracion/perfil"));

        // --- 2. Sabe de qué negocio es este perfil ------------------------------------------
        await Assertions.Expect(page.Locator("[data-testid=perfil-negocio]")).ToBeVisibleAsync();
        var negocio = await page.Locator("[data-testid=perfil-negocio]").InnerTextAsync();
        Assert.False(string.IsNullOrWhiteSpace(negocio));
        // Nunca el identificador interno ni el nombre técnico del rol.
        var cuerpo = await page.Locator("body").InnerTextAsync();
        Assert.DoesNotContain(Bella.ToString(), cuerpo);
        Assert.DoesNotContain("BusinessOwner", cuerpo);

        // --- 3. Edita y guarda ---------------------------------------------------------------
        var breve = $"Salón de barrio {Guid.NewGuid():N}"[..40];
        var completa = $"Descripción de prueba {Guid.NewGuid():N}";
        var telefono = "3005551234";
        await page.Locator("[data-testid=short-description]").FillAsync(breve);
        await page.Locator("[data-testid=full-description]").FillAsync(completa);
        await page.Locator("[data-testid=phone]").FillAsync(telefono);
        await page.Locator("[data-testid=save-profile]").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid=profile-success]")).ToBeVisibleAsync();

        // --- 4. Sale y vuelve: lo guardado sigue ahí ----------------------------------------
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{Bella}/configuracion/perfil");
        await Assertions.Expect(page.Locator("[data-testid=short-description]")).ToHaveValueAsync(breve);
        await Assertions.Expect(page.Locator("[data-testid=full-description]")).ToHaveValueAsync(completa);
        await Assertions.Expect(page.Locator("[data-testid=phone]")).ToHaveValueAsync(telefono);

        // La identidad del negocio se muestra, no se ofrece como campo editable.
        await Assertions.Expect(page.Locator("[data-testid=perfil-bloqueados]")).ToBeVisibleAsync();

        // --- 5. Imágenes: las tres superficies ----------------------------------------------
        await page.Locator("a[href$='/configuracion/imagenes']").First.ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/configuracion/imagenes"));
        await Assertions.Expect(page.Locator("[data-testid=bloque-logo]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=bloque-portada]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=bloque-galeria]")).ToBeVisibleAsync();

        // --- 6. Sube una imagen real y la ve aparecer ---------------------------------------
        var antes = await page.Locator("[data-testid=galeria] figure").CountAsync();
        await page.Locator("[data-testid=upload-galeria]").SetInputFilesAsync(new FilePayload
        {
            Name = "galeria.png", MimeType = "image/png", Buffer = TinyPng()
        });
        await Assertions.Expect(page.Locator("[data-testid=images-success]")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid=galeria] figure")).ToHaveCountAsync(antes + 1);

        // --- 7. Y vuelve a su panel ----------------------------------------------------------
        await page.GotoAsync($"{fixture.BaseUrl}/panel");
        await Assertions.Expect(page.Locator($"a[href='/panel/{Bella}/configuracion']").First).ToBeVisibleAsync();

        // --- 8. El recorrido cabe en un teléfono ---------------------------------------------
        await page.SetViewportSizeAsync(375, 812);
        foreach (var ruta in new[] { "configuracion", "configuracion/perfil", "configuracion/imagenes" })
        {
            await page.GotoAsync($"{fixture.BaseUrl}/panel/{Bella}/{ruta}");
            Assert.False(await page.EvaluateAsync<bool>(
                "document.documentElement.scrollWidth > document.documentElement.clientWidth + 1"),
                $"La página {ruta} desborda a lo ancho en 375 px.");
        }
        await Assertions.Expect(page.Locator("[data-testid=upload-logo]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task A_worker_is_not_offered_the_owner_sections()
    {
        await using var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await Login(page, DevelopmentSeeder.BellaConfigurationWorkerEmail);
        await page.GotoAsync($"{fixture.BaseUrl}/panel/{Bella}/configuracion");
        // Puede configurar servicios, pero el perfil público y las imágenes son de la propietaria:
        // ofrecerle una puerta que después le cierra la cara sería peor que no ofrecerla.
        await Assertions.Expect(page.Locator("[data-testid=config-perfil]")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-testid=config-imagenes]")).ToHaveCountAsync(0);
    }

    private static byte[] TinyPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private async Task Login(IPage page, string email)
    {
        await page.GotoAsync($"{fixture.BaseUrl}/Account/Login");
        await page.GetByLabel("Correo").FillAsync(email);
        await page.GetByLabel("Contraseña").FillAsync(DevelopmentSeeder.DemoPassword);
        await page.GetByRole(AriaRole.Button, new() { Name = "Ingresar" }).ClickAsync();
        await page.WaitForURLAsync(url => url.Contains("/panel"));
    }
}
