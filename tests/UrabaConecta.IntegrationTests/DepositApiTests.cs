using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Adelantos manuales por WhatsApp de extremo a extremo, contra PostgreSQL real: configuración del
/// servicio, congelado en la cita, estados, permisos, enlace wa.me y persistencia.
/// </summary>
public sealed class DepositApiTests(PostgresWebFactory factory) : IClassFixture<PostgresWebFactory>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private HttpClient Client() => factory.CreateClient(new() { AllowAutoRedirect = false });
    private const string Instrucciones = "Transfiera a la cuenta de ahorros 000-000 y envíe el comprobante.";
    private const string WhatsApp = "573001234567";

    private static CreateServiceRequest NuevoServicio(string nombre, decimal precio, bool adelanto,
        string tipo = "Percentage", decimal valor = 50m, string? instrucciones = Instrucciones,
        string? whatsApp = WhatsApp) => new()
    {
        Name = nombre, DurationMinutes = 60, ReferencePrice = precio, RequiresDeposit = adelanto,
        DepositType = adelanto ? tipo : "None", DepositValue = adelanto ? valor : 0,
        DepositInstructions = instrucciones, DepositWhatsAppNumber = whatsApp
    };

    /// <summary>Servicio con adelanto y una profesional que lo atiende, para poder reservarlo.</summary>
    private async Task<ServiceDto> ServicioReservableAsync(HttpClient owner, CreateServiceRequest request)
    {
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", request, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var servicio = (await response.Content.ReadFromJsonAsync<ServiceDto>(Json))!;
        var staff = await owner.PostAsJsonAsync($"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/staff",
            new SaveStaffMemberRequest { DisplayName = $"Profesional {request.Name}", ServiceIds = [servicio.Id] }, Json);
        Assert.Equal(HttpStatusCode.Created, staff.StatusCode);
        return servicio;
    }

    private static async Task<AppointmentCreatedDto> ReservarAsync(HttpClient anon, Guid serviceId, int dias)
    {
        var fecha = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(dias));
        while (fecha.DayOfWeek == DayOfWeek.Sunday) fecha = fecha.AddDays(1);
        var slots = (await anon.GetFromJsonAsync<SlotListDto>(
            $"/api/v1/public/businesses/salon-bella-uraba/appointment-slots?serviceId={serviceId}&date={fecha:yyyy-MM-dd}",
            Json))!;
        Assert.NotEmpty(slots.Slots);
        var response = await anon.PostAsJsonAsync("/api/v1/public/businesses/salon-bella-uraba/appointments",
            new CreateAppointmentRequest
            {
                ServiceId = serviceId, Start = slots.Slots.First().Start, CustomerAlias = "Ana Adelanto",
                Phone = "3001231234", ConsentAccepted = true, ConsentNoticeVersion = "pilot-1"
            }, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<AppointmentCreatedDto>(Json))!;
    }

    private async Task<HttpClient> OwnerAsync()
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(client, DevelopmentSeeder.BellaOwnerEmail);
        return client;
    }

    private static Task<AppointmentTrackingDto?> SeguimientoAsync(HttpClient anon, string code)
        => anon.GetFromJsonAsync<AppointmentTrackingDto>($"/api/v1/public/appointments/{code}", Json);

    /// <summary>
    /// Se busca por hora y por nombre del servicio: cada prueba usa un servicio propio, y dos
    /// pruebas pueden caer en la misma fecha cuando el desplazamiento aterriza en domingo.
    /// </summary>
    private async Task<AppointmentAdminDto> CitaAdminAsync(HttpClient owner, AppointmentCreatedDto creada)
        => (await owner.GetFromJsonAsync<List<AppointmentAdminDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments", Json))!
            .Single(x => x.Start.ToUniversalTime() == creada.Start.ToUniversalTime()
                && x.ServiceName == creada.ServiceName);

    private static Task<HttpResponseMessage> AccionAsync(HttpClient client, Guid appointmentId, string accion,
        string? motivo = null, Guid? businessId = null)
        => client.PostAsJsonAsync(
            $"/api/v1/businesses/{businessId ?? DevelopmentSeeder.BellaBusinessId}/appointments/{appointmentId}/deposit/{accion}",
            new DepositCommandRequest { Reason = motivo }, Json);

    [Fact]
    public async Task A_service_without_deposit_keeps_working_as_before()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Sin adelanto", 40000, false));
        Assert.False(servicio.RequiresDeposit);
        Assert.Equal(0m, servicio.DepositAmount);

        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 11);
        Assert.Equal("NotRequired", creada.DepositStatus);
        var seguimiento = await SeguimientoAsync(anon, creada.TrackingCode);
        Assert.False(seguimiento!.RequiresDeposit);
        Assert.Null(seguimiento.DepositWhatsAppUrl);

        // Y se confirma sin ninguna verificación previa, como siempre.
        var cita = await CitaAdminAsync(owner, creada);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{cita.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Confirmed" }, Json)).StatusCode);
    }

    [Fact]
    public async Task A_fixed_and_a_percentage_deposit_are_calculated_by_the_server()
    {
        using var owner = await OwnerAsync();
        var porcentual = await ServicioReservableAsync(owner,
            NuevoServicio("Adelanto porcentual", 80000, true, "Percentage", 50));
        var fijo = await ServicioReservableAsync(owner,
            NuevoServicio("Adelanto fijo", 80000, true, "FixedAmount", 30000));
        Assert.Equal(40000m, porcentual.DepositAmount);
        Assert.Equal(30000m, fijo.DepositAmount);

        // La ficha pública muestra el adelanto ya calculado, sin exponer el WhatsApp del negocio.
        using var anon = Client();
        var perfil = (await anon.GetFromJsonAsync<BusinessProfileDto>(
            "/api/v1/public/businesses/salon-bella-uraba", Json))!;
        var publico = perfil.Services.Single(x => x.Id == porcentual.Id);
        Assert.True(publico.RequiresDeposit);
        Assert.Equal(40000m, publico.DepositAmount);
        Assert.Equal(Instrucciones, publico.DepositInstructions);
        Assert.Equal("", publico.DepositWhatsAppNumber);
    }

    [Theory]
    [InlineData("Percentage", 0, Instrucciones, WhatsApp)]
    [InlineData("Percentage", 150, Instrucciones, WhatsApp)]
    [InlineData("FixedAmount", 90000, Instrucciones, WhatsApp)]   // supera el precio de 80.000
    [InlineData("Percentage", 50, Instrucciones, "3001234567")]   // sin código de país
    [InlineData("Percentage", 50, Instrucciones, null)]           // WhatsApp obligatorio
    [InlineData("Percentage", 50, null, WhatsApp)]                // instrucciones obligatorias
    [InlineData("None", 50, Instrucciones, WhatsApp)]             // exige adelanto sin decir de qué tipo
    public async Task Invalid_deposit_configuration_is_rejected(string tipo, decimal valor,
        string? instrucciones, string? whatsApp)
    {
        using var owner = await OwnerAsync();
        var response = await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services",
            NuevoServicio($"Inválido {Guid.NewGuid():N}", 80000, true, tipo, valor, instrucciones, whatsApp), Json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task The_appointment_freezes_the_deposit_and_later_edits_do_not_change_it()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner,
            NuevoServicio("Congelado", 80000, true, "Percentage", 50));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 12);
        Assert.Equal("Pending", creada.DepositStatus);
        Assert.Equal(40000m, creada.DepositAmount);

        // Se encarece el servicio y se cambia el adelanto después de reservar.
        var actual = (await owner.GetFromJsonAsync<List<ServiceDto>>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services", Json))!
            .Single(x => x.Id == servicio.Id);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/services/{servicio.Id}",
            new UpdateServiceRequest
            {
                Name = "Congelado", DurationMinutes = 60, ReferencePrice = 200000, IsActive = true,
                Version = actual.Version, RequiresDeposit = true, DepositType = "FixedAmount",
                DepositValue = 150000, DepositInstructions = "Otra cuenta.", DepositWhatsAppNumber = "573009999999"
            }, Json)).StatusCode);

        var seguimiento = (await SeguimientoAsync(anon, creada.TrackingCode))!;
        Assert.Equal(40000m, seguimiento.DepositAmount);
        Assert.Equal(80000m, seguimiento.ServicePrice);
        Assert.Equal("Percentage", seguimiento.DepositType);
        Assert.Contains("573001234567", seguimiento.DepositWhatsAppUrl);
    }

    [Fact]
    public async Task The_public_tracking_shows_the_deposit_and_a_valid_encoded_whatsapp_link()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Enlace", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 13);
        var seguimiento = (await SeguimientoAsync(anon, creada.TrackingCode))!;

        Assert.True(seguimiento.RequiresDeposit);
        Assert.Equal("Pending", seguimiento.DepositStatus);
        Assert.Equal("Adelanto pendiente", seguimiento.DepositStatusLabel);
        Assert.Equal(40000m, seguimiento.DepositAmount);
        Assert.Equal(Instrucciones, seguimiento.DepositInstructions);
        Assert.True(seguimiento.CanReportDeposit);

        var enlace = seguimiento.DepositWhatsAppUrl!;
        Assert.StartsWith("https://wa.me/573001234567?text=", enlace);
        Assert.DoesNotContain(" ", enlace);
        Assert.DoesNotContain("+", enlace);
        var mensaje = Uri.UnescapeDataString(enlace["https://wa.me/573001234567?text=".Length..]);
        Assert.Contains("Salón Bella Urabá", mensaje);
        Assert.Contains(creada.TrackingCode, mensaje);
        Assert.Contains("Adjunto el comprobante para su verificación.", mensaje);
        // El mensaje no arrastra datos personales del solicitante.
        Assert.DoesNotContain("Ana Adelanto", mensaje);
        Assert.DoesNotContain("3001231234", mensaje);
    }

    [Fact]
    public async Task The_customer_can_report_but_never_verify()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Reporte", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 14);

        var reportado = (await (await anon.PostAsync(
            $"/api/v1/public/appointments/{creada.TrackingCode}/deposit-reported", null))
            .Content.ReadFromJsonAsync<AppointmentTrackingDto>(Json))!;
        Assert.Equal("Reported", reportado.DepositStatus);
        Assert.Equal("Comprobante reportado", reportado.DepositStatusLabel);
        Assert.False(reportado.CanReportDeposit);
        Assert.Null(reportado.DepositWhatsAppUrl);

        // Reportar de nuevo ya no aplica, y no existe ninguna ruta pública para verificar.
        Assert.Equal(HttpStatusCode.Conflict, (await anon.PostAsync(
            $"/api/v1/public/appointments/{creada.TrackingCode}/deposit-reported", null)).StatusCode);
        var verificarPublico = await anon.PostAsync(
            $"/api/v1/public/appointments/{creada.TrackingCode}/deposit-verified", null);
        Assert.True(verificarPublico.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
            $"No debe existir una ruta pública de verificación, y respondió {(int)verificarPublico.StatusCode}.");
        // Y la ruta del negocio exige sesión: el código de seguimiento no sirve ahí.
        var cita = await CitaAdminAsync(owner, creada);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AccionAsync(anon, cita.Id, "verify")).StatusCode);
        Assert.Equal("Reported", (await SeguimientoAsync(anon, creada.TrackingCode))!.DepositStatus);
    }

    [Fact]
    public async Task The_owner_rejects_the_customer_retries_and_then_the_owner_verifies_and_confirms()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Ciclo completo", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 15);
        var cita = await CitaAdminAsync(owner, creada);
        Assert.Equal("Pending", cita.DepositStatus);
        Assert.Equal(40000m, cita.DepositAmount);
        Assert.Equal(80000m, cita.ServicePrice);

        // Sin verificar no se confirma.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{cita.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Confirmed" }, Json)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync(
            $"/api/v1/public/appointments/{creada.TrackingCode}/deposit-reported", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await AccionAsync(owner, cita.Id, "reject", "El comprobante no se lee.")).StatusCode);

        var rechazada = (await SeguimientoAsync(anon, creada.TrackingCode))!;
        Assert.Equal("Rejected", rechazada.DepositStatus);
        Assert.Equal("Comprobante rechazado", rechazada.DepositStatusLabel);
        Assert.Equal("El comprobante no se lee.", rechazada.DepositRejectionReason);
        // El botón de WhatsApp vuelve a aparecer para reintentar.
        Assert.True(rechazada.CanReportDeposit);
        Assert.StartsWith("https://wa.me/", rechazada.DepositWhatsAppUrl);

        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync(
            $"/api/v1/public/appointments/{creada.TrackingCode}/deposit-reported", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccionAsync(owner, cita.Id, "verify")).StatusCode);

        var verificada = await CitaAdminAsync(owner, creada);
        Assert.Equal("Verified", verificada.DepositStatus);
        Assert.Equal("Adelanto verificado", verificada.DepositStatusLabel);
        Assert.NotNull(verificada.DepositReportedAt);
        Assert.NotNull(verificada.DepositVerifiedAt);
        Assert.Equal("Propietaria Bella", verificada.DepositVerifiedBy);

        // Ahora sí se confirma, y el cliente lo ve.
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/appointments/{cita.Id}/status",
            new ChangeAppointmentStatusRequest { TargetStatus = "Confirmed" }, Json)).StatusCode);
        var final = (await SeguimientoAsync(anon, creada.TrackingCode))!;
        Assert.Equal("Confirmed", final.Status);
        Assert.Equal("Verified", final.DepositStatus);
        Assert.False(final.CanReportDeposit);
    }

    [Fact]
    public async Task Invalid_deposit_transitions_and_unknown_actions_are_rejected()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Transiciones", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 16);
        var cita = await CitaAdminAsync(owner, creada);

        Assert.Equal(HttpStatusCode.Conflict, (await AccionAsync(owner, cita.Id, "reopen")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await AccionAsync(owner, cita.Id, "aprobar")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccionAsync(owner, cita.Id, "verify")).StatusCode);
        // Verificado es definitivo para el negocio.
        Assert.Equal(HttpStatusCode.Conflict, (await AccionAsync(owner, cita.Id, "reject")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await AccionAsync(owner, cita.Id, "revert")).StatusCode);

        // Una cita sin adelanto no admite ninguna acción de adelanto.
        var sinAdelanto = await ServicioReservableAsync(owner, NuevoServicio("Transiciones sin", 40000, false));
        var otra = await ReservarAsync(anon, sinAdelanto.Id, 17);
        var citaSinAdelanto = await CitaAdminAsync(owner, otra);
        Assert.Equal(HttpStatusCode.Conflict, (await AccionAsync(owner, citaSinAdelanto.Id, "verify")).StatusCode);
    }

    [Fact]
    public async Task Another_business_and_a_member_without_permission_are_rejected()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Aislamiento", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 18);
        var cita = await CitaAdminAsync(owner, creada);

        using var otroNegocio = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(otroNegocio, DevelopmentSeeder.OtherOwnerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await AccionAsync(otroNegocio, cita.Id, "verify")).StatusCode);
        // Ni siquiera pasando el identificador de su propio negocio alcanza la cita ajena.
        Assert.DoesNotContain(
            (await AccionAsync(otroNegocio, cita.Id, "verify", businessId: DevelopmentSeeder.OtherBusinessId)).StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.Created });

        // Una trabajadora del mismo negocio sin permiso de citas tampoco puede tocar el adelanto.
        var miembros = (await owner.GetFromJsonAsync<BusinessMemberListDto>(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships", Json))!;
        var configuradora = miembros.Items.Single(x => x.Email == DevelopmentSeeder.BellaConfigurationWorkerEmail);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync(
            $"/api/v1/businesses/{DevelopmentSeeder.BellaBusinessId}/memberships/{configuradora.Id}/permissions",
            new UpdateMemberPermissionsRequest
            {
                CanManageAppointments = false, CanManageConfiguration = true, Version = configuradora.Version
            }, Json)).StatusCode);
        using var sinPermiso = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(sinPermiso, DevelopmentSeeder.BellaConfigurationWorkerEmail);
        Assert.Equal(HttpStatusCode.Forbidden, (await AccionAsync(sinPermiso, cita.Id, "verify")).StatusCode);

        Assert.Equal("Pending", (await CitaAdminAsync(owner, creada)).DepositStatus);
    }

    [Fact]
    public async Task The_platform_administration_reads_the_audit_trail()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Auditoría", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 19);
        var cita = await CitaAdminAsync(owner, creada);
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync(
            $"/api/v1/public/appointments/{creada.TrackingCode}/deposit-reported", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await AccionAsync(owner, cita.Id, "verify")).StatusCode);

        using var admin = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(admin, DevelopmentSeeder.PlatformAdminEmail);
        var auditoria = (await admin.GetFromJsonAsync<List<AppointmentDepositAuditDto>>(
            $"/api/v1/admin/appointments/{cita.Id}/deposit-audit", Json))!;
        Assert.Equal(2, auditoria.Count);
        var verificacion = auditoria.Single(x => x.NewStatus == "Verified");
        Assert.Equal("Business", verificacion.ActorKind);
        Assert.NotNull(verificacion.ActorUserId);
        var reporte = auditoria.Single(x => x.NewStatus == "Reported");
        Assert.Equal("Customer", reporte.ActorKind);
        Assert.Null(reporte.ActorUserId);   // el cliente no tiene cuenta

        // Una socia comparte la consola, pero la auditoría del adelanto es de la administración.
        using var socia = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        await PlatformAdministrationApiTests.Login(socia, DevelopmentSeeder.PartnerOperatorEmail);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await socia.GetAsync($"/api/v1/admin/appointments/{cita.Id}/deposit-audit")).StatusCode);
    }

    [Fact]
    public async Task Appointments_created_before_the_migration_stay_not_required()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // La cita histórica del sembrado se creó sin adelanto: la migración la deja en NotRequired.
        var historica = await db.Appointments.AsNoTracking()
            .SingleAsync(x => x.Id == Guid.Parse("81000000-0000-0000-0000-000000000001"));
        Assert.Equal(DepositStatus.NotRequired, historica.DepositStatus);
        Assert.Equal(DepositType.None, historica.DepositType);
        Assert.Equal(0m, historica.DepositAmount);
        Assert.Equal(35000m, historica.DisplayPrice);   // el precio anterior no se tocó
        Assert.False(await db.Appointments.AnyAsync(x => x.DepositStatus == DepositStatus.NotRequired &&
            x.DepositAmount != 0m));
        // Y los servicios sembrados antes siguen sin adelanto.
        Assert.False(await db.Services.AnyAsync(x =>
            x.Id == Guid.Parse("10000000-0000-0000-0000-000000000001") && x.RequiresDeposit));
    }

    [Fact]
    public async Task The_deposit_survives_a_restart_because_it_lives_in_postgresql()
    {
        using var owner = await OwnerAsync();
        var servicio = await ServicioReservableAsync(owner, NuevoServicio("Persistencia", 80000, true));
        using var anon = Client();
        var creada = await ReservarAsync(anon, servicio.Id, 20);
        var cita = await CitaAdminAsync(owner, creada);
        Assert.Equal(HttpStatusCode.OK, (await AccionAsync(owner, cita.Id, "verify")).StatusCode);

        // Un contexto nuevo, sin nada en memoria compartida, lee lo mismo de la base.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var almacenada = await db.Appointments.AsNoTracking().SingleAsync(x => x.Id == cita.Id);
        Assert.Equal(DepositStatus.Verified, almacenada.DepositStatus);
        Assert.Equal(40000m, almacenada.DepositAmount);
        Assert.Equal(DepositType.Percentage, almacenada.DepositType);
        Assert.Equal(50m, almacenada.DepositConfiguredValue);
        Assert.Equal(WhatsApp, almacenada.DepositWhatsAppNumber);
        Assert.NotNull(almacenada.DepositVerifiedByUserId);
        Assert.Equal(1, await db.AppointmentDepositAudits.CountAsync(x => x.AppointmentId == cita.Id));
    }
}
