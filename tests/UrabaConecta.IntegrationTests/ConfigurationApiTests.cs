using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class ConfigurationApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Owner_controls_service_visibility_and_stale_update_is_rejected()
    {
        using var first = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var second = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Login(first, DevelopmentSeeder.BellaOwnerEmail);
        await Login(second, DevelopmentSeeder.BellaOwnerEmail);

        var name = $"Servicio integración {Guid.NewGuid():N}";
        var create = await first.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services",
            new CreateServiceRequest { Name = name, Description = "Descripción corta", DurationMinutes = 30,
                ReferencePrice = 15000, DisplayOrder = 9 }, Json);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var service = (await create.Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        var list = await first.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", Json);
        Assert.Contains(list!, x => x.Id == service.Id && x.Description == "Descripción corta");

        var update = new UpdateServiceRequest { Name = $"{name} editado", Description = "Nueva",
            DurationMinutes = 45, ReferencePrice = 18000, DisplayOrder = 3, IsActive = true, Version = service.Version };
        Assert.Equal(HttpStatusCode.OK, (await first.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{service.Id}", update, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await second.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{service.Id}", update, Json)).StatusCode);

        var current = (await first.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", Json))!.Single(x => x.Id == service.Id);
        current = (await (await first.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{service.Id}",
            new UpdateServiceRequest { Name = current.Name, Description = current.Description,
                DurationMinutes = current.DurationMinutes, ReferencePrice = current.ReferencePrice,
                DisplayOrder = current.DisplayOrder, IsActive = false, Version = current.Version }, Json))
            .Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        var publicProfile = await first.GetFromJsonAsync<BusinessProfileDto>(
            "/api/v1/public/businesses/salon-bella-uraba", Json);
        Assert.DoesNotContain(publicProfile!.Services, x => x.Id == service.Id);

        Assert.Equal(HttpStatusCode.OK, (await first.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{service.Id}",
            new UpdateServiceRequest { Name = current.Name, Description = current.Description,
                DurationMinutes = current.DurationMinutes, ReferencePrice = current.ReferencePrice,
                DisplayOrder = current.DisplayOrder, IsActive = true, Version = current.Version }, Json)).StatusCode);
        publicProfile = await first.GetFromJsonAsync<BusinessProfileDto>(
            "/api/v1/public/businesses/salon-bella-uraba", Json);
        Assert.Contains(publicProfile!.Services, x => x.Id == service.Id);
    }

    [Fact]
    public async Task Staff_hours_and_interval_exception_persist_and_change_public_slots()
    {
        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var visitor = factory.CreateClient();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);

        var serviceResponse = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services",
            new CreateServiceRequest { Name = $"Agenda {Guid.NewGuid():N}", DurationMinutes = 30,
                ReferencePrice = 10000 }, Json);
        var service = (await serviceResponse.Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        var staffResponse = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff",
            new SaveStaffMemberRequest { DisplayName = $"Profesional {Guid.NewGuid():N}"[..24],
                IsActive = true, ParticipatesInAvailability = true, ServiceIds = [service.Id] }, Json);
        Assert.Equal(HttpStatusCode.Created, staffResponse.StatusCode);
        var staff = (await staffResponse.Content.ReadFromJsonAsync<StaffMemberDto>(Json))!;

        var date = NextMonday(35);
        var hours = await owner.GetFromJsonAsync<List<BusinessHourAdminDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours", Json);
        var monday = hours!.Single(x => x.Day == DayOfWeek.Monday);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Monday",
            new SaveBusinessHourRequest { OpensAt = new(9, 0), ClosesAt = new(12, 0), Version = monday.Version }, Json)).StatusCode);
        var before = await visitor.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={service.Id}&date={date:yyyy-MM-dd}", Json);
        Assert.NotEmpty(before!.Slots);

        var exceptionResponse = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/availability-exceptions",
            new SaveAvailabilityExceptionRequest { StaffMemberId = staff.Id, Date = date, Type = "ClosedInterval",
                OpensAt = new(10, 0), ClosesAt = new(11, 0), Reason = "Capacitación interna" }, Json);
        Assert.Equal(HttpStatusCode.Created, exceptionResponse.StatusCode);
        var exception = (await exceptionResponse.Content.ReadFromJsonAsync<AvailabilityExceptionDto>(Json))!;
        Assert.Equal("Capacitación interna", exception.Reason);

        var after = await visitor.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={service.Id}&date={date:yyyy-MM-dd}", Json);
        Assert.NotEmpty(after!.Slots);
        Assert.True(after.Slots.Count < before.Slots.Count);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Bogota");
        Assert.DoesNotContain(after.Slots, slot =>
        {
            var localStart = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(slot.Start, zone).DateTime);
            var localEnd = TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(slot.End, zone).DateTime);
            return localStart < new TimeOnly(11, 0) && localEnd > new TimeOnly(10, 0);
        });
    }

    /// <summary>
    /// El fallo que dejó a Studio Laura sin horas: un servicio activo, con horario comercial y con
    /// una duración que cabe en la jornada, no ofrece ni una hora mientras nadie lo preste. El
    /// motor lo resuelve en silencio devolviendo una lista vacía, así que la única señal es esta.
    /// </summary>
    [Fact]
    public async Task An_active_service_that_nobody_provides_offers_no_hours_until_staff_is_assigned()
    {
        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var visitor = factory.CreateClient();
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail);

        var service = (await (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services",
            new CreateServiceRequest { Name = $"Sin personal {Guid.NewGuid():N}"[..24], DurationMinutes = 60,
                ReferencePrice = 60000 }, Json)).Content.ReadFromJsonAsync<ServiceDto>(Json))!;

        var date = NextMonday(21);
        var slotsUrl = "/api/v1/public/businesses/salon-bella-uraba/appointment-slots" +
                       $"?serviceId={service.Id}&date={date:yyyy-MM-dd}";
        var sinPersonal = await visitor.GetFromJsonAsync<SlotListDto>(slotsUrl, Json);
        Assert.Empty(sinPersonal!.Slots);
        // El día está abierto: lo que falta no es el horario.
        var hours = await owner.GetFromJsonAsync<List<BusinessHourAdminDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours", Json);
        Assert.Contains(hours!, x => x.Day == DayOfWeek.Monday && x.OpensAt < x.ClosesAt);

        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff",
            new SaveStaffMemberRequest { DisplayName = $"Quien lo presta {Guid.NewGuid():N}"[..24],
                IsActive = true, ParticipatesInAvailability = true, ServiceIds = [service.Id] }, Json)).StatusCode);

        var conPersonal = await visitor.GetFromJsonAsync<SlotListDto>(slotsUrl, Json);
        Assert.NotEmpty(conPersonal!.Slots);
        Assert.Equal("America/Bogota", conPersonal.BusinessTimeZone);
        // La duración del servicio manda: cada hora ofrecida deja sitio para los sesenta minutos.
        Assert.All(conPersonal.Slots, slot => Assert.Equal(60, (slot.End - slot.Start).TotalMinutes));
    }

    [Fact]
    public async Task Configuration_authorization_matrix_blocks_cross_business_and_unprivileged_users()
    {
        using var visitor = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);

        using var bella = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var other = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var worker = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        using var authorizedWorker = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Login(bella, DevelopmentSeeder.BellaOwnerEmail);
        await Login(other, DevelopmentSeeder.OtherOwnerEmail);
        await Login(worker, DevelopmentSeeder.BellaWorkerEmail);
        await Login(authorizedWorker, DevelopmentSeeder.BellaConfigurationWorkerEmail);

        Assert.Equal(HttpStatusCode.OK, (await bella.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await worker.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await authorizedWorker.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services")).StatusCode);

        var bellaService = (await bella.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", Json))!.First();
        var mutation = new UpdateServiceRequest { Name = "Cruce bloqueado", DurationMinutes = 30,
            ReferencePrice = 1, IsActive = true, Version = bellaService.Version };
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{bellaService.Id}", mutation, Json)).StatusCode);

        var otherServiceResponse = await other.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/services",
            new CreateServiceRequest { Name = "Servicio otro", DurationMinutes = 30, ReferencePrice = 1 }, Json);
        var otherService = (await otherServiceResponse.Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        Assert.Equal(HttpStatusCode.Conflict, (await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff",
            new SaveStaffMemberRequest { DisplayName = "Cruce", ServiceIds = [otherService.Id] }, Json)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Tuesday",
            new SaveBusinessHourRequest { OpensAt = new(9, 0), ClosesAt = new(12, 0) }, Json)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/availability-exceptions",
            new SaveAvailabilityExceptionRequest { StaffMemberId = Guid.NewGuid(), Date = NextMonday(20),
                Type = "ClosedAllDay" }, Json)).StatusCode);
    }

    private static DateOnly NextMonday(int minimumDays)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(minimumDays));
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date;
    }

    private static async Task Login(HttpClient client, string email)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = DevelopmentSeeder.DemoPassword,
            ["Input.RememberMe"] = "false"
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
