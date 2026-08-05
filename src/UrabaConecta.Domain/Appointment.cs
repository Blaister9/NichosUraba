namespace UrabaConecta.Domain;

public sealed class Appointment : IBusinessOwned
{
    private Appointment() { }

    public Appointment(Guid id, Guid businessId, Guid serviceId, Guid staffMemberId,
        DateTimeOffset startAtUtc, int durationMinutes, string serviceName, decimal displayPrice,
        string protectedAlias, string protectedPhone, string phoneLast4, string protectedNotes,
        string publicCodeHash, int publicCodeVersion, Guid consentReceiptId, DateTimeOffset nowUtc,
        DepositPolicy? deposit = null)
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
        FreezeDeposit(deposit ?? DepositPolicy.None);
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

    /// <summary>
    /// Copia congelada del adelanto tal como estaba al crear la cita. Editar el servicio después
    /// cambia lo que se cobrará a las citas nuevas, nunca lo pactado en las que ya existen.
    /// </summary>
    public DepositStatus DepositStatus { get; private set; } = DepositStatus.NotRequired;
    public DepositType DepositType { get; private set; } = DepositType.None;
    /// <summary>El valor configurado: pesos si es fijo, porcentaje si es porcentual.</summary>
    public decimal DepositConfiguredValue { get; private set; }
    /// <summary>El adelanto ya calculado en pesos enteros.</summary>
    public decimal DepositAmount { get; private set; }
    public string DepositInstructions { get; private set; } = "";
    public string DepositWhatsAppNumber { get; private set; } = "";
    public DateTimeOffset? DepositReportedAtUtc { get; private set; }
    public DateTimeOffset? DepositVerifiedAtUtc { get; private set; }
    public Guid? DepositVerifiedByUserId { get; private set; }
    public string? DepositRejectionReason { get; private set; }

    public bool RequiresDeposit => DepositStatus != DepositStatus.NotRequired;

    public void ChangeStatus(AppointmentStatus target, DateTimeOffset nowUtc, string? reason = null)
    {
        var valid = Status switch
        {
            AppointmentStatus.Pending => target is AppointmentStatus.Confirmed or AppointmentStatus.Rejected or AppointmentStatus.Cancelled,
            AppointmentStatus.Confirmed => target is AppointmentStatus.Completed or AppointmentStatus.NoShow or AppointmentStatus.Cancelled,
            _ => false
        };
        if (!valid) throw new DomainException("INVALID_STATE_TRANSITION", $"No se puede pasar de {Status} a {target}.");
        // Confirmar es el compromiso del negocio con el horario, así que espera al adelanto. Cancelar
        // no: una cita sin adelanto pagado tiene que poder soltarse.
        if (target == AppointmentStatus.Confirmed && RequiresDeposit && DepositStatus != DepositStatus.Verified)
            throw new DomainException("DEPOSIT_NOT_VERIFIED",
                "Verifique el adelanto antes de confirmar la cita.");
        Status = target;
        RejectionReason = target == AppointmentStatus.Rejected ? reason?.Trim() : null;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>
    /// Se avisa que el comprobante fue enviado. Lo usa el cliente desde su seguimiento —es lo único
    /// que puede hacer— y también el negocio cuando el comprobante le llegó por otra vía.
    /// </summary>
    public void ReportDeposit(DateTimeOffset nowUtc) => MoveDeposit(DepositStatus.Reported,
        from => from is DepositStatus.Pending or DepositStatus.Rejected, nowUtc);

    public void VerifyDeposit(Guid actorUserId, DateTimeOffset nowUtc)
    {
        MoveDeposit(DepositStatus.Verified,
            from => from is DepositStatus.Pending or DepositStatus.Reported or DepositStatus.Rejected, nowUtc);
        DepositVerifiedByUserId = actorUserId;
        DepositVerifiedAtUtc = nowUtc;
        DepositRejectionReason = null;
    }

    public void RejectDeposit(DateTimeOffset nowUtc, string? reason)
    {
        MoveDeposit(DepositStatus.Rejected,
            from => from is DepositStatus.Pending or DepositStatus.Reported, nowUtc);
        DepositRejectionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        // Quién rechazó queda en la auditoría; "verificado por" describe sólo una verificación.
        DepositVerifiedByUserId = null;
        DepositVerifiedAtUtc = null;
    }

    /// <summary>Devuelve un comprobante rechazado a la espera de uno nuevo.</summary>
    public void ReopenDeposit(DateTimeOffset nowUtc)
    {
        MoveDeposit(DepositStatus.Pending, from => from is DepositStatus.Rejected, nowUtc);
        DepositReportedAtUtc = null;
        DepositRejectionReason = null;
    }

    /// <summary>
    /// Deshacer una verificación. Sólo la administración de la plataforma: para el negocio, un
    /// adelanto verificado es definitivo.
    /// </summary>
    public void RevertDepositVerification(DateTimeOffset nowUtc)
    {
        if (Status == AppointmentStatus.Confirmed)
            throw new DomainException("APPOINTMENT_ALREADY_CONFIRMED",
                "La cita ya está confirmada: no se puede deshacer la verificación del adelanto.");
        MoveDeposit(DepositStatus.Pending, from => from is DepositStatus.Verified, nowUtc);
        DepositVerifiedAtUtc = null;
        DepositVerifiedByUserId = null;
        DepositReportedAtUtc = null;
    }

    private void MoveDeposit(DepositStatus target, Func<DepositStatus, bool> allowed, DateTimeOffset nowUtc)
    {
        if (DepositStatus == DepositStatus.NotRequired)
            throw new DomainException("DEPOSIT_NOT_REQUIRED", "Esta cita no exige adelanto.");
        if (!allowed(DepositStatus))
            throw new DomainException("INVALID_DEPOSIT_TRANSITION",
                $"No se puede pasar el adelanto de {DepositStatus} a {target}.");
        if (target == DepositStatus.Reported) DepositReportedAtUtc = nowUtc;
        DepositStatus = target;
        UpdatedAtUtc = nowUtc;
    }

    private void FreezeDeposit(DepositPolicy policy)
    {
        DepositType = policy.Type;
        DepositConfiguredValue = policy.RequiresDeposit ? policy.Value : 0m;
        DepositAmount = policy.CalculateFor(DisplayPrice);
        DepositInstructions = policy.Instructions;
        DepositWhatsAppNumber = policy.WhatsAppNumber;
        DepositStatus = policy.RequiresDeposit ? DepositStatus.Pending : DepositStatus.NotRequired;
    }
}
