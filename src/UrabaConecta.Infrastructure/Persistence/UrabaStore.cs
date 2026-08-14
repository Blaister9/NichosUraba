using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class UrabaStore(AppDbContext db, IObjectStorage storage, IPublicDirectoryCache publicCache)
    : IUrabaStore
{
    public Task<IReadOnlyList<BusinessCardDto>> FindBusinessesAsync(string? search, string? municipality,
        string? category, CancellationToken cancellationToken)
        // El directorio publicado es idéntico para todos los visitantes.
        => publicCache.GetOrCreateAsync($"directorio|{search}|{municipality}|{category}",
            ct => QueryBusinessesAsync(search, municipality, category, ct), cancellationToken);

    private async Task<IReadOnlyList<BusinessCardDto>> QueryBusinessesAsync(string? search, string? municipality,
        string? category, CancellationToken cancellationToken)
    {
        var query = from b in db.Businesses.AsNoTracking()
                    join m in db.Municipalities on b.MunicipalityId equals m.Id
                    join c in db.Categories on b.CategoryId equals c.Id
                    where b.Status == BusinessStatus.Active && b.IsPublished && m.IsActive && c.IsActive
                    select new { b, m, c };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search}%";
            query = query.Where(x => EF.Functions.ILike(x.b.Name, pattern));
        }
        if (!string.IsNullOrWhiteSpace(municipality)) query = query.Where(x => x.m.Slug == municipality);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.c.Slug == category);
        var rows = await query.OrderBy(x => x.b.Name).Select(x => new
        {
            x.b.Id, x.b.Slug, x.b.Name, x.b.Description, x.b.ShortDescription, x.b.Address,
            CategorySlug = x.c.Slug, CategoryName = x.c.Name,
            MunicipalitySlug = x.m.Slug, MunicipalityName = x.m.Name,
            HasQueue = db.BusinessModules.Any(m => m.BusinessId == x.b.Id && m.Module == BusinessModuleKind.VirtualQueues && m.IsEnabled),
            HasOrders = db.BusinessModules.Any(m => m.BusinessId == x.b.Id && m.Module == BusinessModuleKind.PickupOrders && m.IsEnabled),
            HasAppointments = db.BusinessModules.Any(m => m.BusinessId == x.b.Id && m.Module == BusinessModuleKind.Appointments && m.IsEnabled),
            Logo = db.BusinessImages.Where(i => i.BusinessId == x.b.Id && !i.IsDeleted && i.Kind == BusinessImageKind.Logo)
                .Select(i => new { i.StorageKey, i.AltText }).FirstOrDefault(),
            Cover = db.BusinessImages.Where(i => i.BusinessId == x.b.Id && !i.IsDeleted && i.Kind == BusinessImageKind.Cover)
                .Select(i => new { i.StorageKey, i.AltText }).FirstOrDefault(),
            // "Desde $25.000" contesta antes de entrar la pregunta que trae la mayoría. Nullable
            // a propósito: sin servicios con precio, Min sobre una lista vacía sería 0 y "desde $0"
            // afirmaría algo que no es cierto.
            PriceFrom = db.Services.Where(s => s.BusinessId == x.b.Id && s.IsActive)
                .Select(s => (decimal?)s.ReferencePrice).Min(),
            QueueIsOpen = db.QueueSessions.Any(q => q.BusinessId == x.b.Id && q.Status == QueueSessionStatus.Open),
            x.b.TimeZoneId,
            Hours = db.BusinessHours.Where(h => h.BusinessId == x.b.Id)
                .Select(h => new { h.Day, h.OpensAt, h.ClosesAt }).ToList()
        }).ToListAsync(cancellationToken);
        return rows.Select(x => new BusinessCardDto(x.Slug, x.Name,
            new(x.CategorySlug, x.CategoryName), new(x.MunicipalitySlug, x.MunicipalityName),
            x.Description, x.Address, x.HasQueue, x.HasOrders, x.HasAppointments,
            x.Logo is null ? null : storage.PublicUrl(x.Logo.StorageKey),
            x.Cover is null ? null : storage.PublicUrl(x.Cover.StorageKey),
            x.Logo?.AltText, x.Cover?.AltText, x.ShortDescription,
            x.HasAppointments ? x.PriceFrom : null,
            x.HasQueue && x.QueueIsOpen,
            OpenStatus(x.TimeZoneId, x.Hours
                .Select(h => new BusinessHourDto(h.Day, h.OpensAt.ToString("HH:mm"), h.ClosesAt.ToString("HH:mm")))
                .ToList()))).ToList();
    }

    /// <summary>
    /// Categorías con negocios publicados, de la más poblada a la menos. Se calcula sobre el mismo
    /// filtro que el directorio —activo, publicado, municipio y categoría vigentes— para que el
    /// conteo que se promete coincida con lo que aparece al entrar.
    /// </summary>
    public Task<IReadOnlyList<CategoryCardDto>> FindCategoriesAsync(string? municipality,
        CancellationToken cancellationToken)
        => publicCache.GetOrCreateAsync($"categorias|{municipality}",
            ct => QueryCategoriesAsync(municipality, ct), cancellationToken);

    private async Task<IReadOnlyList<CategoryCardDto>> QueryCategoriesAsync(string? municipality,
        CancellationToken cancellationToken)
    {
        var query = from b in db.Businesses.AsNoTracking()
                    join m in db.Municipalities on b.MunicipalityId equals m.Id
                    join c in db.Categories on b.CategoryId equals c.Id
                    where b.Status == BusinessStatus.Active && b.IsPublished && m.IsActive && c.IsActive
                    select new { b, m, c };
        if (!string.IsNullOrWhiteSpace(municipality)) query = query.Where(x => x.m.Slug == municipality);
        var rows = await query.GroupBy(x => new { x.c.Slug, x.c.Name })
            .Select(g => new { g.Key.Slug, g.Key.Name, Count = g.Count() })
            .ToListAsync(cancellationToken);
        return rows.OrderByDescending(x => x.Count).ThenBy(x => x.Name)
            .Select(x => new CategoryCardDto(x.Slug, x.Name, x.Count)).ToList();
    }

    public Task<BusinessProfileDto?> GetBusinessProfileAsync(string slug, bool requirePublished,
        CancellationToken cancellationToken)
        // Sólo se cachea la ficha pública. La vista previa administrativa (requirePublished: false)
        // debe reflejar cambios sin espera, así que siempre consulta la base.
        => requirePublished
            ? publicCache.GetOrCreateAsync($"ficha|{slug}",
                ct => QueryBusinessProfileAsync(slug, true, ct), cancellationToken)
            : QueryBusinessProfileAsync(slug, false, cancellationToken);

    private async Task<BusinessProfileDto?> QueryBusinessProfileAsync(string slug, bool requirePublished,
        CancellationToken cancellationToken)
    {
        // Una sola sentencia: la ficha pública costaba ocho idas y vueltas secuenciales y la
        // base vive en otra región. Se proyectan valores crudos y se formatean en memoria para
        // que todo sea traducible a SQL.
        var data = await (from b in db.Businesses.AsNoTracking()
                          join m in db.Municipalities on b.MunicipalityId equals m.Id
                          join c in db.Categories on b.CategoryId equals c.Id
                          where b.Slug == slug &&
                                (!requirePublished || (b.Status == BusinessStatus.Active && b.IsPublished))
                          select new
                          {
                              b,
                              MunicipalitySlug = m.Slug, MunicipalityName = m.Name,
                              CategorySlug = c.Slug, CategoryName = c.Name,
                              Modules = db.BusinessModules.Where(x => x.BusinessId == b.Id && x.IsEnabled)
                                  .Select(x => x.Module).ToList(),
                              Hours = db.BusinessHours.Where(x => x.BusinessId == b.Id)
                                  .OrderBy(x => x.Day)
                                  .Select(x => new { x.Day, x.OpensAt, x.ClosesAt }).ToList(),
                              Services = db.Services.Where(x => x.BusinessId == b.Id && x.IsActive)
                                  .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                                  .Select(x => new
                                  {
                                      x.Id, x.Name, x.Description, x.DurationMinutes, x.ReferencePrice,
                                      x.DisplayOrder, x.IsActive, x.Version, x.RequiresDeposit, x.DepositType,
                                      x.DepositValue, x.DepositInstructions
                                  }).ToList(),
                              Images = db.BusinessImages.Where(x => x.BusinessId == b.Id && !x.IsDeleted)
                                  .OrderBy(x => x.Kind).ThenBy(x => x.DisplayOrder)
                                  .Select(x => new { x.Id, x.Kind, x.StorageKey, x.AltText, x.Width, x.Height,
                                      x.DisplayOrder, x.Version, x.ServiceId, x.ProductId })
                                  .ToList(),
                          }).SingleOrDefaultAsync(cancellationToken);
        if (data is null) return null;
        // Ordenado por día y hora de apertura: una jornada partida se lee en el orden en que
        // ocurre, y quien consuma la API no depende del orden en que se guardaron los tramos.
        var publicHours = data.Hours
            .OrderBy(x => x.Day).ThenBy(x => x.OpensAt)
            .Select(x => new BusinessHourDto(x.Day, x.OpensAt.ToString("HH:mm"), x.ClosesAt.ToString("HH:mm")))
            .ToList();
        var hasAppointments = data.Modules.Contains(BusinessModuleKind.Appointments);
        // El adelanto se muestra antes de reservar, ya calculado. El WhatsApp del negocio no viaja
        // aquí: sólo hace falta después de crear la cita, y entonces sale de la copia congelada.
        // La foto de cada servicio se resuelve una vez y se busca por identificador: recorrer la
        // lista de imágenes por cada servicio convertía una ficha con quince servicios en quince
        // recorridos completos.
        var serviceImages = data.Images.Where(x => x.Kind == BusinessImageKind.Service && x.ServiceId is not null)
            .ToDictionary(x => x.ServiceId!.Value);
        var services = data.Services.Select(x =>
        {
            var policy = x.RequiresDeposit
                ? new DepositPolicy(true, x.DepositType, x.DepositValue, x.DepositInstructions, "")
                : DepositPolicy.None;
            var photo = serviceImages.GetValueOrDefault(x.Id);
            return new ServiceDto(x.Id, x.Name, x.Description, x.DurationMinutes, x.ReferencePrice,
                x.DisplayOrder, x.IsActive, 0, x.Version, x.RequiresDeposit, x.DepositType.ToString(),
                x.DepositValue, policy.CalculateFor(x.ReferencePrice), x.DepositInstructions, "",
                photo is null ? null : storage.PublicUrl(photo.StorageKey), photo?.AltText);
        }).ToList();
        // La galería de la ficha muestra el establecimiento; las fotos del catálogo ya viajan dentro
        // de su servicio o de su producto y repetirlas aquí llenaría la galería de primeros planos.
        var images = data.Images
            .Where(x => x.Kind is BusinessImageKind.Logo or BusinessImageKind.Cover or BusinessImageKind.Gallery)
            .Select(x => new BusinessImageDto(x.Id, x.Kind.ToString(),
                storage.PublicUrl(x.StorageKey), x.AltText, x.Width, x.Height, x.DisplayOrder, x.Version)).ToList();
        return new(data.b.Slug, data.b.Name, data.b.Description, data.b.Address, data.b.PublicPhone,
            new(data.CategorySlug, data.CategoryName), new(data.MunicipalitySlug, data.MunicipalityName),
            publicHours, hasAppointments ? services : [],
            data.Modules.Contains(BusinessModuleKind.VirtualQueues),
            data.Modules.Contains(BusinessModuleKind.PickupOrders),
            data.b.ShortDescription, data.b.ReferencePoint, data.b.WhatsAppUrl, data.b.PublicEmail,
            data.b.InstagramUrl, data.b.FacebookUrl, data.b.LocationUrl, data.b.CustomerInstructions,
            images,
            OpenStatus(data.b.TimeZoneId, publicHours));
    }

    /// <summary>
    /// Estado según el horario publicado y la zona del negocio. Devuelve null cuando no hay horario
    /// cargado, para no mostrar un estado que no se puede calcular. Con jornada partida distingue
    /// la pausa del cierre del día: a las 13:00, entre 08:00–12:00 y 14:00–18:00, el negocio no
    /// está simplemente "Cerrado", sino cerrado hasta que reabre por la tarde.
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
        var now = TimeOnly.FromDateTime(local.DateTime);
        if (BusinessSchedule.IntervalAt(today, now) is not null) return "Abierto";
        var next = BusinessSchedule.NextInterval(today, now);
        return next is null ? "Cerrado" : $"Cerrado temporalmente · abre a las {Display(next.Value.OpensAt)}";
    }

    /// <summary>Hora en el formato coloquial colombiano: "2:00 p. m.".</summary>
    private static string Display(TimeOnly value)
        => value.ToString("h:mm tt", System.Globalization.CultureInfo.GetCultureInfo("es-CO"));

    public async Task<SchedulingContext?> GetSchedulingContextAsync(string slug, Guid serviceId, DateOnly date,
        CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug &&
            x.Status == BusinessStatus.Active && x.IsPublished, cancellationToken);
        if (business is null) return null;
        if (!await db.BusinessModules.AnyAsync(x => x.BusinessId == business.Id &&
            x.Module == BusinessModuleKind.Appointments && x.IsEnabled, cancellationToken)) return null;
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x =>
            x.BusinessId == business.Id && x.Id == serviceId, cancellationToken);
        if (service is null) return null;
        var staff = await (from s in db.StaffMembers.AsNoTracking()
                           join ss in db.StaffServices.AsNoTracking()
                               on new { s.BusinessId, StaffMemberId = s.Id } equals new { ss.BusinessId, ss.StaffMemberId }
                           where s.BusinessId == business.Id && ss.ServiceId == serviceId && s.IsActive
                               && s.ParticipatesInAvailability
                           select s).ToListAsync(cancellationToken);
        var staffIds = staff.Select(x => x.Id).ToArray();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
        var dayEnd = dayStart.AddDays(1);
        var occupied = await db.Appointments.AsNoTracking().Where(x => x.BusinessId == business.Id &&
                staffIds.Contains(x.StaffMemberId) && x.StartAtUtc < dayEnd && x.EndAtUtc > dayStart &&
                (x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed))
            .Select(x => new ValueTuple<DateTimeOffset, DateTimeOffset, Guid>(x.StartAtUtc, x.EndAtUtc, x.StaffMemberId))
            .ToListAsync(cancellationToken);
        var hours = await db.BusinessHours.AsNoTracking().Where(x => x.BusinessId == business.Id).ToListAsync(cancellationToken);
        var exceptions = await db.AvailabilityExceptions.AsNoTracking().Where(x =>
            x.BusinessId == business.Id && staffIds.Contains(x.StaffMemberId) && x.Date == date).ToListAsync(cancellationToken);
        return new(business, service, hours, staff, exceptions, occupied);
    }

    public async Task<bool> AddAppointmentAsync(Appointment appointment, ConsentReceipt consent,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken);
        db.ConsentReceipts.Add(consent); db.Appointments.Add(appointment);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg &&
            pg.SqlState is PostgresErrorCodes.ExclusionViolation or PostgresErrorCodes.UniqueViolation)
        {
            await tx.RollbackAsync(cancellationToken);
            return false;
        }
    }

    public async Task<AppointmentRecord?> FindAppointmentByCodeHashAsync(string codeHash, CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.PublicCodeHash == codeHash, cancellationToken);
        return appointment is null ? null : await BuildRecord(appointment, cancellationToken);
    }
    public Task<bool> IsMemberAsync(Guid userId, Guid businessId, CancellationToken cancellationToken)
        => db.BusinessMemberships.AsNoTracking().AnyAsync(x => x.UserId == userId && x.BusinessId == businessId && x.IsActive, cancellationToken);
    public Task<bool> CanManageConfigurationAsync(Guid userId, Guid businessId, CancellationToken cancellationToken)
        => db.BusinessMemberships.AsNoTracking().AnyAsync(x => x.UserId == userId && x.BusinessId == businessId &&
            x.IsActive && (x.Role == MembershipRole.Owner || x.CanManageConfiguration), cancellationToken);
    public Task<bool> CanManageAppointmentsAsync(Guid userId, Guid businessId, CancellationToken cancellationToken)
        => db.BusinessMemberships.AsNoTracking().AnyAsync(x => x.UserId == userId && x.BusinessId == businessId &&
            x.IsActive && (x.Role == MembershipRole.Owner || x.CanManageAppointments), cancellationToken);
    public Task<bool> IsModuleEnabledAsync(Guid businessId, BusinessModuleKind module,
        CancellationToken cancellationToken)
        => db.BusinessModules.AsNoTracking()
            .AnyAsync(x => x.BusinessId == businessId && x.Module == module && x.IsEnabled, cancellationToken);

    public async Task<IReadOnlyList<MyBusinessDto>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken)
        // Un negocio archivado ya no se opera: listarlo en "Mis establecimientos" ofrecía accesos
        // que no conducen a nada. Se excluye aquí y no en la vista, para que ninguna otra pantalla
        // lo herede.
        => await (from membership in db.BusinessMemberships.AsNoTracking()
                  join business in db.Businesses.AsNoTracking() on membership.BusinessId equals business.Id
                  where membership.UserId == userId && membership.IsActive &&
                        business.Status != BusinessStatus.Archived
                  orderby business.Name
                  select new MyBusinessDto(business.Id, business.Name, business.Slug, membership.Role.ToString(),
                      membership.Role == MembershipRole.Owner || membership.CanManageConfiguration,
                      membership.Role == MembershipRole.Owner || membership.CanManageAppointments,
                      membership.Role == MembershipRole.Owner || membership.CanManageMembers,
                      membership.Role == MembershipRole.Owner || membership.CanManageQueues,
                      membership.Role == MembershipRole.Owner || membership.CanManageOrders,
                      db.PickupOrderSettings.Any(s => s.BusinessId == business.Id),
                      business.Status.ToString(),
                      db.BusinessModules.Any(m => m.BusinessId == business.Id &&
                          m.Module == BusinessModuleKind.Appointments && m.IsEnabled),
                      db.BusinessModules.Any(m => m.BusinessId == business.Id &&
                          m.Module == BusinessModuleKind.VirtualQueues && m.IsEnabled),
                      db.BusinessModules.Any(m => m.BusinessId == business.Id &&
                          m.Module == BusinessModuleKind.PickupOrders && m.IsEnabled)))
            .ToListAsync(cancellationToken);

    public async Task<AppointmentBoardRecord> GetAppointmentsAsync(Guid businessId, DateOnly? date,
        AppointmentStatus? status, CancellationToken cancellationToken)
    {
        // Todas las citas del listado pertenecen al mismo negocio, así que se lee una sola vez.
        var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId, cancellationToken);
        var query = db.Appointments.AsNoTracking().Where(x => x.BusinessId == businessId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (date.HasValue)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.Value.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
            var end = start.AddDays(1);
            query = query.Where(x => x.StartAtUtc >= start && x.StartAtUtc < end);
        }
        var appointments = await query.OrderByDescending(x => x.StartAtUtc).Take(200).ToListAsync(cancellationToken);
        // El negocio ya está leído arriba, así que un día sin citas no cuesta una consulta extra ni
        // deja a la pantalla sin saber de quién es la agenda que está mostrando.
        if (appointments.Count == 0) return new(business, []);
        // Los consentimientos se traen en bloque: uno por cita costaba una ida y vuelta por fila.
        var consentIds = appointments.Select(x => x.ConsentReceiptId).Distinct().ToList();
        var consents = await db.ConsentReceipts.AsNoTracking().Where(x => consentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var verifiers = await VerifierNamesAsync(appointments, cancellationToken);
        return new(business, appointments.Select(x => new AppointmentRecord(x, business,
            consents[x.ConsentReceiptId], Verifier(verifiers, x))).ToList());
    }

    /// <summary>
    /// Los nombres de quienes verificaron, en una sola consulta para todo el listado. Uno por fila
    /// era exactamente la regresión que hacía lenta la consola.
    /// </summary>
    private async Task<Dictionary<Guid, string>> VerifierNamesAsync(IEnumerable<Appointment> appointments,
        CancellationToken cancellationToken)
    {
        var ids = appointments.Where(x => x.DepositVerifiedByUserId.HasValue)
            .Select(x => x.DepositVerifiedByUserId!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Users.AsNoTracking().Where(x => ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.Email ?? "" : x.DisplayName,
                cancellationToken);
    }

    private static string? Verifier(Dictionary<Guid, string> names, Appointment appointment)
        => appointment.DepositVerifiedByUserId is { } id && names.TryGetValue(id, out var name) ? name : null;

    public async Task<AppointmentRecord?> GetAppointmentAsync(Guid businessId, Guid appointmentId,
        CancellationToken cancellationToken)
    {
        var appointment = await db.Appointments.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == appointmentId,
            cancellationToken);
        return appointment is null ? null : await BuildRecord(appointment, cancellationToken);
    }
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException("CONCURRENCY_CONFLICT",
                "La información cambió mientras la editaba. Recargue e intente de nuevo.", 409);
        }
        // Cualquier escritura por esta vía puede alterar lo que muestra el directorio o una ficha
        // (servicios, horarios, personal). Invalidar aquí, y no en cada caso de uso, evita que un
        // camino nuevo olvide hacerlo y deje información vencida a la vista del público.
        publicCache.Invalidate();
    }
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
    {
        // El adelanto calculado se resuelve en memoria: la regla de redondeo vive en el dominio y no
        // conviene reescribirla en SQL.
        var rows = await db.Services.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id, x.Name, x.Description, x.DurationMinutes, x.ReferencePrice, x.DisplayOrder, x.IsActive,
                x.Version, x.RequiresDeposit, x.DepositType, x.DepositValue, x.DepositInstructions,
                x.DepositWhatsAppNumber,
                Future = db.Appointments.Count(a => a.BusinessId == businessId &&
                    a.ServiceId == x.Id && a.StartAtUtc > nowUtc &&
                    (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)),
                // La foto viaja con el servicio para que el propietario vea desde el celular cuáles
                // ya tienen imagen y cuáles no, que es la lista que necesita para completarlas.
                Photo = db.BusinessImages
                    .Where(i => i.ServiceId == x.Id && !i.IsDeleted)
                    .Select(i => new { i.StorageKey, i.AltText }).FirstOrDefault()
            }).ToListAsync(cancellationToken);
        return rows.Select(x =>
        {
            var policy = x.RequiresDeposit
                ? new DepositPolicy(true, x.DepositType, x.DepositValue, x.DepositInstructions, x.DepositWhatsAppNumber)
                : DepositPolicy.None;
            return new ServiceDto(x.Id, x.Name, x.Description, x.DurationMinutes, x.ReferencePrice,
                x.DisplayOrder, x.IsActive, x.Future, x.Version, x.RequiresDeposit, x.DepositType.ToString(),
                x.DepositValue, policy.CalculateFor(x.ReferencePrice), x.DepositInstructions,
                x.DepositWhatsAppNumber,
                x.Photo is null ? null : storage.PublicUrl(x.Photo.StorageKey), x.Photo?.AltText);
        }).ToList();
    }
    public Task<Service?> GetServiceAsync(Guid businessId, Guid serviceId, CancellationToken cancellationToken)
        => db.Services.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == serviceId, cancellationToken);
    public void AddService(Service service) => db.Services.Add(service);

    public async Task<IReadOnlyList<StaffMemberDto>> GetStaffAsync(Guid businessId, CancellationToken cancellationToken)
    {
        var staff = await db.StaffMembers.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
        var links = await db.StaffServices.AsNoTracking().Where(x => x.BusinessId == businessId)
            .ToListAsync(cancellationToken);
        return staff.Select(x => new StaffMemberDto(x.Id, x.DisplayName, x.IsActive, x.ParticipatesInAvailability,
            links.Where(link => link.StaffMemberId == x.Id).Select(link => link.ServiceId).ToArray(), x.Version)).ToArray();
    }
    public Task<StaffMember?> GetStaffMemberAsync(Guid businessId, Guid staffId, CancellationToken cancellationToken)
        => db.StaffMembers.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Id == staffId, cancellationToken);
    public async Task<bool> SetStaffServicesAsync(Guid businessId, Guid staffId,
        IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken)
    {
        var validCount = await db.Services.CountAsync(x => x.BusinessId == businessId && serviceIds.Contains(x.Id),
            cancellationToken);
        if (validCount != serviceIds.Count) return false;
        var existing = await db.StaffServices.Where(x => x.BusinessId == businessId && x.StaffMemberId == staffId)
            .ToListAsync(cancellationToken);
        db.StaffServices.RemoveRange(existing);
        db.StaffServices.AddRange(serviceIds.Select(id => new StaffService(businessId, staffId, id)));
        return true;
    }
    public void AddStaffMember(StaffMember staff) => db.StaffMembers.Add(staff);
    public Task<BusinessHour?> GetBusinessHourAsync(Guid businessId, DayOfWeek day, CancellationToken cancellationToken)
        => db.BusinessHours.SingleOrDefaultAsync(x => x.BusinessId == businessId && x.Day == day, cancellationToken);
    public async Task<IReadOnlyList<BusinessHour>> GetBusinessHoursAsync(Guid businessId,
        CancellationToken cancellationToken)
        => await db.BusinessHours.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.Day).ToListAsync(cancellationToken);
    public void AddBusinessHour(BusinessHour hour) => db.BusinessHours.Add(hour);
    public void RemoveBusinessHour(BusinessHour hour) => db.BusinessHours.Remove(hour);
    public async Task<IReadOnlyList<AvailabilityException>> GetAvailabilityExceptionsAsync(Guid businessId,
        CancellationToken cancellationToken) => await db.AvailabilityExceptions.Where(x => x.BusinessId == businessId)
        .OrderBy(x => x.Date).ToListAsync(cancellationToken);
    public Task<AvailabilityException?> GetAvailabilityExceptionAsync(Guid businessId, Guid exceptionId,
        CancellationToken cancellationToken) => db.AvailabilityExceptions.SingleOrDefaultAsync(
        x => x.BusinessId == businessId && x.Id == exceptionId, cancellationToken);
    public Task<bool> StaffBelongsToBusinessAsync(Guid businessId, Guid staffId, CancellationToken cancellationToken)
        => db.StaffMembers.AsNoTracking().AnyAsync(x => x.BusinessId == businessId && x.Id == staffId, cancellationToken);
    public async Task<int> CountFutureAppointmentConflictsAsync(Guid businessId, Guid? staffId, DateOnly date,
        TimeOnly? startsAt, TimeOnly? endsAt, bool conflictsOutsideInterval, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId, cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
        var dayStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            date.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
        var dayEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
            date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
        var query = db.Appointments.AsNoTracking().Where(x => x.BusinessId == businessId &&
            x.StartAtUtc < dayEnd && x.EndAtUtc > dayStart &&
            (x.Status == AppointmentStatus.Pending || x.Status == AppointmentStatus.Confirmed));
        if (staffId.HasValue) query = query.Where(x => x.StaffMemberId == staffId.Value);
        if (startsAt.HasValue && endsAt.HasValue)
        {
            var intervalStart = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                date.ToDateTime(startsAt.Value), zone), TimeSpan.Zero);
            var intervalEnd = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(
                date.ToDateTime(endsAt.Value), zone), TimeSpan.Zero);
            query = conflictsOutsideInterval
                ? query.Where(x => x.StartAtUtc < intervalStart || x.EndAtUtc > intervalEnd)
                : query.Where(x => x.StartAtUtc < intervalEnd && x.EndAtUtc > intervalStart);
        }
        return await query.CountAsync(cancellationToken);
    }
    public void AddAvailabilityException(AvailabilityException exception) => db.AvailabilityExceptions.Add(exception);
    public void RemoveAvailabilityException(AvailabilityException exception) => db.AvailabilityExceptions.Remove(exception);

    private async Task<AppointmentRecord> BuildRecord(Appointment appointment, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == appointment.BusinessId, cancellationToken);
        var consent = await db.ConsentReceipts.AsNoTracking().SingleAsync(x => x.Id == appointment.ConsentReceiptId, cancellationToken);
        var verifiers = await VerifierNamesAsync([appointment], cancellationToken);
        return new(appointment, business, consent, Verifier(verifiers, appointment));
    }

    public void AddDepositAudit(AppointmentDepositAudit entry) => db.AppointmentDepositAudits.Add(entry);

    public async Task<IReadOnlyList<AppointmentDepositAuditDto>> ListDepositAuditAsync(Guid appointmentId,
        CancellationToken cancellationToken)
        => await db.AppointmentDepositAudits.AsNoTracking().Where(x => x.AppointmentId == appointmentId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Select(x => new AppointmentDepositAuditDto(x.Id, x.AppointmentId, x.ActorKind.ToString(),
                x.ActorUserId, x.PreviousStatus.ToString(), x.NewStatus.ToString(), x.OccurredAtUtc, x.Reason))
            .ToListAsync(cancellationToken);
}
