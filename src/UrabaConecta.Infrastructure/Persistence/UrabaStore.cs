using Microsoft.EntityFrameworkCore;
using Npgsql;
using UrabaConecta.Application;
using UrabaConecta.Contracts;
using UrabaConecta.Domain;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class UrabaStore(AppDbContext db) : IUrabaStore
{
    public async Task<IReadOnlyList<BusinessCardDto>> FindBusinessesAsync(string? search, string? municipality,
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
        return await query.OrderBy(x => x.b.Name).Select(x => new BusinessCardDto(x.b.Slug, x.b.Name,
            new(x.c.Slug, x.c.Name), new(x.m.Slug, x.m.Name), x.b.Description, x.b.Address,
            db.QueueDefinitions.Any(q => q.BusinessId == x.b.Id && q.IsActive && q.IsEnabled),
            db.PickupOrderSettings.Any(s => s.BusinessId == x.b.Id && s.IsEnabled)))
            .ToListAsync(cancellationToken);
    }

    public async Task<BusinessProfileDto?> GetBusinessProfileAsync(string slug, CancellationToken cancellationToken)
    {
        var data = await (from b in db.Businesses.AsNoTracking()
                          join m in db.Municipalities on b.MunicipalityId equals m.Id
                          join c in db.Categories on b.CategoryId equals c.Id
                          where b.Slug == slug && b.Status == BusinessStatus.Active && b.IsPublished
                          select new { b, m, c }).SingleOrDefaultAsync(cancellationToken);
        if (data is null) return null;
        var hours = await db.BusinessHours.AsNoTracking().Where(x => x.BusinessId == data.b.Id)
            .OrderBy(x => x.Day).Select(x => new BusinessHourDto(x.Day, x.OpensAt.ToString("HH:mm"), x.ClosesAt.ToString("HH:mm")))
            .ToListAsync(cancellationToken);
        var services = await db.Services.AsNoTracking().Where(x => x.BusinessId == data.b.Id && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new ServiceDto(x.Id, x.Name, x.Description, x.DurationMinutes, x.ReferencePrice,
                x.DisplayOrder, x.IsActive, 0, x.Version))
            .ToListAsync(cancellationToken);
        return new(data.b.Slug, data.b.Name, data.b.Description, data.b.Address, data.b.PublicPhone,
            new(data.c.Slug, data.c.Name), new(data.m.Slug, data.m.Name), hours, services,
            await db.QueueDefinitions.AnyAsync(q => q.BusinessId == data.b.Id && q.IsActive && q.IsEnabled,
                cancellationToken),
            await db.PickupOrderSettings.AnyAsync(s => s.BusinessId == data.b.Id && s.IsEnabled,
                cancellationToken));
    }

    public async Task<SchedulingContext?> GetSchedulingContextAsync(string slug, Guid serviceId, DateOnly date,
        CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug &&
            x.Status == BusinessStatus.Active && x.IsPublished, cancellationToken);
        if (business is null) return null;
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

    public async Task<IReadOnlyList<MyBusinessDto>> GetMembershipsAsync(Guid userId, CancellationToken cancellationToken)
        => await (from membership in db.BusinessMemberships.AsNoTracking()
                  join business in db.Businesses.AsNoTracking() on membership.BusinessId equals business.Id
                  where membership.UserId == userId && membership.IsActive
                  orderby business.Name
                  select new MyBusinessDto(business.Id, business.Name, business.Slug, membership.Role.ToString(),
                      membership.Role == MembershipRole.Owner || membership.CanManageConfiguration,
                      membership.Role == MembershipRole.Owner || membership.CanManageAppointments,
                      membership.Role == MembershipRole.Owner || membership.CanManageMembers,
                      membership.Role == MembershipRole.Owner || membership.CanManageQueues,
                      membership.Role == MembershipRole.Owner || membership.CanManageOrders,
                      db.PickupOrderSettings.Any(s => s.BusinessId == business.Id)))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<AppointmentRecord>> GetAppointmentsAsync(Guid businessId, DateOnly? date,
        AppointmentStatus? status, CancellationToken cancellationToken)
    {
        var query = db.Appointments.AsNoTracking().Where(x => x.BusinessId == businessId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (date.HasValue)
        {
            var business = await db.Businesses.AsNoTracking().SingleAsync(x => x.Id == businessId, cancellationToken);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(business.TimeZoneId);
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(date.Value.ToDateTime(TimeOnly.MinValue), zone), TimeSpan.Zero);
            var end = start.AddDays(1);
            query = query.Where(x => x.StartAtUtc >= start && x.StartAtUtc < end);
        }
        var appointments = await query.OrderBy(x => x.StartAtUtc).ToListAsync(cancellationToken);
        var records = new List<AppointmentRecord>();
        foreach (var appointment in appointments) records.Add(await BuildRecord(appointment, cancellationToken));
        return records;
    }

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
    }
    public async Task<IReadOnlyList<ServiceDto>> GetServicesAsync(Guid businessId, DateTimeOffset nowUtc,
        CancellationToken cancellationToken)
        => await db.Services.AsNoTracking().Where(x => x.BusinessId == businessId)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
            .Select(x => new ServiceDto(x.Id, x.Name, x.Description, x.DurationMinutes, x.ReferencePrice,
                x.DisplayOrder, x.IsActive, db.Appointments.Count(a => a.BusinessId == businessId &&
                    a.ServiceId == x.Id && a.StartAtUtc > nowUtc &&
                    (a.Status == AppointmentStatus.Pending || a.Status == AppointmentStatus.Confirmed)), x.Version))
            .ToListAsync(cancellationToken);
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
        return new(appointment, business, consent);
    }
}
