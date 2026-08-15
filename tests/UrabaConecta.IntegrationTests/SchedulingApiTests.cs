using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

public sealed partial class SchedulingApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly Guid ServiceId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Public_directory_booking_persistence_tracking_and_consent_work()
    {
        using (var scope = factory.Services.CreateScope())
            Assert.Contains("urabaconecta_tests",
                scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString());
        using var client = factory.CreateClient();
        var businesses = await client.GetFromJsonAsync<List<BusinessCardDto>>("/api/v1/public/businesses?q=Bella", Json);
        Assert.Contains(businesses!, x => x.Slug == "salon-bella-uraba");
        Assert.Contains((await client.GetFromJsonAsync<List<BusinessCardDto>>(
            "/api/v1/public/businesses?q=Manicure", Json))!, x => x.Slug == "salon-bella-uraba");
        Assert.Contains((await client.GetFromJsonAsync<List<BusinessCardDto>>(
            "/api/v1/public/businesses?q=Limonada", Json))!, x => x.Slug == "restaurante-sazon-local");
        Assert.Contains((await client.GetFromJsonAsync<List<BusinessCardDto>>(
            "/api/v1/public/businesses?q=Barber%C3%ADa", Json))!, x => x.Slug == "barberia-el-corte");
        Assert.Empty((await client.GetFromJsonAsync<List<BusinessCardDto>>(
            "/api/v1/public/businesses?municipality=turbo", Json))!);

        var created = await CreateAppointment(client, 4);
        Assert.Equal(22, created.TrackingCode.Length);
        var tracking = await client.GetFromJsonAsync<AppointmentTrackingDto>(
            $"/api/v1/public/appointments/{created.TrackingCode}", Json);
        Assert.Equal("Pending", tracking!.Status);
        Assert.EndsWith("1234", tracking.PhoneMasked);
        Assert.DoesNotContain("3001231234", tracking.PhoneMasked);
    }

    [Fact]
    public async Task Invalid_code_and_out_of_hours_are_rejected()
    {
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/api/v1/public/appointments/abcdefghijklmnopqrstuv")).StatusCode);
        var request = NewRequest(new DateTimeOffset(
            NextBusinessDate(8).ToDateTime(new TimeOnly(2, 0)), TimeSpan.FromHours(-5)));
        var response = await client.PostAsJsonAsync("/api/v1/public/businesses/salon-bella-uraba/appointments", request, Json);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_requests_create_only_one_appointment()
    {
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        var date = NextBusinessDate(12);
        var slots = await first.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={ServiceId}&date={date:yyyy-MM-dd}", Json);
        var chosen = slots!.Slots.Last();
        var request1 = NewRequest(chosen.Start, "Concurrencia uno");
        var request2 = NewRequest(chosen.Start, "Concurrencia dos");
        var responses = await Task.WhenAll(
            first.PostAsJsonAsync("/api/v1/public/businesses/salon-bella-uraba/appointments", request1, Json),
            second.PostAsJsonAsync("/api/v1/public/businesses/salon-bella-uraba/appointments", request2, Json));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Real_identity_cookie_enforces_tenant_isolation_and_status_rules()
    {
        using var publicClient = factory.CreateClient();
        var created = await CreateAppointment(publicClient, 20);

        using var bella = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Login(bella, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        Assert.Equal(HttpStatusCode.OK,
            (await bella.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bella.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/appointments")).StatusCode);

        var appointments = await bella.GetFromJsonAsync<AppointmentBoardDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments", Json);
        var appointment = appointments!.Items.Single(x => x.Start.ToUniversalTime() == created.Start.ToUniversalTime()
            && x.Status == "Pending");
        var confirm = await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{appointment.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Confirmed" }, Json);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        var invalid = await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{appointment.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Rejected" }, Json);
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
        var complete = await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{appointment.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Completed" }, Json);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.Equal("Completed", (await publicClient.GetFromJsonAsync<AppointmentTrackingDto>(
            $"/api/v1/public/appointments/{created.TrackingCode}", Json))!.Status);

        var createServiceResponse = await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services",
            new CreateServiceRequest { Name = "Servicio configurable", DurationMinutes = 30, ReferencePrice = 12000 }, Json);
        Assert.Equal(HttpStatusCode.Created, createServiceResponse.StatusCode);
        var configurableService = (await createServiceResponse.Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        var createStaffResponse = await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff",
            new SaveStaffMemberRequest { DisplayName = "Profesional configurable", ServiceIds = [configurableService.Id] }, Json);
        Assert.Equal(HttpStatusCode.Created, createStaffResponse.StatusCode);
        var configurableStaff = (await createStaffResponse.Content.ReadFromJsonAsync<StaffMemberDto>(Json))!;
        Assert.Equal(HttpStatusCode.OK, (await bella.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/hours/Sunday",
            new SaveBusinessHourRequest { OpensAt = new(9, 0), ClosesAt = new(13, 0) }, Json)).StatusCode);
        var exceptionResponse = await bella.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/availability-exceptions",
            new SaveAvailabilityExceptionRequest
            { StaffMemberId = configurableStaff.Id, Date = NextBusinessDate(30), Type = "ClosedAllDay" }, Json);
        Assert.Equal(HttpStatusCode.Created, exceptionResponse.StatusCode);
        var availabilityException = (await exceptionResponse.Content.ReadFromJsonAsync<AvailabilityExceptionDto>(Json))!;
        var blockedSlots = await publicClient.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={configurableService.Id}&date={availabilityException.Date:yyyy-MM-dd}",
            Json);
        Assert.Empty(blockedSlots!.Slots);
        Assert.Equal(HttpStatusCode.NoContent, (await bella.DeleteAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/availability-exceptions/{availabilityException.Id}?version={availabilityException.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bella.DeleteAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{configurableService.Id}")).StatusCode);

        using var other = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Login(other, DevelopmentSeeder.OtherOwnerEmail, DevelopmentSeeder.DemoPassword);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await other.GetAsync($"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments")).StatusCode);
        var update = new UpdateServiceRequest { Name = "Ataque cruzado", DurationMinutes = 60, ReferencePrice = 1, IsActive = true };
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{ServiceId}", update, Json)).StatusCode);

        using var worker = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Login(worker, DevelopmentSeeder.BellaWorkerEmail, DevelopmentSeeder.DemoPassword);
        Assert.Equal(HttpStatusCode.OK, (await worker.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await worker.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.OtherBusinessId}/appointments")).StatusCode);
    }

    [Fact]
    public async Task Visitor_cannot_open_private_api_and_filters_work()
    {
        using var visitor = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await visitor.GetAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments")).StatusCode);

        using var owner = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await Login(owner, DevelopmentSeeder.BellaOwnerEmail, DevelopmentSeeder.DemoPassword);
        var completed = await owner.GetFromJsonAsync<AppointmentBoardDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments?status=Completed", Json);
        Assert.All(completed!.Items, x => Assert.Equal("Completed", x.Status));
    }

    private static async Task<AppointmentCreatedDto> CreateAppointment(HttpClient client, int days)
    {
        var date = NextBusinessDate(days);
        var slots = await client.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={ServiceId}&date={date:yyyy-MM-dd}", Json);
        Assert.NotEmpty(slots!.Slots);
        var response = await client.PostAsJsonAsync("/api/v1/public/businesses/salon-bella-uraba/appointments",
            NewRequest(slots.Slots.First().Start), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AppointmentCreatedDto>(Json))!;
    }

    private static CreateAppointmentRequest NewRequest(DateTimeOffset start, string alias = "Ana Prueba") => new()
    {
        ServiceId = ServiceId, Start = start, CustomerAlias = alias, Phone = "3001231234",
        Notes = "Prueba automatizada", ConsentAccepted = true, ConsentNoticeVersion = "pilot-1"
    };
    private static DateOnly NextBusinessDate(int days)
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(days));
        while (date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(1);
        return date;
    }
    private static async Task Login(HttpClient client, string email, string password)
    {
        var html = await client.GetStringAsync("/Account/Login");
        var token = AntiforgeryRegex().Match(html).Groups[1].Value.Replace("&quot;", "\"").Replace("&amp;", "&");
        Assert.NotEmpty(token);
        var response = await client.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token, ["_handler"] = "login",
            ["Input.Email"] = email, ["Input.Password"] = password,
            ["Input.RememberMe"] = "false"
        }));
        Assert.True(response.StatusCode == HttpStatusCode.Redirect,
            $"Login devolvió {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiforgeryRegex();
}
