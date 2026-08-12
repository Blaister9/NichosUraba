using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Jornadas partidas de extremo a extremo, contra PostgreSQL real: configuración, disponibilidad
/// de citas, franjas de pedidos, permisos y persistencia.
/// </summary>
public sealed class SplitScheduleApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });

    private static SaveBusinessHourRequest Partida(long version, params (int, int, int, int)[] tramos) => new()
    {
        IsClosed = false, Version = version,
        Intervals = tramos.Select(t => new ScheduleIntervalDto(new TimeOnly(t.Item1, t.Item2),
            new TimeOnly(t.Item3, t.Item4))).ToList(),
    };

    private static async Task<List<BusinessHourAdminDto>> Hours(HttpClient client, Guid businessId)
        => (await client.GetFromJsonAsync<List<BusinessHourAdminDto>>(
            $"/api/v1/businesses/{businessId}/hours", Json))!;

    [Fact]
    public async Task Existing_continuous_hours_survive_as_a_single_interval()
    {
        // Comprueba la conversión de la migración: lo que había sigue ahí, como un tramo.
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var hours = await Hours(client, DevelopmentSeeder.BellaBusinessId);
        var abiertos = hours.Where(x => !x.IsClosed).ToList();
        Assert.NotEmpty(abiertos);
        foreach (var day in abiertos)
        {
            Assert.NotEmpty(day.Schedule);
            Assert.All(day.Schedule, x => Assert.True(x.ClosesAt > x.OpensAt));
            // El primer tramo sigue coincidiendo con el horario continuo anterior.
            Assert.Equal(day.OpensAt, day.Schedule[0].OpensAt);
        }
    }

    [Fact]
    public async Task An_owner_saves_a_split_day_and_it_persists()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var before = (await Hours(client, DevelopmentSeeder.BellaBusinessId))
            .Single(x => x.Day == DayOfWeek.Wednesday);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Wednesday",
            Partida(before.Version, (8, 0, 12, 0), (14, 0, 18, 0)), Json)).StatusCode);

        // Se relee con un cliente nuevo: sale de la base, no de memoria del proceso anterior.
        using var fresh = Client();
        await PlatformAdministrationApiTests.Login(fresh, DevelopmentSeeder.BellaOwnerEmail);
        var after = (await Hours(fresh, DevelopmentSeeder.BellaBusinessId))
            .Single(x => x.Day == DayOfWeek.Wednesday);
        Assert.False(after.IsClosed);
        Assert.Equal(2, after.Schedule.Count);
        Assert.Equal(new TimeOnly(8, 0), after.Schedule[0].OpensAt);
        Assert.Equal(new TimeOnly(12, 0), after.Schedule[0].ClosesAt);
        Assert.Equal(new TimeOnly(14, 0), after.Schedule[1].OpensAt);
        Assert.Equal(new TimeOnly(18, 0), after.Schedule[1].ClosesAt);
    }

    [Fact]
    public async Task Overlapping_intervals_are_rejected_by_the_api()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var day = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == DayOfWeek.Thursday);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Thursday",
            Partida(day.Version, (8, 0, 13, 0), (12, 0, 18, 0)), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("solapar", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Contiguous_intervals_are_accepted_by_the_api()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var day = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == DayOfWeek.Friday);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Friday",
            Partida(day.Version, (8, 0, 14, 0), (14, 0, 18, 0)), Json)).StatusCode);
    }

    [Fact]
    public async Task A_closed_day_drops_every_interval()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var day = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == DayOfWeek.Saturday);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Saturday",
            Partida(day.Version, (8, 0, 12, 0), (14, 0, 18, 0)), Json)).StatusCode);
        var partido = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == DayOfWeek.Saturday);
        Assert.Equal(2, partido.Schedule.Count);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Saturday",
            new SaveBusinessHourRequest { IsClosed = true, Version = partido.Version }, Json)).StatusCode);
        var cerrado = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == DayOfWeek.Saturday);
        Assert.True(cerrado.IsClosed);
        Assert.Empty(cerrado.Schedule);
    }

    [Fact]
    public async Task A_partner_operator_configures_split_hours_from_the_platform()
    {
        using var socia = Client();
        await PlatformAdministrationApiTests.Login(socia, DevelopmentSeeder.PartnerOperatorEmail);
        // Una socia sólo administra los negocios que ella dio de alta, así que se crea uno suyo.
        var catalog = (await socia.GetFromJsonAsync<PlatformBusinessListDto>("/api/v1/admin/businesses", Json))!;
        var created = (await (await socia.PostAsJsonAsync("/api/v1/admin/businesses",
            new CreatePlatformBusinessRequest
            {
                Name = $"Jornada {Guid.NewGuid():N}"[..18], Slug = $"jornada-{Guid.NewGuid():N}",
                MunicipalityId = catalog.Municipalities[0].Id, CategoryId = catalog.Categories[0].Id,
                ShortDescription = "Piloto ficticio de jornadas partidas.",
                Description = "Piloto ficticio para comprobar jornadas partidas.",
                Appointments = true, SaveAsDraft = true,
            }, Json)).Content.ReadFromJsonAsync<PlatformBusinessCreatedDto>(Json))!.Business;

        var hours = (await socia.GetFromJsonAsync<List<BusinessHourAdminDto>>(
            $"/api/v1/admin/businesses/{created.Id}/hours", Json))!;
        var tuesday = hours.Single(x => x.Day == DayOfWeek.Tuesday);
        Assert.Equal(HttpStatusCode.OK, (await socia.PutAsJsonAsync(
            $"/api/v1/admin/businesses/{created.Id}/hours/Tuesday",
            Partida(tuesday.Version, (9, 0, 12, 0), (15, 0, 19, 0)), Json)).StatusCode);
        var after = (await socia.GetFromJsonAsync<List<BusinessHourAdminDto>>(
            $"/api/v1/admin/businesses/{created.Id}/hours", Json))!
            .Single(x => x.Day == DayOfWeek.Tuesday);
        Assert.Equal(2, after.Schedule.Count);
        Assert.Equal(new TimeOnly(9, 0), after.Schedule[0].OpensAt);
        Assert.Equal(new TimeOnly(15, 0), after.Schedule[1].OpensAt);
    }

    [Fact]
    public async Task A_member_without_configuration_permission_is_rejected()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.CorteNoPermissionEmail);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.CorteBusinessId}/hours/Monday",
            Partida(0, (8, 0, 12, 0)), Json);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Hours_of_another_business_are_not_reachable()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.CorteOwnerEmail);
        // Aislamiento: el propietario de El Corte no toca la jornada de Bella.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Monday",
            Partida(0, (8, 0, 12, 0)), Json)).StatusCode);
    }

    [Fact]
    public async Task Appointment_slots_skip_the_pause_and_resume_afterwards()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var hours = await Hours(client, DevelopmentSeeder.BellaBusinessId);
        // Se configura la jornada partida en todos los días laborables para no depender del hoy.
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                     DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            var actual = hours.Single(x => x.Day == day);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
                $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/{day}",
                Partida(actual.Version, (8, 0, 12, 0), (14, 0, 18, 0)), Json)).StatusCode);
        }

        var services = (await client.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", Json))!;
        var service = services.First(x => x.IsActive);

        using var anon = Client();
        var bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        // Un día laborable con margen suficiente para no chocar con "ya pasó".
        var target = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, bogota).DateTime).AddDays(7);
        while (target.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) target = target.AddDays(1);

        var slots = (await anon.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={service.Id}&date={target:yyyy-MM-dd}", Json))!;
        var locales = slots.Slots
            .Select(x => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Start, bogota).DateTime)).ToList();
        Assert.NotEmpty(locales);
        // Ninguna hora cae en la pausa, y la tarde vuelve a ofrecerse.
        Assert.DoesNotContain(locales, x => x >= new TimeOnly(12, 0) && x < new TimeOnly(14, 0));
        Assert.Contains(locales, x => x < new TimeOnly(12, 0));
        Assert.Contains(locales, x => x >= new TimeOnly(14, 0));
        // Y ninguna cita se sale de su tramo.
        Assert.All(locales, x => Assert.True(
            x.AddMinutes(service.DurationMinutes) <= new TimeOnly(12, 0) ||
            (x >= new TimeOnly(14, 0) && x.AddMinutes(service.DurationMinutes) <= new TimeOnly(18, 0))));
    }

    [Fact]
    public async Task Pickup_slots_are_not_generated_during_the_pause()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.SazonOwnerEmail);
        var hours = await Hours(client, DevelopmentSeeder.SazonBusinessId);
        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            var actual = hours.Single(x => x.Day == day);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
                $"/api/v1/businesses/{DevelopmentSeeder.SazonBusinessId}/hours/{day}",
                Partida(actual.Version, (11, 0, 14, 0), (17, 0, 21, 0)), Json)).StatusCode);
        }

        using var anon = Client();
        var bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        var slots = (await anon.GetFromJsonAsync<PickupSlotListDto>(
            "/api/v1/public/businesses/restaurante-sazon-local/pickup-slots", Json))!;
        var locales = slots.Slots
            .Select(x => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Start, bogota).DateTime)).ToList();
        Assert.NotEmpty(locales);
        // Entre 14:00 y 17:00 no debe existir ninguna franja.
        Assert.DoesNotContain(locales, x => x >= new TimeOnly(14, 0) && x < new TimeOnly(17, 0));
        Assert.Contains(locales, x => x >= new TimeOnly(11, 0) && x < new TimeOnly(14, 0));
        Assert.Contains(locales, x => x >= new TimeOnly(17, 0));
    }

    /// <summary>Configura la jornada partida de referencia en un día concreto y devuelve la fecha.</summary>
    private async Task<(DateOnly Date, ServiceDto Service)> SplitDayAsync(HttpClient client)
    {
        var bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        var target = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, bogota).DateTime).AddDays(10);
        while (target.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) target = target.AddDays(1);
        var day = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == target.DayOfWeek);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/{target.DayOfWeek}",
            Partida(day.Version, (8, 0, 12, 0), (14, 0, 18, 0)), Json)).StatusCode);
        var services = (await client.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", Json))!;
        return (target, services.First(x => x.IsActive));
    }

    private async Task<List<TimeOnly>> LocalSlotsAsync(Guid serviceId, DateOnly date)
    {
        using var anon = Client();
        var bogota = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        var slots = (await anon.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={serviceId}&date={date:yyyy-MM-dd}", Json))!;
        return slots.Slots.Select(x => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.Start, bogota).DateTime)).ToList();
    }

    [Fact]
    public async Task A_closed_all_day_exception_wins_over_the_split_schedule()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var (date, service) = await SplitDayAsync(client);
        Assert.NotEmpty(await LocalSlotsAsync(service.Id, date));

        // Se cierra el día para todo el personal: la excepción por fecha manda sobre la semana.
        var staff = (await client.GetFromJsonAsync<List<StaffMemberDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff", Json))!;
        foreach (var person in staff.Where(x => x.IsActive))
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/availability-exceptions",
                new SaveAvailabilityExceptionRequest { StaffMemberId = person.Id, Date = date,
                    Type = "ClosedAllDay", Reason = "Festivo" }, Json)).StatusCode);

        Assert.Empty(await LocalSlotsAsync(service.Id, date));
    }

    [Fact]
    public async Task An_extraordinary_opening_replaces_the_split_schedule_for_that_date()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var (date, service) = await SplitDayAsync(client);
        // El 24 de diciembre del ejemplo: sólo 08:00–12:00 aunque la semana diga otra cosa.
        var staff = (await client.GetFromJsonAsync<List<StaffMemberDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff", Json))!;
        foreach (var person in staff.Where(x => x.IsActive))
            Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(
                $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/availability-exceptions",
                new SaveAvailabilityExceptionRequest { StaffMemberId = person.Id, Date = date,
                    Type = "ExtraordinaryOpening", OpensAt = new(8, 0), ClosesAt = new(12, 0),
                    Reason = "Jornada especial" }, Json)).StatusCode);

        var locales = await LocalSlotsAsync(service.Id, date);
        Assert.NotEmpty(locales);
        // La tarde de la jornada partida deja de ofrecerse porque la excepción la reemplaza.
        Assert.All(locales, x => Assert.True(x.AddMinutes(service.DurationMinutes) <= new TimeOnly(12, 0)));
        Assert.DoesNotContain(locales, x => x >= new TimeOnly(14, 0));
    }

    [Fact]
    public async Task The_public_profile_shows_every_interval_of_a_split_day()
    {
        using var client = Client();
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        var monday = (await Hours(client, DevelopmentSeeder.BellaBusinessId)).Single(x => x.Day == DayOfWeek.Monday);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Monday",
            Partida(monday.Version, (8, 0, 12, 0), (14, 0, 18, 0)), Json)).StatusCode);

        using var anon = Client();
        var profile = (await anon.GetFromJsonAsync<BusinessProfileDto>(
            "/api/v1/public/businesses/salon-bella-uraba", Json))!;
        var lunes = profile.Hours.Where(x => x.Day == DayOfWeek.Monday).OrderBy(x => x.OpensAt).ToList();
        Assert.Equal(2, lunes.Count);
        Assert.Equal("08:00", lunes[0].OpensAt);
        Assert.Equal("12:00", lunes[0].ClosesAt);
        Assert.Equal("14:00", lunes[1].OpensAt);
        Assert.Equal("18:00", lunes[1].ClosesAt);
    }
}
