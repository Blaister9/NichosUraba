using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Cada prueba fija una de las causas medidas de lentitud en la Demo. La base de datos vive en
/// otra región que la aplicación, así que el coste de una petición lo domina el número de idas y
/// vueltas: por eso se afirma el conteo de sentencias y no el tiempo de reloj, que sería inestable.
/// </summary>
public sealed class PerformanceRegressionTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });
    private QueryCounter Counter => factory.Services.GetRequiredService<QueryCounter>();

    [Fact]
    public async Task The_administration_list_does_not_add_queries_per_business()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;

        Counter.Reset();
        var before = await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json);
        var queriesBefore = Counter.Count;

        // Tres negocios más. Con el recorrido anterior cada uno costaba dieciséis sentencias.
        for (var i = 0; i < 3; i++)
        {
            var response = await admin.PostAsJsonAsync("/api/v1/admin/businesses", new CreatePlatformBusinessRequest
            {
                Name = $"Carga {Guid.NewGuid():N}"[..18], Slug = $"carga-{Guid.NewGuid():N}",
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                Description = "Negocio ficticio para medir el coste del listado.",
                Appointments = true, ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = true
            }, Json);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        Counter.Reset();
        var after = await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json);
        var queriesAfter = Counter.Count;

        Assert.Equal(before!.Items.Count + 3, after!.Items.Count);
        // El listado se resuelve con un número fijo de sentencias, no proporcional a los negocios.
        Assert.Equal(queriesBefore, queriesAfter);
        Assert.True(queriesAfter <= 8, $"El listado costó {queriesAfter} sentencias.");
    }

    [Fact]
    public async Task The_administration_list_keeps_reporting_readiness_and_owner()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var list = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var bella = list.Items.Single(x => x.Id == DevelopmentSeeder.BellaBusinessId);

        // La reescritura del listado debe conservar catálogos, propietario, módulos y checklist.
        Assert.False(string.IsNullOrWhiteSpace(bella.Municipality));
        Assert.False(string.IsNullOrWhiteSpace(bella.Category));
        Assert.Equal(DevelopmentSeeder.BellaOwnerEmail, bella.OwnerEmail);
        Assert.Contains("Appointments", bella.Modules);

        var detail = (await admin.GetFromJsonAsync<PlatformBusinessDto>(
            $"/api/v1/admin/businesses/{DevelopmentSeeder.BellaBusinessId}", Json))!;
        Assert.Equal(bella.Municipality, detail.Municipality);
        Assert.Equal(bella.OwnerEmail, detail.OwnerEmail);
        Assert.Equal(bella.IsReady, detail.IsReady);
    }

    [Fact]
    public async Task The_public_profile_is_resolved_in_a_single_query()
    {
        using var client = factory.CreateClient();
        // Se fuerza el fallo de caché con un negocio recién publicado y su propio identificador.
        var slug = await PublishFreshBusinessAsync();

        Counter.Reset();
        var profile = await client.GetFromJsonAsync<BusinessProfileDto>(
            $"/api/v1/public/businesses/{slug}", Json);
        var queries = Counter.Count;

        Assert.NotNull(profile);
        Assert.Equal(slug, profile!.Slug);
        Assert.True(queries <= 2, $"La ficha pública costó {queries} sentencias.");
    }

    [Fact]
    public async Task The_public_profile_is_served_from_cache_and_refreshed_when_it_changes()
    {
        using var client = factory.CreateClient();
        var slug = await PublishFreshBusinessAsync();
        _ = await client.GetFromJsonAsync<BusinessProfileDto>($"/api/v1/public/businesses/{slug}", Json);

        Counter.Reset();
        _ = await client.GetFromJsonAsync<BusinessProfileDto>($"/api/v1/public/businesses/{slug}", Json);
        Assert.Equal(0, Counter.Count);

        // Suspender debe invalidar: el directorio no puede seguir mostrando un negocio retirado.
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var business = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!
            .Items.Single(x => x.Slug == slug);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(
            $"/api/v1/admin/businesses/{business.Id}/suspend",
            new PlatformBusinessStateRequest { Version = business.Version, Reason = "Prueba de invalidación" },
            Json)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/public/businesses/{slug}")).StatusCode);
    }

    [Fact]
    public async Task The_appointment_panel_does_not_add_queries_per_appointment()
    {
        using var owner = Client();
        await PlatformAdministrationApiTests.Login(owner, DevelopmentSeeder.BellaOwnerEmail);
        var route = $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments";
        using var visitor = factory.CreateClient();

        // Ambas mediciones deben tener al menos una cita: con la lista vacía el listado sale
        // antes de leer los consentimientos y el conteo no sería comparable.
        var seeded = 0;
        for (var day = 40; day < 52 && seeded < 1; day++)
            if (await TryCreateAppointmentAsync(visitor, day)) seeded++;
        Assert.True(seeded > 0, "No se pudo crear la cita inicial de prueba.");

        Counter.Reset();
        var before = await owner.GetFromJsonAsync<List<AppointmentAdminDto>>(route, Json);
        var queriesBefore = Counter.Count;

        var created = 0;
        for (var day = 52; day < 70 && created < 2; day++)
            if (await TryCreateAppointmentAsync(visitor, day)) created++;
        Assert.True(created > 0, "No se pudo crear ninguna cita adicional.");

        Counter.Reset();
        var after = await owner.GetFromJsonAsync<List<AppointmentAdminDto>>(route, Json);
        var queriesAfter = Counter.Count;

        Assert.Equal(before!.Count + created, after!.Count);
        // Antes cada cita añadía dos sentencias: negocio y consentimiento, una por fila.
        Assert.Equal(queriesBefore, queriesAfter);
        Assert.True(queriesAfter <= 6, $"El panel de citas costó {queriesAfter} sentencias.");
    }

    [Fact]
    public async Task Dynamic_html_and_json_are_compressed()
    {
        using var client = factory.CreateClient();
        foreach (var route in new[] { "/", "/api/v1/public/businesses" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, route);
            request.Headers.AcceptEncoding.ParseAdd("br, gzip");
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(response.Content.Headers.ContentEncoding,
                x => x is "br" or "gzip");
        }
    }

    [Fact]
    public async Task A_logo_is_stored_small_and_as_webp()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var business = await NewDraftAsync(admin, catalog);

        var wide = SquarePng(1600);
        var logo = (await (await PlatformAdministrationApiTests.UploadImageAsync(admin, business.Id, "Logo",
            "logo.png", "image/png", wide, "Logo de prueba")).Content
            .ReadFromJsonAsync<BusinessImageDto>(Json))!;
        // Un logo se muestra a 64 px en la tarjeta: no se guarda a 1600 px.
        Assert.Equal(320, logo.Width);
        Assert.EndsWith(".webp", logo.Url);

        var gallery = (await (await PlatformAdministrationApiTests.UploadImageAsync(admin, business.Id, "Gallery",
            "foto.png", "image/png", wide, "Galería de prueba")).Content
            .ReadFromJsonAsync<BusinessImageDto>(Json))!;
        // La galería sí se ve en grande y conserva el lado mayor.
        Assert.Equal(1600, gallery.Width);

        var bytes = await admin.GetByteArrayAsync(logo.Url);
        Assert.True(bytes.Length < wide.Length,
            $"El logo normalizado pesa {bytes.Length} y el original {wide.Length}.");
        using var decoded = Image.Load(bytes);
        Assert.Equal(320, decoded.Width);
    }

    // -----------------------------------------------------------------------

    private async Task<PlatformBusinessDto> NewDraftAsync(HttpClient admin, PlatformBusinessListDto catalog)
        => (await (await admin.PostAsJsonAsync("/api/v1/admin/businesses", new CreatePlatformBusinessRequest
        {
            Name = $"Ficha {Guid.NewGuid():N}"[..18], Slug = $"ficha-{Guid.NewGuid():N}",
            MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
            Description = "Negocio ficticio para medir el coste de la ficha pública.",
            Appointments = true, InitialServiceName = "Servicio de prueba",
            ExistingOwnerEmail = DevelopmentSeeder.BellaOwnerEmail, SaveAsDraft = true
        }, Json)).Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

    /// <summary>Crea y publica un negocio para garantizar que la ficha no venga de la caché.</summary>
    private async Task<string> PublishFreshBusinessAsync()
    {
        using var admin = Client();
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var catalog = (await admin.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var draft = await NewDraftAsync(admin, catalog);
        var ready = await PlatformAdministrationApiTests.CompleteChecklistAsync(admin, draft, catalog);
        var activated = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{ready.Id}/activate",
            new PlatformBusinessStateRequest { Version = ready.Version }, Json);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        return ready.Slug;
    }

    private static async Task<bool> TryCreateAppointmentAsync(HttpClient visitor, int daysAhead)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(daysAhead));
        var profile = await visitor.GetFromJsonAsync<BusinessProfileDto>(
            "/api/v1/public/businesses/salon-bella-uraba", Json);
        var serviceId = profile!.Services[0].Id;
        var slots = await visitor.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={serviceId}&date={date:yyyy-MM-dd}",
            Json);
        if (slots!.Slots.Count == 0) return false;
        var legal = await visitor.GetFromJsonAsync<LegalInfoDto>("/api/v1/public/legal", Json);
        var response = await visitor.PostAsJsonAsync(
            "/api/v1/public/businesses/salon-bella-uraba/appointments", new CreateAppointmentRequest
            {
                ServiceId = serviceId, Start = slots.Slots[0].Start, CustomerAlias = "Medición",
                Phone = "3000000000", ConsentAccepted = true,
                ConsentNoticeVersion = legal!.PolicyVersion
            }, Json);
        return response.StatusCode == HttpStatusCode.Created;
    }

    /// <summary>PNG opaco del tamaño pedido, para comprobar el reescalado por uso.</summary>
    private static byte[] SquarePng(int side)
    {
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(side, side);
        image.Mutate(x => x.BackgroundColor(Color.CornflowerBlue));
        using var buffer = new MemoryStream();
        image.SaveAsPng(buffer);
        return buffer.ToArray();
    }
}
