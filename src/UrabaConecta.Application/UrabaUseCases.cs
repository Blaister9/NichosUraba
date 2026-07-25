using System.Globalization;
using System.Text.RegularExpressions;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed partial class UrabaUseCases(IUrabaStore store, IPublicCodeService codes,
    IPersonalDataProtector protector, TimeProvider timeProvider) : IUrabaUseCases
{
    public Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search, string? municipality,
        string? category, CancellationToken cancellationToken = default)
        => store.FindBusinessesAsync(search?.Trim(), municipality, category, cancellationToken);

    public Task<BusinessProfileDto?> GetBusinessAsync(string slug, CancellationToken cancellationToken = default)
        => store.GetBusinessProfileAsync(slug, cancellationToken);

    public async Task<SlotListDto> GetSlotsAsync(string slug, Guid serviceId, DateOnly date,
        CancellationToken cancellationToken = default)
    {
        var context = await store.GetSchedulingContextAsync(slug, serviceId, date, cancellationToken)
            ?? throw new ApiException("BUSINESS_OR_SERVICE_NOT_FOUND", "No encontramos el establecimiento o servicio.", 404);
        EnsureServiceActive(context.Service);
        if (date > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(60))
            throw new ApiException("DATE_OUT_OF_RANGE", "Solo puede consultar los próximos 60 días.");

        var businessHour = context.Hours.FirstOrDefault(x => x.Day == date.DayOfWeek);
        if (businessHour is null || context.EligibleStaff.Count == 0)
            return new(context.Business.TimeZoneId, date, []);

        var zone = TimeZoneInfo.FindSystemTimeZoneById(context.Business.TimeZoneId);
        var all = context.EligibleStaff
            .SelectMany(staff =>
            {
                var exception = context.Exceptions.FirstOrDefault(x => x.StaffMemberId == staff.Id && x.Date == date);
                if (exception?.IsUnavailable == true) return [];
                var opensAt = exception?.OpensAt ?? businessHour.OpensAt;
                var closesAt = exception?.ClosesAt ?? businessHour.ClosesAt;
                return AppointmentSlotCalculator.Calculate(date, opensAt, closesAt,
                    context.Service.DurationMinutes, zone, timeProvider.GetUtcNow(),
                    context.Occupied.Where(x => x.StaffId == staff.Id).Select(x => (x.Start, x.End)))
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
        var appointment = new Appointment(Guid.NewGuid(), context.Business.Id, context.Service.Id, staffId,
            request.Start, context.Service.DurationMinutes, context.Service.Name, context.Service.ReferencePrice,
            protector.Protect(request.CustomerAlias.Trim()), protector.Protect(digits), digits[^4..],
            protector.Protect(request.Notes?.Trim() ?? ""), code.Hash, code.Version, consent.Id, now);
        consent.LinkAppointment(appointment.Id);

        if (!await store.AddAppointmentAsync(appointment, consent, cancellationToken))
            throw new ApiException("SLOT_UNAVAILABLE", "Ese horario acaba de ocuparse. Elija otro.", 409);
        return new(code.PlainText, appointment.Status.ToString(), appointment.ServiceName, appointment.StartAtUtc);
    }

    public async Task<AppointmentTrackingDto?> GetTrackingAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 20 or > 128) return null;
        var record = await store.FindAppointmentByCodeHashAsync(codes.Hash(code), cancellationToken);
        return record is null ? null : ToTracking(record);
    }

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
        await DemandMembership(userId, businessId, cancellationToken);
        AppointmentStatus? parsed = Enum.TryParse<AppointmentStatus>(status, true, out var value) ? value : null;
        var records = await store.GetAppointmentsAsync(businessId, date, parsed, cancellationToken);
        return records.Select(ToAdmin).ToArray();
    }

    public async Task<AppointmentAdminDto> ChangeStatusAsync(Guid userId, Guid businessId, Guid appointmentId,
        ChangeAppointmentStatusRequest request, CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
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
        await DemandMembership(userId, businessId, cancellationToken);
        var service = await store.GetServiceAsync(businessId, serviceId, cancellationToken)
            ?? throw new ApiException("SERVICE_NOT_FOUND", "No encontramos el servicio.", 404);
        TryDomain(() => service.Update(request.Name, request.DurationMinutes, request.ReferencePrice, request.IsActive));
        await store.SaveChangesAsync(cancellationToken);
        return new(service.Id, service.Name, service.DurationMinutes, service.ReferencePrice, service.IsActive);
    }

    public async Task<ServiceDto> CreateServiceAsync(Guid userId, Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        Service service;
        try { service = new Service(Guid.NewGuid(), businessId, request.Name.Trim(), request.DurationMinutes, request.ReferencePrice); }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message); }
        store.AddService(service);
        await store.SaveChangesAsync(cancellationToken);
        return new(service.Id, service.Name, service.DurationMinutes, service.ReferencePrice, service.IsActive);
    }

    public async Task DeactivateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        var service = await store.GetServiceAsync(businessId, serviceId, cancellationToken)
            ?? throw new ApiException("SERVICE_NOT_FOUND", "No encontramos el servicio.", 404);
        service.Update(service.Name, service.DurationMinutes, service.ReferencePrice, false);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        return await store.GetStaffAsync(businessId, cancellationToken);
    }

    public async Task<StaffMemberDto> CreateStaffAsync(Guid userId, Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        var staff = new StaffMember(Guid.NewGuid(), businessId, request.DisplayName.Trim());
        staff.Update(request.DisplayName, request.IsActive);
        store.AddStaffMember(staff);
        if (!await store.SetStaffServicesAsync(businessId, staff.Id, request.ServiceIds.Distinct().ToArray(), cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "Uno o más servicios no pertenecen al establecimiento.", 409);
        await store.SaveChangesAsync(cancellationToken);
        return new(staff.Id, staff.DisplayName, staff.IsActive, request.ServiceIds.Distinct().ToArray());
    }

    public async Task<StaffMemberDto> UpdateStaffAsync(Guid userId, Guid businessId, Guid staffId,
        SaveStaffMemberRequest request, CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        var staff = await store.GetStaffMemberAsync(businessId, staffId, cancellationToken)
            ?? throw new ApiException("STAFF_NOT_FOUND", "No encontramos al trabajador.", 404);
        TryDomain(() => staff.Update(request.DisplayName, request.IsActive));
        if (!await store.SetStaffServicesAsync(businessId, staff.Id, request.ServiceIds.Distinct().ToArray(), cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "Uno o más servicios no pertenecen al establecimiento.", 409);
        await store.SaveChangesAsync(cancellationToken);
        return new(staff.Id, staff.DisplayName, staff.IsActive, request.ServiceIds.Distinct().ToArray());
    }

    public async Task SetBusinessHourAsync(Guid userId, Guid businessId, DayOfWeek day,
        SaveBusinessHourRequest request, CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        var existing = await store.GetBusinessHourAsync(businessId, day, cancellationToken);
        if (request.IsClosed)
        {
            if (existing is not null) store.RemoveBusinessHour(existing);
        }
        else if (existing is null)
            store.AddBusinessHour(new BusinessHour(Guid.NewGuid(), businessId, day, request.OpensAt, request.ClosesAt));
        else
            TryDomain(() => existing.Update(request.OpensAt, request.ClosesAt));
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AvailabilityExceptionDto>> GetAvailabilityExceptionsAsync(Guid userId,
        Guid businessId, CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        var items = await store.GetAvailabilityExceptionsAsync(businessId, cancellationToken);
        return items.Select(ToExceptionDto).ToArray();
    }

    public async Task<AvailabilityExceptionDto> SaveAvailabilityExceptionAsync(Guid userId, Guid businessId,
        SaveAvailabilityExceptionRequest request, CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        if (!await store.StaffBelongsToBusinessAsync(businessId, request.StaffMemberId, cancellationToken))
            throw new ApiException("CROSS_BUSINESS_REFERENCE", "El trabajador no pertenece al establecimiento.", 409);
        var existing = (await store.GetAvailabilityExceptionsAsync(businessId, cancellationToken))
            .SingleOrDefault(x => x.StaffMemberId == request.StaffMemberId && x.Date == request.Date);
        if (existing is null)
        {
            existing = new AvailabilityException(Guid.NewGuid(), businessId, request.StaffMemberId, request.Date,
                request.IsUnavailable, request.OpensAt, request.ClosesAt);
            if (!request.IsUnavailable) TryDomain(() => existing.Update(false, request.OpensAt, request.ClosesAt));
            store.AddAvailabilityException(existing);
        }
        else
            TryDomain(() => existing.Update(request.IsUnavailable, request.OpensAt, request.ClosesAt));
        await store.SaveChangesAsync(cancellationToken);
        return ToExceptionDto(existing);
    }

    public async Task DeleteAvailabilityExceptionAsync(Guid userId, Guid businessId, Guid exceptionId,
        CancellationToken cancellationToken = default)
    {
        await DemandMembership(userId, businessId, cancellationToken);
        var item = await store.GetAvailabilityExceptionAsync(businessId, exceptionId, cancellationToken)
            ?? throw new ApiException("EXCEPTION_NOT_FOUND", "No encontramos el bloqueo.", 404);
        store.RemoveAvailabilityException(item);
        await store.SaveChangesAsync(cancellationToken);
    }

    private async Task DemandMembership(Guid userId, Guid businessId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty) throw new ApiException("UNAUTHENTICATED", "Debe iniciar sesión.", 401);
        if (!await store.IsMemberAsync(userId, businessId, cancellationToken))
            throw new ApiException("BUSINESS_ACCESS_DENIED", "No tiene acceso a este establecimiento.", 403);
    }

    private static void ValidateContact(CreateAppointmentRequest request)
    {
        if (!request.ConsentAccepted || request.ConsentNoticeVersion != "pilot-1")
            throw new ApiException("CONSENT_REQUIRED", "Debe aceptar la versión vigente del aviso.");
        if (request.CustomerAlias.Trim().Length is < 2 or > 100)
            throw new ApiException("INVALID_ALIAS", "Ingrese un nombre o alias de 2 a 100 caracteres.");
        var digits = PhoneDigits().Replace(request.Phone, "");
        if (digits.Length is < 7 or > 15)
            throw new ApiException("INVALID_PHONE", "Ingrese un teléfono de 7 a 15 dígitos.");
        if ((request.Notes?.Length ?? 0) > 300)
            throw new ApiException("INVALID_NOTES", "La observación puede tener máximo 300 caracteres.");
    }

    private AppointmentAdminDto ToAdmin(AppointmentRecord record) => new(record.Appointment.Id,
        record.Appointment.BusinessId, record.Appointment.ServiceName, record.Appointment.StartAtUtc,
        record.Appointment.EndAtUtc, protector.Unprotect(record.Appointment.ProtectedCustomerAlias),
        protector.Unprotect(record.Appointment.ProtectedPhone), protector.Unprotect(record.Appointment.ProtectedNotes),
        record.Appointment.Status.ToString(), record.Appointment.CreatedAtUtc, record.Consent.NoticeVersion,
        record.Consent.AcceptedAtUtc, record.Appointment.Version);

    private static AppointmentTrackingDto ToTracking(AppointmentRecord record)
    {
        var status = record.Appointment.Status;
        var labels = new Dictionary<AppointmentStatus, string>
        {
            [AppointmentStatus.Pending] = "Pendiente", [AppointmentStatus.Confirmed] = "Confirmada",
            [AppointmentStatus.Rejected] = "Rechazada", [AppointmentStatus.Cancelled] = "Cancelada",
            [AppointmentStatus.Completed] = "Completada", [AppointmentStatus.NoShow] = "No asistió"
        };
        return new(status.ToString(), labels[status], record.Business.Name, record.Appointment.ServiceName,
            record.Appointment.StartAtUtc, $"******{record.Appointment.PhoneLast4}",
            status is AppointmentStatus.Pending or AppointmentStatus.Confirmed, record.Appointment.UpdatedAtUtc);
    }

    private static void TryDomain(Action action)
    {
        try { action(); }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 409); }
    }

    private static void EnsureServiceActive(Service service)
    {
        try { service.EnsureActive(); }
        catch (DomainException ex) { throw new ApiException(ex.Code, ex.Message, 404); }
    }

    private static AvailabilityExceptionDto ToExceptionDto(AvailabilityException item)
        => new(item.Id, item.StaffMemberId, item.Date, item.IsUnavailable, item.OpensAt, item.ClosesAt);

    [GeneratedRegex(@"\D")]
    private static partial Regex PhoneDigits();
}
