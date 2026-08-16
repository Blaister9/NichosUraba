using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// La búsqueda del próximo día con horarios. La Home la usa en lugar de encadenar una consulta por
/// jornada, así que lo que hay que fijar es doble: que responda exactamente lo mismo que responder
/// día por día, y que cueste una sola tanda de lecturas en vez de una por día.
/// </summary>
public sealed class NextAvailabilityApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private const string Slug = "salon-bella-uraba";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private QueryCounter Counter => factory.Services.GetRequiredService<QueryCounter>();

    [Fact]
    public async Task It_answers_the_same_day_that_asking_one_day_at_a_time_would_find()
    {
        using var client = factory.CreateClient();
        var serviceId = await ServiceId(client);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        // El recorrido que hacía la Home antes: preguntar jornada por jornada y quedarse con la
        // primera que tenga hueco.
        (DateOnly Date, int Count)? esperado = null;
        for (var offset = 0; offset < 4 && esperado is null; offset++)
        {
            var dia = hoy.AddDays(offset);
            var slots = await client.GetFromJsonAsync<SlotListDto>(
                $"/api/v1/public/businesses/{Slug}/appointment-slots?serviceId={serviceId}&date={dia:yyyy-MM-dd}", Json);
            if (slots!.Slots.Count > 0) esperado = (dia, slots.Slots.Count);
        }

        var response = await client.GetAsync(
            $"/api/v1/public/businesses/{Slug}/next-availability?serviceId={serviceId}&from={hoy:yyyy-MM-dd}&days=4");

        if (esperado is null)
        {
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var encontrado = (await response.Content.ReadFromJsonAsync<SlotListDto>(Json))!;
        Assert.Equal(esperado.Value.Date, encontrado.Date);
        Assert.Equal(esperado.Value.Count, encontrado.Slots.Count);
    }

    [Fact]
    public async Task The_slots_it_returns_are_the_same_ones_that_day_reports()
    {
        using var client = factory.CreateClient();
        var serviceId = await ServiceId(client);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.GetAsync(
            $"/api/v1/public/businesses/{Slug}/next-availability?serviceId={serviceId}&from={hoy:yyyy-MM-dd}&days=7");
        if (response.StatusCode == HttpStatusCode.NoContent) return;
        var encontrado = (await response.Content.ReadFromJsonAsync<SlotListDto>(Json))!;

        var delDia = await client.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/{Slug}/appointment-slots?serviceId={serviceId}&date={encontrado.Date:yyyy-MM-dd}", Json);

        // No basta con que coincida el número: tienen que ser las mismas horas, porque las dos
        // rutas comparten las reglas y una divergencia sería una regla duplicada.
        Assert.Equal(delDia!.Slots.Select(x => x.Start), encontrado.Slots.Select(x => x.Start));
        Assert.Equal(delDia.BusinessTimeZone, encontrado.BusinessTimeZone);
    }

    [Fact]
    public async Task Looking_at_four_days_costs_the_same_as_looking_at_one()
    {
        using var client = factory.CreateClient();
        var serviceId = await ServiceId(client);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        Counter.Reset();
        _ = await client.GetAsync(
            $"/api/v1/public/businesses/{Slug}/next-availability?serviceId={serviceId}&from={hoy:yyyy-MM-dd}&days=1");
        var unDia = Counter.Count;

        Counter.Reset();
        _ = await client.GetAsync(
            $"/api/v1/public/businesses/{Slug}/next-availability?serviceId={serviceId}&from={hoy:yyyy-MM-dd}&days=4");
        var cuatroDias = Counter.Count;

        // Antes cada jornada costaba su propia tanda de siete lecturas.
        Assert.Equal(unDia, cuatroDias);
        Assert.True(cuatroDias <= 8, $"Cuatro jornadas costaron {cuatroDias} sentencias.");
    }

    [Fact]
    public async Task An_occupied_appointment_still_removes_its_hour_from_the_answer()
    {
        using var client = factory.CreateClient();
        var serviceId = await ServiceId(client);
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        var antes = await Next(client, serviceId, hoy, 7);
        Assert.NotNull(antes);
        var reservada = antes!.Slots[0].Start;

        var legal = await client.GetFromJsonAsync<LegalInfoDto>("/api/v1/public/legal", Json);
        var creada = await client.PostAsJsonAsync($"/api/v1/public/businesses/{Slug}/appointments",
            new CreateAppointmentRequest
            {
                ServiceId = serviceId, Start = reservada, CustomerAlias = "Ocupada",
                Phone = "3000000000", ConsentAccepted = true, ConsentNoticeVersion = legal!.PolicyVersion
            }, Json);
        Assert.Equal(HttpStatusCode.Created, creada.StatusCode);

        var despues = await Next(client, serviceId, hoy, 7);
        Assert.NotNull(despues);
        // La hora reservada desaparece: el contexto por rango lee las citas igual que el de un día.
        Assert.DoesNotContain(despues!.Slots, x => x.Start == reservada);
    }

    [Fact]
    public async Task A_range_beyond_sixty_days_is_rejected_like_a_single_date_is()
    {
        using var client = factory.CreateClient();
        var serviceId = await ServiceId(client);
        var lejos = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(70));

        var response = await client.GetAsync(
            $"/api/v1/public/businesses/{Slug}/next-availability?serviceId={serviceId}&from={lejos:yyyy-MM-dd}&days=4");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unknown_business_reports_no_availability_instead_of_failing()
    {
        using var client = factory.CreateClient();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        var response = await client.GetAsync(
            $"/api/v1/public/businesses/negocio-que-no-existe/next-availability?serviceId={Guid.NewGuid()}&from={hoy:yyyy-MM-dd}&days=4");

        // La Home trata esto como "sin horarios" y sigue componiendo el feed; un 500 la tumbaría.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<SlotListDto?> Next(HttpClient client, Guid serviceId, DateOnly from, int days)
    {
        var response = await client.GetAsync(
            $"/api/v1/public/businesses/{Slug}/next-availability?serviceId={serviceId}&from={from:yyyy-MM-dd}&days={days}");
        return response.StatusCode == HttpStatusCode.NoContent
            ? null : await response.Content.ReadFromJsonAsync<SlotListDto>(Json);
    }

    private static async Task<Guid> ServiceId(HttpClient client)
        => (await client.GetFromJsonAsync<BusinessProfileDto>($"/api/v1/public/businesses/{Slug}", Json))!
            .Services[0].Id;
}
