namespace UrabaConecta.Domain;

public sealed class Appointment : IBusinessOwned
{
    private Appointment() { }

    public Appointment(Guid id, Guid businessId, Guid serviceId, Guid staffMemberId,
        DateTimeOffset startAtUtc, int durationMinutes, string serviceName, decimal displayPrice,
        string protectedAlias, string protectedPhone, string phoneLast4, string protectedNotes,
        string publicCodeHash, int publicCodeVersion, Guid consentReceiptId, DateTimeOffset nowUtc)
    {
        if (startAtUtc <= nowUtc) throw new DomainException("APPOINTMENT_IN_PAST", "La cita debe ser futura.");
        if (durationMinutes is < 5 or > 480) throw new DomainException("INVALID_DURATION", "Duración inválida.");
        if (string.IsNullOrWhiteSpace(protectedAlias) || string.IsNullOrWhiteSpace(protectedPhone))
            throw new DomainException("CONTACT_REQUIRED", "Alias y teléfono son obligatorios.");
        if (consentReceiptId == Guid.Empty) throw new DomainException("CONSENT_REQUIRED", "El consentimiento es obligatorio.");

        Id = id; BusinessId = businessId; ServiceId = serviceId; StaffMemberId = staffMemberId;
        StartAtUtc = startAtUtc.ToUniversalTime(); EndAtUtc = StartAtUtc.AddMinutes(durationMinutes);
        DurationMinutes = durationMinutes; ServiceName = serviceName; DisplayPrice = displayPrice;
        ProtectedCustomerAlias = protectedAlias; ProtectedPhone = protectedPhone; PhoneLast4 = phoneLast4;
        ProtectedNotes = protectedNotes; PublicCodeHash = publicCodeHash; PublicCodeVersion = publicCodeVersion;
        ConsentReceiptId = consentReceiptId; CreatedAtUtc = nowUtc; UpdatedAtUtc = nowUtc;
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid ServiceId { get; private set; }
    public Guid StaffMemberId { get; private set; }
    public DateTimeOffset StartAtUtc { get; private set; }
    public DateTimeOffset EndAtUtc { get; private set; }
    public string ServiceName { get; private set; } = "";
    public int DurationMinutes { get; private set; }
    public decimal DisplayPrice { get; private set; }
    public string ProtectedCustomerAlias { get; private set; } = "";
    public string ProtectedPhone { get; private set; } = "";
    public string PhoneLast4 { get; private set; } = "";
    public string ProtectedNotes { get; private set; } = "";
    public string PublicCodeHash { get; private set; } = "";
    public int PublicCodeVersion { get; private set; }
    public Guid ConsentReceiptId { get; private set; }
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Pending;
    public string? RejectionReason { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public uint Version { get; private set; }

    public void ChangeStatus(AppointmentStatus target, DateTimeOffset nowUtc, string? reason = null)
    {
        var valid = Status switch
        {
            AppointmentStatus.Pending => target is AppointmentStatus.Confirmed or AppointmentStatus.Rejected or AppointmentStatus.Cancelled,
            AppointmentStatus.Confirmed => target is AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.Cancelled,
            _ => false
        };
        if (!valid) throw new DomainException("INVALID_STATE_TRANSITION", $"No se puede pasar de {Status} a {target}.");
        Status = target;
        RejectionReason = target == AppointmentStatus.Rejected ? reason?.Trim() : null;
        UpdatedAtUtc = nowUtc;
    }
}
