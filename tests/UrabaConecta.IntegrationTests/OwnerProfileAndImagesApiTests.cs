using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// P0-2 de la auditoría V6: el propietario podía operar su negocio pero no administrarlo. El perfil
/// comercial y las imágenes sólo existían bajo <c>/api/v1/admin</c>, reservado a la plataforma.
///
/// Estas pruebas fijan la superficie del propietario y, sobre todo, su frontera: reutiliza los mismos
/// casos de uso que la administración, así que lo único que separa a un propietario de los datos de
/// otro es la autorización. Si esa frontera se rompiera, se rompería en silencio.
/// </summary>
public sealed class OwnerProfileAndImagesApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid Bella = DevelopmentSeeder.BellaBusinessId;

    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    private async Task<HttpClient> LoggedInAs(string email)
    {
        var client = Client();
        await PlatformAdministrationApiTests.Login(client, email);
        return client;
    }

    // ------------------------------------------------------------------ acceso permitido

    [Fact]
    public async Task The_owner_reads_and_saves_the_commercial_profile_of_her_own_business()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        var before = await owner.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/businesses/{Bella}/profile", Json);
        Assert.NotNull(before);

        var marca = $"Salón de barrio {Guid.NewGuid():N}"[..40];
        var response = await owner.PutAsJsonAsync($"/api/v1/businesses/{Bella}/profile",
            new SaveOwnerProfileRequest
            {
                ShortDescription = marca,
                Description = before!.Description,
                Address = before.Address,
                PublicPhone = before.PublicPhone,
                Version = before.Version
            }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Se relee del servidor: interesa que quedara guardado, no lo que devolvió el PUT.
        var after = await owner.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/businesses/{Bella}/profile", Json);
        Assert.Equal(marca, after!.ShortDescription);
    }

    [Fact]
    public async Task The_owner_cannot_rename_or_move_her_business_even_by_hand()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        var before = await owner.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/businesses/{Bella}/profile", Json);

        // El contrato del propietario ni siquiera admite estos campos; se comprueba que el servidor
        // los repone desde lo guardado y no quedan a merced de un cliente modificado.
        var response = await owner.PutAsJsonAsync($"/api/v1/businesses/{Bella}/profile",
            new Dictionary<string, object?>
            {
                ["shortDescription"] = "Descripción breve legítima del negocio.",
                ["description"] = before!.Description,
                ["name"] = "Nombre secuestrado",
                ["slug"] = "nombre-secuestrado",
                ["municipalityId"] = Guid.NewGuid(),
                ["categoryId"] = Guid.NewGuid(),
                ["version"] = before.Version
            }, Json);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await owner.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/businesses/{Bella}/profile", Json);
        Assert.Equal(before.Name, after!.Name);
        Assert.Equal(before.Slug, after.Slug);
        Assert.Equal(before.MunicipalityId, after.MunicipalityId);
        Assert.Equal(before.CategoryId, after.CategoryId);
    }

    [Fact]
    public async Task The_owner_lists_and_uploads_the_images_of_her_own_business()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        Assert.Equal(HttpStatusCode.OK,
            (await owner.GetAsync($"/api/v1/businesses/{Bella}/images")).StatusCode);

        foreach (var kind in new[] { "Logo", "Cover", "Gallery" })
            Assert.Equal(HttpStatusCode.Created,
                (await UploadAsync(owner, Bella, kind, "foto.png", "image/png",
                    PlatformAdministrationApiTests.TinyPng())).StatusCode);

        var images = await owner.GetFromJsonAsync<List<BusinessImageDto>>(
            $"/api/v1/businesses/{Bella}/images", Json);
        Assert.Contains(images!, x => x.Kind == "Logo");
        Assert.Contains(images!, x => x.Kind == "Cover");
        Assert.Contains(images!, x => x.Kind == "Gallery");
    }

    [Fact]
    public async Task Owner_service_screen_contains_preview_replace_and_remove_photo_controls()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        var html = await owner.GetStringAsync($"/panel/{Bella}/configuracion/servicios");
        Assert.Contains("data-testid=\"catalog-image-editor\"", html);
        Assert.Contains("data-testid=\"catalog-image-file\"", html);
        Assert.Contains("Foto de", html);
    }

    [Fact]
    public async Task Owner_product_screen_contains_catalog_photo_controls()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.SazonOwnerEmail);
        var html = await owner.GetStringAsync(
            $"/panel/{DevelopmentSeeder.SazonBusinessId}/configuracion/pedidos");
        Assert.Contains("data-testid=\"catalog-image-editor\"", html);
        Assert.Contains("data-testid=\"catalog-image-file\"", html);
    }

    [Fact]
    public async Task Owner_uploads_a_service_photo_without_mixing_it_with_business_gallery()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        var services = await owner.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{Bella}/services", Json);
        var service = services!.First();
        var response = await UploadAsync(owner, Bella, "Service", "servicio.png", "image/png",
            PlatformAdministrationApiTests.TinyPng(), service.Id);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var images = await owner.GetFromJsonAsync<List<BusinessImageDto>>(
            $"/api/v1/businesses/{Bella}/catalog-images", Json);
        var photo = Assert.Single(images!, x => x.Kind == "Service" && x.ServiceId == service.Id);
        Assert.Null(photo.ProductId);
        Assert.DoesNotContain(photo, images!.Where(x => x.Kind == "Gallery"));
    }

    // ------------------------------------------------------------------ frontera

    [Theory]
    [InlineData("GET", "profile")]
    [InlineData("PUT", "profile")]
    [InlineData("GET", "images")]
    public async Task Another_owner_is_refused_on_a_business_that_is_not_hers(string method, string resource)
    {
        using var intruso = await LoggedInAs(DevelopmentSeeder.OtherOwnerEmail);
        var url = $"/api/v1/businesses/{Bella}/{resource}";
        var response = method == "PUT"
            ? await intruso.PutAsJsonAsync(url, new SaveOwnerProfileRequest
            {
                ShortDescription = "Intento de escritura ajena.", Version = 0
            }, Json)
            : await intruso.GetAsync(url);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Another_owner_cannot_upload_images_to_a_business_that_is_not_hers()
    {
        using var intruso = await LoggedInAs(DevelopmentSeeder.OtherOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await UploadAsync(intruso, Bella, "Logo", "foto.png", "image/png",
                PlatformAdministrationApiTests.TinyPng())).StatusCode);
    }

    [Fact]
    public async Task A_worker_of_the_same_business_does_not_reach_the_profile()
    {
        // Trabaja en Bella y puede configurar servicios, pero el perfil público es del propietario.
        using var trabajadora = await LoggedInAs(DevelopmentSeeder.BellaWorkerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await trabajadora.GetAsync($"/api/v1/businesses/{Bella}/profile")).StatusCode);
    }

    [Fact]
    public async Task An_anonymous_request_is_unauthorized_and_not_forbidden()
    {
        using var anonimo = Client();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonimo.GetAsync($"/api/v1/businesses/{Bella}/profile")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonimo.GetAsync($"/api/v1/businesses/{Bella}/images")).StatusCode);
    }

    [Fact]
    public async Task A_business_that_does_not_exist_is_refused_without_revealing_it()
    {
        // 403 y no 404: para quien no tiene membresía, la diferencia sería un oráculo de existencia.
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await owner.GetAsync($"/api/v1/businesses/{Guid.NewGuid()}/profile")).StatusCode);
    }

    // ------------------------------------------------------------------ validación reutilizada

    [Fact]
    public async Task The_owner_surface_keeps_the_image_validation_of_the_platform()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        var svg = System.Text.Encoding.UTF8.GetBytes(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(owner, Bella, "Logo", "logo.png", "image/png", svg)).StatusCode);

        var ejecutable = new byte[512];
        ejecutable[0] = 0x4D; ejecutable[1] = 0x5A;
        Assert.Equal(HttpStatusCode.BadRequest,
            (await UploadAsync(owner, Bella, "Logo", "logo.jpg", "image/jpeg", ejecutable)).StatusCode);
    }

    [Fact]
    public async Task The_owner_cannot_save_a_profile_without_a_short_description()
    {
        using var owner = await LoggedInAs(DevelopmentSeeder.BellaOwnerEmail);
        var before = await owner.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/businesses/{Bella}/profile", Json);
        var response = await owner.PutAsJsonAsync($"/api/v1/businesses/{Bella}/profile",
            new SaveOwnerProfileRequest { ShortDescription = "   ", Version = before!.Version }, Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid businessId, string kind,
        string fileName, string contentType, byte[] content, Guid? targetId = null)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(content);
        part.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        form.Add(part, "file", fileName);
        form.Add(new StringContent(kind), "kind");
        if (targetId is { } target) form.Add(new StringContent(target.ToString()), "targetId");
        return client.PostAsync($"/api/v1/businesses/{businessId}/images", form);
    }
}
