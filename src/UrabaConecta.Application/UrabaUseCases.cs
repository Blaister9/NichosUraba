using System.Globalization;
using System.Text.RegularExpressions;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Application;

public sealed partial class UrabaUseCases(IUrabaStore store, IMembershipAdministrationStore membershipStore,
    IIdentityAccountManager identityAccounts, IPublicCodeService codes,
    IPersonalDataProtector protector, IConsentPolicyProvider consentPolicy,
    IPushNotificationService push, INotificationPublisher notifications,
    TimeProvider timeProvider) : IUrabaUseCases
{
    public Task<IReadOnlyList<BusinessCardDto>> GetBusinessesAsync(string? search, string? municipality,
        string? category, CancellationToken cancellationToken = default)
        => store.FindBusinessesAsync(search?.Trim(), municipality, category, cancellationToken);

    public Task<IReadOnlyList<CategoryCardDto>> GetCategoriesAsync(string? municipality,
        CancellationToken cancellationToken = default)
        => store.FindCategoriesAsync(municipality, cancellationToken);

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
        return new(context.Business.TimeZoneId, date, BuildSlots(context, date));
    }

    /// <summary>
    /// El primer día con horarios a partir de <paramref name="from"/>, mirando como mucho
    /// <paramref name="days"/> jornadas. Devuelve null si ninguna tiene hueco.
    /// </summary>
    /// <remarks>
    /// Existe para la Home, que sólo necesita saber si hay disponibilidad cercana y cuánta. Pedir
    /// día por día costaba siete lecturas por jornada y repetía cinco que no dependen de la fecha;
    /// aquí el contexto se lee una vez para todo el rango y los días se resuelven en memoria con
    /// exactamente las mismas reglas que <see cref="GetSlotsAsync"/>.
    /// </remarks>
    public async Task<SlotListDto?> FindNextAvailabilityAsync(string slug, Guid serviceId, DateOnly from,
        int days, CancellationToken cancellationToken = default)
    {
        if (days is < 1 or > 14) throw new ApiException("INVALID_RANGE", "El rango admite entre 1 y 14 días.");
        var last = from.AddDays(days - 1);
        // El mismo tope que la consulta de un día: el extremo del rango no puede saltárselo.
        if (last > DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime).AddDays(60))
            throw new ApiException("DATE_OUT_OF_RANGE", "Solo puede consultar los próximos 60 días.");
        var context = await store.GetSchedulingContextAsync(slug, serviceId, from, last, cancellationToken);
        if (context is null) return null;
        EnsureServiceActive(context.Service);
        for (var offset = 0; offset < days; offset++)
        {
            var date = from.AddDays(offset);
            var slots = BuildSlots(context, date);
            if (slots.Count > 0) return new(context.Business.TimeZoneId, date, slots);
        }
        return null;
    }

    /// <summary>
    /// Cuántas jornadas de recogida mira la Home para decir a qué hora se recoge. Es el mismo rango
    /// que ofrece la pantalla de pedidos cuando no se le pide una fecha concreta.
    /// </summary>
    private const int PickupHorizonDays = 7;

    /// <summary>
    /// El feed de la Home. La disponibilidad y las franjas de recogida se calculan aquí con los
    /// mismos métodos que usan la pantalla de citas y la de pedidos: lo que cambia es de dónde sale
    /// el material —una lectura para todos los negocios en lugar de una tanda por negocio—, no las
    /// reglas que deciden qué se enseña.
    /// </summary>
    public async Task<HomeFeedDto> GetHomeFeedAsync(DateOnly today, int availabilityDays,
        CancellationToken cancellationToken = default)
    {
        if (availabilityDays is < 1 or > 14)
            throw new ApiException("INVALID_RANGE", "El rango admite entre 1 y 14 días.");
        var source = await store.GetHomeFeedSourceAsync(today, availabilityDays, PickupHorizonDays,
            cancellationToken);
        var now = timeProvider.GetUtcNow();
        var businesses = new List<HomeFeedBusinessDto>(source.Businesses.Count);
        foreach (var business in source.Businesses)
        {
            businesses.Add(new HomeFeedBusinessDto(business.Slug, business.Name, business.Category,
                business.Municipality, business.HasVirtualQueue, business.HasPickupOrdering,
                business.HasScheduling, business.CoverUrl, business.CoverAltText, business.PriceFrom,
                OpenStatus(business.TimeZoneId, business.Hours),
                // Un negocio puede conservar la definición de su fila con el módulo ya apagado.
                // La ficha pública tampoco la enseñaría en ese caso.
                business.HasVirtualQueue ? business.Queue : null,
                // Igual que la ficha pública: sin el módulo de citas, los servicios no se enseñan.
                // Un negocio puede conservar filas de servicio de antes de apagarlo.
                business.HasScheduling ? business.Services : [],
                Availability(business, source, today, availabilityDays),
                business.HasPickupOrdering ? business.Product : null,
                NextPickup(business, source, now)));
        }
        var promotions = await push.GetPublicPromotionsAsync(cancellationToken);
        return new(businesses, promotions);
    }

    /// <summary>El día más cercano con horarios para el primer servicio, dentro del rango pedido.</summary>
    private HomeAvailabilityDto? Availability(HomeFeedBusinessSource business, HomeFeedSource source,
        DateOnly from, int days)
    {
        if (!business.HasScheduling || business.Services.Count == 0) return null;
        if (!source.EligibleStaff.TryGetValue(business.Id, out var staff) || staff.Count == 0) return null;
        var occupied = source.Occupied.Where(x => x.BusinessId == business.Id)
            .Select(x => (x.Start, x.End, x.StaffId)).ToArray();
        var exceptions = source.Exceptions.Where(x => x.BusinessId == business.Id).ToArray();
        for (var offset = 0; offset < days; offset++)
        {
            var date = from.AddDays(offset);
            var slots = BuildSlots(business.TimeZoneId, business.Services[0].DurationMinutes,
                business.Hours.Select(x => (x.Day, TimeOnly.Parse(x.OpensAt), TimeOnly.Parse(x.ClosesAt))),
                staff, exceptions, occupied, date);
            if (slots.Count > 0) return new(date, slots.Count);
        }
        return null;
    }

    /// <summary>La primera franja de recogida con cupo, o null si no queda ninguna en el horizonte.</summary>
    private static DateTimeOffset? NextPickup(HomeFeedBusinessSource business, HomeFeedSource source,
        DateTimeOffset now)
    {
        if (!business.HasPickupOrdering || business.Pickup is not { } settings) return null;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId); }
        catch (TimeZoneNotFoundException) { return null; }
        var first = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).Date);
        var candidates = PickupSlotCalculator.Candidates(
            Enumerable.Range(0, PickupHorizonDays).Select(first.AddDays),
            business.Hours.Select(x => (x.Day, TimeOnly.Parse(x.OpensAt), TimeOnly.Parse(x.ClosesAt))),
            settings.ReceivesFrom, settings.ReceivesUntil, settings.SlotIntervalMinutes, zone,
            now.AddMinutes(settings.MinimumPreparationMinutes));
        foreach (var start in candidates.OrderBy(x => x))
        {
            var taken = source.PickupOccupancy.TryGetValue((business.Id, start), out var count) ? count : 0;
            if (taken < settings.MaximumActivePerSlot) return start;
        }
        return null;
    }

    /// <summary>
    /// Si el negocio está atendiendo ahora. Es la misma frase que el directorio pone en la tarjeta;
    /// se recalcula aquí porque depende de la hora de quien mira, no de cuándo se leyó el horario.
    /// </summary>
    private static string? OpenStatus(string timeZoneId, IReadOnlyList<BusinessHourDto> hours)
    {
        if (hours.Count == 0) return null;
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return null; }
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);
        var today = hours.Where(x => x.Day == local.DayOfWeek)
            .Select(x => new ScheduleInterval(TimeOnly.Parse(x.OpensAt), TimeOnly.Parse(x.ClosesAt)))
            .OrderBy(x => x.OpensAt).ToList();
        if (today.Count == 0) return "Cerrado";
        var moment = TimeOnly.FromDateTime(local.DateTime);
        if (BusinessSchedule.IntervalAt(today, moment) is not null) return "Abierto";
        var next = BusinessSchedule.NextInterval(today, moment);
        return next is null ? "Cerrado"
            : $"Cerrado temporalmente · abre a las {next.Value.OpensAt.ToString("h:mm tt", new CultureInfo("es-CO"))}";
    }

    /// <summary>
    /// Las horas libres de un día concreto dentro de un contexto ya leído. Es el único sitio donde
    /// viven las reglas de disponibilidad, así que la consulta de un día y la búsqueda del próximo
    /// día con hueco no pueden responder cosas distintas.
    /// </summary>
    private IReadOnlyList<SlotDto> BuildSlots(SchedulingContext context, DateOnly date)
        => BuildSlots(context.Business.TimeZoneId, context.Service.DurationMinutes,
            context.Hours.Select(x => (x.Day, x.OpensAt, x.ClosesAt)),
            context.EligibleStaff.Select(x => x.Id).ToArray(), context.Exceptions, context.Occupied, date);

    /// <summary>
    /// La misma jornada resuelta a partir de sus piezas sueltas, sin exigir un
    /// <see cref="SchedulingContext"/>. El feed de la Home lee el material de todos los negocios de
    /// una vez y no tiene un contexto por negocio que pasar; lo que no puede es calcular la
    /// disponibilidad por su cuenta, porque entonces la Home y la pantalla de citas podrían decir
    /// cosas distintas sobre el mismo día.
    /// </summary>
    private IReadOnlyList<SlotDto> BuildSlots(string timeZoneId, int durationMinutes,
        IEnumerable<(DayOfWeek Day, TimeOnly OpensAt, TimeOnly ClosesAt)> hours, IReadOnlyList<Guid> staffIds,
        IReadOnlyList<AvailabilityException> exceptions,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End, Guid StaffId)> occupied, DateOnly date)
    {
        // Un día puede tener varios tramos: sin filas está cerrado, con varias es jornada partida.
        var dayIntervals = hours.Where(x => x.Day == date.DayOfWeek)
            .Select(x => new ScheduleInterval(x.OpensAt, x.ClosesAt)).ToList();
        if (dayIntervals.Count == 0 || staffIds.Count == 0) return [];

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        // El contexto puede abarcar varios días: se acotan las citas ocupadas a la jornada que se
        // está resolviendo, para que el resultado no dependa de cuántos días se hayan leído.
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        return staffIds
            .SelectMany(staffId =>
            {
                var exception = exceptions.FirstOrDefault(x => x.StaffMemberId == staffId && x.Date == date);
                if (exception?.Type == AvailabilityExceptionType.ClosedAllDay) return [];
                // Una apertura extraordinaria reemplaza la jornada de esa fecha por su propio tramo.
                var extraordinary = exception?.Type == AvailabilityExceptionType.ExtraordinaryOpening;
                var intervals = extraordinary
                    ? [new ScheduleInterval(exception!.OpensAt!.Value, exception.ClosesAt!.Value)]
                    : dayIntervals;
                var busy = occupied
                    .Where(x => x.StaffId == staffId && x.Start < dayEnd && x.End > dayStart)
                    .Select(x => (x.Start, x.End)).ToList();
                if (exception?.Type == AvailabilityExceptionType.ClosedInterval)
                {
                    var startUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                        date.ToDateTime(exception.OpensAt!.Value), zone), TimeSpan.Zero);
                    var endUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                        date.ToDateTime(exception.ClosesAt!.Value), zone), TimeSpan.Zero);
                    busy.Add((startUtc, endUtc));
                }
                return AppointmentSlotCalculator.Calculate(date, intervals,
                    durationMinutes, zone, timeProvider.GetUtcNow(), busy)
                    .Select(x => new SlotDto(x.Start, x.End));
            })
            .GroupBy(x => x.Start).Select(x => x.First()).OrderBy(x => x.Start).ToArray();
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
        // El aviso se guarda; sacarlo hacia los dispositivos es trabajo del buzón. Si el servicio
        // Push está caído, la cita queda creada igual y el negocio la encuentra en su bandeja.
        await notifications.PublishAsync(new(appointment.BusinessId, NotificationAudience.Business,
            NotificationKind.AppointmentRequested, "Nueva cita",
            $"Solicitud para {appointment.ServiceName}.",
            $"/panel/{appointment.BusinessId}/citas#appointment-{appointment.Id}",
            TrackedEntities.Appointment, appointment.Id,
            Notification.Key(NotificationAudience.Business, NotificationKind.AppointmentRequested, appointment.Id),
            PushAudience.Owner), cancellationToken);
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

    public async Task<AppointmentBoardDto> GetAppointmentsAsync(Guid userId, Guid businessId,
        DateOnly? date, string? status, CancellationToken cancellationToken = default)
    {
        await DemandAppointmentAccess(userId, businessId, cancellationToken);
        AppointmentStatus? parsed = Enum.TryParse<AppointmentStatus>(status, true, out var value) ? value : null;
        var board = await store.GetAppointmentsAsync(businessId, date, parsed, cancellationToken);
        // El nombre y la zona salen del mismo negocio que la consulta ya resolvió: la pantalla no
        // tiene que pedirlos aparte para poder decir de quién es la agenda y en qué hora se lee.
        return new(businessId, board.Business.Name, board.Business.TimeZoneId,
            board.Appointments.Select(ToAdmin).ToArray());
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
        // Todos los estados dejan rastro para el cliente, no sólo los tres que antes salían por
        // Push: quien abre su seguimiento tiene que poder leer qué pasó y cuándo.
        var appointment = record.Appointment;
        (NotificationKind Kind, string Title, string Body)? announcement = target switch
        {
            AppointmentStatus.Confirmed => (NotificationKind.AppointmentConfirmed, "Cita confirmada",
                $"{appointment.ServiceName}: el negocio confirmó tu cita."),
            AppointmentStatus.Rejected => (NotificationKind.AppointmentRejected, "Novedad en tu cita",
                "El negocio no pudo confirmar la solicitud. Revisa el seguimiento."),
            AppointmentStatus.Cancelled => (NotificationKind.AppointmentCancelled, "Cita cancelada",
                "La cita fue cancelada. Revisa el seguimiento para ver su estado."),
            AppointmentStatus.Completed => (NotificationKind.AppointmentCompleted, "Cita completada",
                $"{appointment.ServiceName}: el negocio marcó tu cita como atendida."),
            AppointmentStatus.NoShow => (NotificationKind.AppointmentNoShow, "Cita no asistida",
                "El negocio registró que no se presentó nadie a esta cita."),
            _ => null
        };
        if (announcement is { } news)
            await notifications.PublishAsync(new(appointment.BusinessId, NotificationAudience.Customer,
                news.Kind, news.Title, news.Body, null, TrackedEntities.Appointment, appointment.Id,
                Notification.Key(NotificationAudience.Customer, news.Kind, appointment.Id),
                PushAudience.Appointment, Renotify: true), cancellationToken);
        return ToAdmin(record);
    }

    public async Task<ServiceDto> UpdateServiceAsync(Guid userId, Guid businessId, Guid serviceId,
        UpdateServiceRequest request, CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Services);
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
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Services);
        return await store.GetServicesAsync(businessId, timeProvider.GetUtcNow(), cancellationToken);
    }

    public async Task<ServiceDto> CreateServiceAsync(Guid userId, Guid businessId, CreateServiceRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Services);
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
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Services);
        var service = await store.GetServiceAsync(businessId, serviceId, cancellationToken)
            ?? throw new ApiException("SERVICE_NOT_FOUND", "No encontramos el servicio.", 404);
        service.Update(service.Name, service.DurationMinutes, service.ReferencePrice, false,
            service.Description, service.DisplayOrder);
        await store.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid userId, Guid businessId,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Staff);
        return await store.GetStaffAsync(businessId, cancellationToken);
    }

    public async Task<StaffMemberDto> CreateStaffAsync(Guid userId, Guid businessId, SaveStaffMemberRequest request,
        CancellationToken cancellationToken = default)
    {
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Staff);
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
        await DemandConfigurationAccess(userId, businessId, cancellationToken,
            BusinessModuleKind.Staff);
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

    /// <summary>
    /// <paramref name="capability"/> exige además que el negocio tenga esa capacidad. Ocultar la
    /// tarjeta no basta: una dirección escrita a mano llegaba igual a administrar servicios en un
    /// negocio que sólo despacha pedidos.
    /// </summary>
    private async Task DemandConfigurationAccess(Guid userId, Guid businessId,
        CancellationToken cancellationToken, BusinessModuleKind? capability = null)
    {
        if (userId == Guid.Empty) throw new ApiException("UNAUTHENTICATED", "Debe iniciar sesión.", 401);
        if (!await store.IsMemberAsync(userId, businessId, cancellationToken))
            throw new ApiException("BUSINESS_ACCESS_DENIED", "No tiene acceso a este establecimiento.", 403);
        if (!await store.CanManageConfigurationAsync(userId, businessId, cancellationToken))
            throw new ApiException("CONFIGURATION_FORBIDDEN", "No tiene permiso para cambiar la configuración.", 403);
        if (capability is { } required &&
            !await store.HasCapabilityAsync(businessId, required, cancellationToken))
            throw new ApiException("CAPABILITY_DISABLED",
                "Este establecimiento no tiene esa función habilitada.", 403);
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
