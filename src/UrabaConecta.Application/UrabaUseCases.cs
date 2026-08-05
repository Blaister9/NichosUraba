using System.Globalization;
using System.Text.RegularExpressions;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed partial class UrabaUseCases(IUrabaStore store, IMembershipAdministrationStore membershipStore,
    IIdentityAccountManager identityAccounts, IPublicCodeService codes,
    IPersonalDataProtector protector, IConsentPolicyProvider consentPolicy,
    TimeProvider timeProvider) : IUrabaUseCases
{
    public Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search, string? municipality,
        string? category, CancellationToken cancellationToken = default)
        => store.FindBusinessesAsync(search?.Trim(), municipality, category, cancellationToken);

    public Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default)
        => store.GetBusinessProfileAsync(slug, requirePublished: true, cancellationToken);

    public async Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var context = await store.GetSchedulingContextAsync(slug, serviceId, date, cancellationToken)
            ?? throw new ApiException("BUSINESS_OR_SERVICE_NOT_FOUND", "No encontramos el establecimiento o servicio.", 404);
        EnsureServiceActive(context.Service);
        if (date > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(60))
            throw new ApiException("DATE_OUT_OF_RANGE", "Solo puede consultar los próximos 60 días.");

        // Un día puede tener varios tramos: sin filas está cerrado, con varias es jornada partida.
        var dayIntervals = context.Hours.Where(x => x.Day == date.DayOfWeek)
            .Select(x => new ScheduleInterval(x.OpensAt, x.ClosesAt)).ToList();
        if (dayIntervals.Count == 0 || context.EligibleStaff.Count == 0)
            return new(context.Business.TimeZoneId, date, []);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(context.Business.TimeZoneId);
        var all = context.EligibleStaff
            .SelectMany(staff =>
            {
                var exception = context.Exceptions.FirstOrDefault(x => x.StaffMemberId == staff.Id && x.Date == date);
                if (exception?.Type == AvailabilityExceptionType.ClosedAllDay) return [];
                // Una apertura extraordinaria reemplaza la jornada de esa fecha por su propio tramo.
                var extraordinary = exception?.Type == AvailabilityExceptionType.ExtraordinaryOpening;
                var intervals = extraordinary
                    ? [new ScheduleInterval(exception!.OpensAt!.Value, exception.ClosesAt!.Value)]
                    : dayIntervals;
                var occupied = context.Occupied.Where(x => x.StaffId == staff.Id).Select(x => (x.Start, x.End)).ToList();
                if (exception?.Type == AvailabilityExceptionType.ClosedInterval)
                {
                    var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                        date.ToDateTime(exception.OpensAt!.Value), zone), TimeSpan.Zero);
                    var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                        date.ToDateTime(exception.ClosesAt!.Value), zone), TimeSpan.Zero);
                    occupied.Add((startUtc, endUtc));
                }
                return AppointmentSlotCalculator.Calculate(date, intervals,
                    context.Service.DurationMinutes, zone, timeProvider.GetUtcNow(),
                    occupied)
                    .Select(x => new SlotDto(x.Start, x.End));
            })
            .GroupBy(x => x.Start).Select(x => x.First()).OrderBy(x => x.Start).ToArray();
        return new(context.Business.TimeZoneId, date, all);
    }

    public async Task<AppointmentCreatedDto> CreateAppointmentAsync(string slug, CreateAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateContact(request);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(request.Start,
            TimeZoneInfo.FindSystemTimeZoneById("America/Bogota")).DateTime);
        var context = await store.GetSchedulingContextAsync(slug, request.ServiceId, localDate, cancellationToken)
            ?? throw new ApiException("BUSINESS_OR_SERVICE_NOT_FOUND", "No encontramos el establecimiento o servicio.", 404);
        EnsureServiceActive(context.Service);

        var slots = await GetSlotsAsync(slug, request.ServiceId, localDate, cancellationToken);
        var chosen = slots.Slots.FirstOrDefault(x => x.Start.ToUniversalTime() == request.Start.ToUniversalTime());
        if (chosen is null) throw new ApiException("SLOT_UNAVAILABLE", "Ese horario acaba de ocuparse. Elija otro.", 409);

        var staffId = context.EligibleStaff
            .Where(staff => !context.Occupied.Any(x => x.StaffId == staff.Id && x.Start < chosen.End && x.End > chosen.Start))
            .OrderBy(x => x.Id).Select(x => x.Id).FirstOrDefault();
        if (staffId == Guid.Empty) throw new ApiException("SLOT_UNAVAILABLE", "Ese horario acaba de ocuparse. Elija otro.", 409);

        var now = timeProvider.GetUtcNow();
        var code = codes.Generate();
        var consent = new ConsentReceipt(Guid.NewGuid(), context.Business.Id, request.ConsentNoticeVersion,
            "Gestionar la solicitud de cita y contactar al solicitante.", now);
        var digits = PhoneDigits().Replace(request.Phone, "");
        // El adelanto se congela aquí: la cita guarda lo pactado hoy, no lo que diga el servicio mañana.
        var appointment = new Appointment(Guid.NewGuid(), context.Business.Id, context.Service.Id, staffId,
            request.Start, context.Service.DurationMinutes, context.Service.Name, context.Service.ReferencePrice,
            protector.Protect(request.CustomerAlias.Trim()), protector.Protect(digits), digits[^4..],
            protector.Protect(request.Notes?.Trim() ?? ""), code.Hash, code.Version, consent.Id, now,
            context.Service.Deposit);
        consent.LinkAppointment(appointment.Id);

        if (!await store.AddAppointmentAsync(appointment, consent, cancellationToken))
            throw new ApiException("SLOT_UNAVAILABLE", "Ese horario acaba de ocuparse. Elija otro.", 409);
        return new(code.PlainText, appointment.Status.ToString(), appointment.ServiceName, appointment.StartAtUtc,
            appointment.DepositStatus.ToString(), appointment.DepositAmount);
    }

    public async Task<AppointmentTrackingDto?> GetTrackingAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 20 or > 128) return null;
        var record = await store.FindAppointmentByCodeHashAsync(codes.Hash(code), cancellationToken);
        return record is null ? null : ToTracking(record, code);
    }

    public async Task<AppointmentTrackingDto> ReportDepositAsync(string code,
        CancellationToken cancellationToken = default)
    {
        var record = await store.FindAppointmentByCodeHashAsync(codes.Hash(code), cancellationToken)
            ?? throw new ApiException("APPOINTMENT_NOT_FOUND", "No encontramos la cita.", 404);
        var previous = record.Appointment.DepositStatus;
        // El cliente sólo puede llegar hasta "reportado": verificar es del negocio.
        TryDomain(() => record.Appointment.ReportDeposit(timeProvider.GetUtcNow()));
        Audit(record.Appointment, DepositActorKind.Customer, null, previous);
        await store.SaveChangesAsync(cancellationToken);
        return ToTracking(record, code);
    }

    public async Task<AppointmentAdminDto> ChangeDepositAsync(Guid userId, Guid businessId, Guid appointmentId,
        string action, DepositCommandRequest request, bool isPlatformAdmin = false,
        CancellationToken cancellationToken = default)
    {
        await DemandAppointmentAccess(userId, businessId, cancellationToken);
        var record = await store.GetAppointmentAsync(businessId, appointmentId, cancellationToken)
            ?? throw new ApiException("APPOINTMENT_NOT_FOUND", "No encontramos la cita.", 404);
        var appointment = record.Appointment;
        var previous = appointment.DepositStatus;
        var now = timeProvider.GetUtcNow();
        switch (action.ToLowerInvariant())
        {
            case "report": TryDomain(() => appointment.ReportDeposit(now)); break;
            case "verify": TryDomain(() => appointment.VerifyDeposit(userId, now)); break;
            case "reject": TryDomain(() => appointment.RejectDeposit(now, request.Reason)); break;
            case "reopen": TryDomain(() => appointment.ReopenDeposit(now)); break;
            case "revert":
                // Deshacer una verificación no es una corrección cotidiana del negocio.
                if (!isPlatformAdmin)
                    throw new ApiException("DEPOSIT_REVERT_FORBIDDEN",
                        "Sólo la administración de la plataforma puede deshacer una verificación.", 403);
                TryDomain(() => appointment.RevertDepositVerification(now));
                break;
            default: throw new ApiException("INVALID_DEPOSIT_ACTION", "La acción solicitada no existe.");
        }
        Audit(appointment, isPlatformAdmin ? DepositActorKind.PlatformAdmin : DepositActorKind.Business,
            userId, previous, request.Reason);
        await store.SaveChangesAsync(cancellationToken);
        return ToAdmin(record);
    }

    public Task<IReadOnlyList<AppointmentDepositAuditDto>> GetDepositAuditAsync(Guid appointmentId,
        CancellationToken cancellationToken = default)
        => store.ListDepositAuditAsync(appointmentId, cancellationToken);

    private void Audit(Appointment appointment, DepositActorKind actorKind, Guid? actorUserId,
        DepositStatus previous, string? reason = null)
        => store.AddDepositAudit(new(Guid.NewGuid(), appointment.BusinessId, appointment.Id, actorKind,
            actorUserId, previous, appointment.DepositStatus, timeProvider.GetUtcNow(), reason));

    public async Task CancelAsync(string code, CancellationToken cancellationToken = default)
    {
        var record = await store.FindAppointmentByCodeHashAsync(codes.Hash(code), cancellationToken)
            ?? throw new ApiException("APPOINTMENT_NOT_FOUND", "No encontramos la cita.", 404);
        TryDomain(() => record.Appointment.ChangeStatus(AppointmentStatus.Cancelled, timeProvider.GetUtcNow()));
        await store.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<MyBusinessDto>> GetMyBusinessesAsync(Guid userId, CancellationToken cancellationToken = default)
        => store.GetMembershipsAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<AppointmentAdminDto>> GetAppointmentsAsync(Guid userId, Guid businessId,
        DateOnly? date, string? status, CancellationToken cancellationToken = default)
    {
        await DemandAppointmentAccess(userId, businessId, cancellationToken);
        AppointmentStatus? parsed = Enum.TryParse<AppointmentStatus>(status, true, out var value) ? value : null;
        var records = await store.GetAppointmentsAsync(businessId, date, parsed, cancellationToken);
        return records.Select(ToAdmin).ToArray();
    }

    public async Task<AppointmentAdminDto> ChangeStatusAsync(Guid userId, Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await DemandAppointmentAccess(userId, businessId, cancellationToken);
        var record = await store.GetAppointmentAsync(businessId, appointmentId, cancellationToken)
            ?? throw new ApiException("APPOINTMENT_NOT_FOUND", "No encontramos la cita.", 404);
        if (!Enum.TryParse<AppointmentStatus>(request.TargetStatus, true, out var target))
            throw new ApiException("INVALID_STATUS", "El estado solicitado no existe.");
        TryDomain(() => record.Appointment.ChangeStatus(target, timeProvider.GetUtcNow(), request.Reason));
        await store.SaveChangesAsync(cancellationToken);
        return ToAdmin(record);
    }

    public async Task<ServiceDto> UpdateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        UpdateServiceRequest request, CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var service = await store.GetServiceAsync(businessId, serviceId, cancellationToken)
            ?? throw new ApiException("SERVICE_NOT_FOUND", "No encontramos el servicio.", 404);
        var policy = ToPolicy(request, request.ReferencePrice);
        TryDomain(() => service.Update(request.Name, request.DurationMinutes, request.ReferencePrice, request.IsActive,
            request.Description, request.DisplayOrder, request.Version, policy));
        await store.SaveChangesAsync(cancellationToken);
        return ToServiceDto(service);
    }

    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        return await store.GetServicesAsync(businessId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<ServiceDto> CreateServiceAsync(Guid userId, Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        Service service;
        try
        {
            service = new Service(Guid.NewGuid(), businessId, request.Name, request.DurationMinutes,
                request.ReferencePrice, request.Description, request.DisplayOrder,
                ToPolicy(request, request.ReferencePrice));
        }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message); }
        store.AddService(service);
        await store.SaveChangesAsync(cancellationToken);
        return ToServiceDto(service);
    }

    public async Task DeactivateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var service = await store.GetServiceAsync(businessId, serviceId, cancellationToken)
            ?? throw new ApiException("SERVICE_NOT_FOUND", "No encontramos el servicio.", 404);
        service.Update(service.Name, service.DurationMinutes, service.ReferencePrice, false,
            service.Description, service.DisplayOrder);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        return await store.GetStaffAsync(businessId, cancellationToken);
    }

    public async Task<StaffMemberDto> CreateStaffAsync(Guid userId, Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var staff = new StaffMember(Guid.NewGuid(), businessId, request.DisplayName.Trim());
        staff.Update(request.DisplayName, request.IsActive, request.ParticipatesInAvailability);
        store.AddStaffMember(staff);
        if (!await store.SetStaffServicesAsync(businessId, staff.Id, request.ServiceIds.Distinct().ToArray(), cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "Uno o más servicios no pertenecen al establecimiento.", 409);
        await store.SaveChangesAsync(cancellationToken);
        return new(staff.Id, staff.DisplayName, staff.IsActive, staff.ParticipatesInAvailability,
            request.ServiceIds.Distinct().ToArray(), staff.Version);
    }

    public async Task<StaffMemberDto> UpdateStaffAsync(Guid userId, Guid businessId, Guid staffId,
        SaveStaffMemberRequest request, CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var staff = await store.GetStaffMemberAsync(businessId, staffId, cancellationToken)
            ?? throw new ApiException("STAFF_NOT_FOUND", "No encontramos al trabajador.", 404);
        TryDomain(() => staff.Update(request.DisplayName, request.IsActive, request.ParticipatesInAvailability,
            request.Version));
        if (!await store.SetStaffServicesAsync(businessId, staff.Id, request.ServiceIds.Distinct().ToArray(), cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "Uno o más servicios no pertenecen al establecimiento.", 409);
        await store.SaveChangesAsync(cancellationToken);
        return new(staff.Id, staff.DisplayName, staff.IsActive, staff.ParticipatesInAvailability,
            request.ServiceIds.Distinct().ToArray(), staff.Version);
    }

    public async Task<IReadOnlyList<BusinessHourAdminDto>> GetBusinessHoursAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var existing = await store.GetBusinessHoursAsync(businessId, cancellationToken);
        return Enum.GetValues<DayOfWeek>().Select(day =>
        {
            // Un día son ahora cero, uno o varios tramos. Sin tramos, está cerrado.
            var tramos = existing.Where(x => x.Day == day)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.OpensAt).ToList();
            var first = tramos.FirstOrDefault();
            return new BusinessHourAdminDto(day, tramos.Count == 0, first?.OpensAt, first?.ClosesAt,
                first?.Version ?? 0,
                tramos.Select(x => new ScheduleIntervalDto(x.OpensAt, x.ClosesAt)).ToList());
        }).ToArray();
    }

    public async Task<ConfigurationImpactDto> SetBusinessHourAsync(Guid userId, Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var current = (await store.GetBusinessHoursAsync(businessId, cancellationToken))
            .Where(x => x.Day == day).OrderBy(x => x.SortOrder).ThenBy(x => x.OpensAt).ToList();
        var existing = current.FirstOrDefault();

        // Con Intervals la jornada del día se reemplaza entera, que es lo que permite las pausas.
        // Sin Intervals se conserva el comportamiento anterior de un solo tramo.
        if (request.Intervals is not null && !request.IsClosed)
        {
            if (request.Intervals.Count == 0)
                throw new ApiException("INVALID_HOURS", "Indique al menos un intervalo o marque el día como cerrado.");
            if (existing is not null)
                EnsureVersion(existing.Version, request.Version, "El horario cambió. Recargue la información.");
            var normalized = TryDomain(() => BusinessSchedule.Normalize(
                request.Intervals.Select(x => new ScheduleInterval(x.OpensAt, x.ClosesAt))));
            foreach (var stale in current) store.RemoveBusinessHour(stale);
            var order = 0;
            foreach (var interval in normalized)
                store.AddBusinessHour(new BusinessHour(Guid.NewGuid(), businessId, day,
                    interval.OpensAt, interval.ClosesAt, order++));
        }
        else if (request.IsClosed)
        {
            if (existing is not null)
                EnsureVersion(existing.Version, request.Version, "El horario cambió. Recargue la información.");
            // Un día cerrado no conserva ningún tramo, ni siquiera los de una jornada partida.
            foreach (var stale in current) store.RemoveBusinessHour(stale);
        }
        else if (existing is null)
        {
            if (request.OpensAt is null || request.ClosesAt is null)
                throw new ApiException("INVALID_HOURS", "Indique hora de apertura y cierre.");
            store.AddBusinessHour(new BusinessHour(Guid.NewGuid(), businessId, day,
                request.OpensAt.Value, request.ClosesAt.Value));
        }
        else
        {
            if (request.OpensAt is null || request.ClosesAt is null)
                throw new ApiException("INVALID_HOURS", "Indique hora de apertura y cierre.");
            // Guardar un tramo único sustituye una jornada partida previa, para no dejar restos.
            foreach (var stale in current.Skip(1)) store.RemoveBusinessHour(stale);
            TryDomain(() => existing.Update(request.OpensAt.Value, request.ClosesAt.Value, request.Version, 0));
        }
        await store.SaveChangesAsync(cancellationToken);
        var nextDate = NextDate(day);
        var conflicts = await store.CountFutureAppointmentConflictsAsync(businessId, null, nextDate,
            request.IsClosed ? null : request.OpensAt, request.IsClosed ? null : request.ClosesAt, true,
            cancellationToken);
        return new(conflicts);
    }

    public async Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid userId,
        Guid businessId, DateOnly? from = null, CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var items = await store.GetAvailabilityExceptionsAsync(businessId, cancellationToken);
        var filtered = from.HasValue ? items.Where(x => x.Date >= from.Value) : items;
        var result = new List<AvailabilityExceptionDto>();
        foreach (var item in filtered)
            result.Add(await ToExceptionDto(item, cancellationToken));
        return result;
    }

    public async Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid userId, Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        if (!await store.StaffBelongsToBusinessAsync(businessId, request.StaffMemberId, cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "El trabajador no pertenece al establecimiento.", 409);
        if (!Enum.TryParse<AvailabilityExceptionType>(request.Type, true, out var type))
            throw new ApiException("INVALID_EXCEPTION", "Seleccione un tipo de excepción válido.");
        var existing = (await store.GetAvailabilityExceptionsAsync(businessId, cancellationToken))
            .SingleOrDefault(x => x.StaffMemberId == request.StaffMemberId && x.Date == request.Date);
        if (existing is null)
        {
            try
            {
                existing = new AvailabilityException(Guid.NewGuid(), businessId, request.StaffMemberId, request.Date,
                    type, request.OpensAt, request.ClosesAt, request.Reason);
            }
            catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message); }
            store.AddAvailabilityException(existing);
        }
        else
            TryDomain(() => existing.Update(type, request.OpensAt, request.ClosesAt, request.Reason, request.Version));
        await store.SaveChangesAsync(cancellationToken);
        return await ToExceptionDto(existing, cancellationToken);
    }

    public async Task DeleteAvailabilityExceptionAsync(Guid userId, Guid businessId, Guid exceptionId, long version,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken);
        var item = await store.GetAvailabilityExceptionAsync(businessId, exceptionId, cancellationToken)
            ?? throw new ApiException("EXCEPTION_NOT_FOUND", "No encontramos el bloqueo.", 404);
        EnsureVersion(item.Version, version, "La excepción cambió. Recargue la información.");
        store.RemoveAvailabilityException(item);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task DemandMembership(Guid userId, Guid businessId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ApiException("UNAUTHENTICATED", "Debe iniciar sesión.", 401);
        if (!await store.IsMemberAsync(userId, businessId, cancellationToken))
            throw new ApiException("BUSINESS_ACCESS_DENIED", "No tiene acceso a este establecimiento.", 403);
    }

    private async Task DemandAppointmentAccess(Guid userId, Guid businessId, CancellationToken cancellationToken)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        if (!await store.CanManageAppointmentsAsync(userId, businessId, cancellationToken))
            throw new ApiException("APPOINTMENTS_FORBIDDEN", "No tiene permiso para administrar citas.", 403);
        // Ocultar el botón no basta: una URL directa llegaba igual al módulo no habilitado.
        if (!await store.IsModuleEnabledAsync(businessId, BusinessModuleKind.Appointments, cancellationToken))
            throw new ApiException("MODULE_DISABLED", "Este establecimiento no tiene citas habilitadas.", 403);
    }

    private async Task DemandConfigurationAccess(Guid userId, Guid businessId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ApiException("UNAUTHENTICATED", "Debe iniciar sesión.", 401);
        if (!await store.IsMemberAsync(userId, businessId, cancellationToken))
            throw new ApiException("BUSINESS_ACCESS_DENIED", "No tiene acceso a este establecimiento.", 403);
        if (!await store.CanManageConfigurationAsync(userId, businessId, cancellationToken))
            throw new ApiException("CONFIGURATION_FORBIDDEN", "No tiene permiso para cambiar la configuración.", 403);
    }

    private void ValidateContact(CreateAppointmentRequest request)
    {
        if (!request.ConsentAccepted || request.ConsentNoticeVersion != consentPolicy.CurrentVersion)
            throw new ApiException("CONSENT_REQUIRED", "Debe aceptar la versión vigente del aviso.");
        if (request.CustomerAlias.Trim().Length is < 2 or > 100)
            throw new ApiException("INVALID_ALIAS", "Ingrese un nombre o alias de 2 a 100 caracteres.");
        var digits = PhoneDigits().Replace(request.Phone, "");
        if (digits.Length is < 7 or > 15)
            throw new ApiException("INVALID_PHONE", "Ingrese un teléfono de 7 a 15 dígitos.");
        if ((request.Notes?.Length ?? 0) > 300)
            throw new ApiException("INVALID_NOTES", "La observación puede tener máximo 300 caracteres.");
    }

    private AppointmentAdminDto ToAdmin(AppointmentRecord record)
    {
        var appointment = record.Appointment;
        return new(appointment.Id, appointment.BusinessId, appointment.ServiceName, appointment.StartAtUtc,
            appointment.EndAtUtc, protector.Unprotect(appointment.ProtectedCustomerAlias),
            protector.Unprotect(appointment.ProtectedPhone), protector.Unprotect(appointment.ProtectedNotes),
            appointment.Status.ToString(), appointment.CreatedAtUtc, record.Consent.NoticeVersion,
            record.Consent.AcceptedAtUtc, appointment.Version,
            appointment.DisplayPrice, appointment.RequiresDeposit, appointment.DepositType.ToString(),
            appointment.DepositConfiguredValue, appointment.DepositAmount, appointment.DepositStatus.ToString(),
            DepositLabel(appointment.DepositStatus), appointment.DepositInstructions,
            appointment.DepositWhatsAppNumber, appointment.DepositReportedAtUtc, appointment.DepositVerifiedAtUtc,
            record.DepositVerifiedByName, appointment.DepositRejectionReason);
    }

    /// <summary>Los rótulos visibles del adelanto, iguales en el seguimiento y en el panel.</summary>
    public static string DepositLabel(DepositStatus status) => status switch
    {
        DepositStatus.Pending => "Adelanto pendiente",
        DepositStatus.Reported => "Comprobante reportado",
        DepositStatus.Verified => "Adelanto verificado",
        DepositStatus.Rejected => "Comprobante rechazado",
        _ => "No requiere adelanto"
    };

    /// <summary>
    /// El enlace de WhatsApp sólo se puede armar aquí porque necesita el código en claro, y de ese
    /// código la base sólo guarda el hash.
    /// </summary>
    private static AppointmentTrackingDto ToTracking(AppointmentRecord record, string trackingCode)
    {
        var appointment = record.Appointment;
        var status = appointment.Status;
        var labels = new Dictionary<AppointmentStatus, string>
        {
            [AppointmentStatus.Pending] = "Pendiente", [AppointmentStatus.Confirmed] = "Confirmada",
            [AppointmentStatus.Rejected] = "Rechazada", [AppointmentStatus.Cancelled] = "Cancelada",
            [AppointmentStatus.Completed] = "Completada", [AppointmentStatus.NoShow] = "No asistió"
        };
        var deposit = appointment.DepositStatus;
        // El botón acompaña mientras haya algo que enviar: pendiente, o rechazado y hay que reintentar.
        var canSend = deposit is DepositStatus.Pending or DepositStatus.Rejected;
        return new(status.ToString(), labels[status], record.Business.Name, appointment.ServiceName,
            appointment.StartAtUtc, $"******{appointment.PhoneLast4}",
            status is AppointmentStatus.Pending or AppointmentStatus.Confirmed, appointment.UpdatedAtUtc,
            appointment.DisplayPrice, appointment.RequiresDeposit, appointment.DepositType.ToString(),
            appointment.DepositAmount, deposit.ToString(), DepositLabel(deposit),
            appointment.DepositInstructions,
            canSend
                ? WhatsAppNumbers.BuildLink(appointment.DepositWhatsAppNumber, DepositMessage.Build(
                    record.Business.Name, appointment.ServiceName, appointment.StartAtUtc,
                    record.Business.TimeZoneId, trackingCode, appointment.DepositAmount, appointment.DisplayPrice))
                : null,
            canSend, appointment.DepositRejectionReason);
    }

    private static void TryDomain(Action action)
    {
        try { action(); }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 409); }
    }

    /// <summary>
    /// Los intervalos mal formados son un error de la petición, no un conflicto de concurrencia:
    /// se devuelven como 400 para que la pantalla los muestre como error de validación.
    /// </summary>
    private static T TryDomain<T>(Func<T> action)
    {
        try { return action(); }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 400); }
    }

    private static void EnsureServiceActive(Service service)
    {
        try { service.EnsureActive(); }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 404); }
    }

    private async Task<AvailabilityExceptionDto> ToExceptionDto(AvailabilityException item,
        CancellationToken cancellationToken)
    {
        var conflicts = await store.CountFutureAppointmentConflictsAsync(item.BusinessId, item.StaffMemberId,
            item.Date, item.Type == AvailabilityExceptionType.ClosedAllDay ? null : item.OpensAt,
            item.Type == AvailabilityExceptionType.ClosedAllDay ? null : item.ClosesAt,
            item.Type == AvailabilityExceptionType.ExtraordinaryOpening, cancellationToken);
        return new(item.Id, item.StaffMemberId, item.Date, item.Type.ToString(), item.OpensAt, item.ClosesAt,
            item.Reason, conflicts, item.Version);
    }

    private DateOnly NextDate(DayOfWeek day)
    {
        var date = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        do { date = date.AddDays(1); } while (date.DayOfWeek != day);
        return date;
    }

    private static void EnsureVersion(long actual, long expected, string message)
    {
        if (actual != expected) throw new ApiException("CONCURRENCY_CONFLICT", message, 409);
    }

    private static ServiceDto ToServiceDto(Service service) => new(service.Id, service.Name, service.Description,
        service.DurationMinutes, service.ReferencePrice, service.DisplayOrder, service.IsActive, 0, service.Version,
        service.RequiresDeposit, service.DepositType.ToString(), service.DepositValue,
        service.Deposit.CalculateFor(service.ReferencePrice), service.DepositInstructions,
        service.DepositWhatsAppNumber);

    /// <summary>Traduce la solicitud a la política del dominio, que es quien decide si es válida.</summary>
    private static DepositPolicy ToPolicy(ServiceDepositFields fields, decimal referencePrice)
    {
        if (!fields.RequiresDeposit) return DepositPolicy.None;
        if (!Enum.TryParse<DepositType>(fields.DepositType, true, out var type))
            throw new ApiException("DEPOSIT_TYPE_REQUIRED", "Elija si el adelanto es un valor fijo o un porcentaje.");
        try
        {
            return DepositPolicy.Create(true, type, fields.DepositValue, fields.DepositInstructions,
                fields.DepositWhatsAppNumber, referencePrice);
        }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message); }
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex PhoneDigits();
}
