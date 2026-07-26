namespace UrabaConecta.Domain;

public enum QueueSessionStatus { Closed, Open, Paused }
public enum QueueTicketStatus { Waiting, Called, InService, Completed, Skipped, Cancelled }
public enum QueueTicketSource { Online, WalkIn }

public sealed class QueueDefinition : IBusinessOwned
{
    private QueueDefinition() { }
    public QueueDefinition(Guid id, Guid businessId, string name, int averageDurationMinutes,
        int maximumWaiting, string? publicMessage, bool isEnabled = true,
        DateTimeOffset? now = null)
    {
        Id = id;
        BusinessId = businessId;
        CreatedAtUtc = UpdatedAtUtc = now ?? DateTimeOffset.UtcNow;
        Apply(name, averageDurationMinutes, maximumWaiting, publicMessage, isEnabled);
    }

    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public string Name { get; private set; } = "";
    public int AverageDurationMinutes { get; private set; }
    public int MaximumWaiting { get; private set; }
    public string PublicMessage { get; private set; } = "";
    public bool IsEnabled { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Update(string name, int averageDurationMinutes, int maximumWaiting,
        string? publicMessage, bool isEnabled, DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        Apply(name, averageDurationMinutes, maximumWaiting, publicMessage, isEnabled);
        UpdatedAtUtc = now;
        Version++;
    }

    private void Apply(string name, int duration, int maximum, string? message, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80)
            throw new DomainException("INVALID_QUEUE_DEFINITION", "El nombre debe tener entre 1 y 80 caracteres.");
        if (duration is < 1 or > 480)
            throw new DomainException("INVALID_QUEUE_DURATION", "La duración promedio debe estar entre 1 y 480 minutos.");
        if (maximum is < 1 or > 500)
            throw new DomainException("INVALID_QUEUE_CAPACITY", "La capacidad debe estar entre 1 y 500 turnos.");
        if ((message?.Trim().Length ?? 0) > 160)
            throw new DomainException("INVALID_QUEUE_MESSAGE", "El mensaje público puede tener máximo 160 caracteres.");
        Name = name.Trim();
        AverageDurationMinutes = duration;
        MaximumWaiting = maximum;
        PublicMessage = message?.Trim() ?? "";
        IsEnabled = enabled;
    }

    private void EnsureVersion(long expected)
    {
        if (Version != expected)
            throw new DomainException("CONCURRENCY_CONFLICT", "La configuración de turnos cambió. Recargue la información.");
    }
}

public sealed class QueueSession : IBusinessOwned
{
    private QueueSession() { }
    public QueueSession(Guid id, Guid businessId, Guid queueDefinitionId, DateTimeOffset openedAtUtc)
    {
        Id = id; BusinessId = businessId; QueueDefinitionId = queueDefinitionId;
        OpenedAtUtc = openedAtUtc; Status = QueueSessionStatus.Open; NextNumber = 1;
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid QueueDefinitionId { get; private set; }
    public QueueSessionStatus Status { get; private set; }
    public int NextNumber { get; private set; }
    public DateTimeOffset OpenedAtUtc { get; private set; }
    public DateTimeOffset? PausedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public long Version { get; private set; }

    public int AllocateNumber(long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        EnsureOpen();
        var number = NextNumber++;
        Version++;
        return number;
    }
    public void Pause(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion); EnsureStatus(QueueSessionStatus.Open);
        Status = QueueSessionStatus.Paused; PausedAtUtc = now; Version++;
    }
    public void Resume(long expectedVersion)
    {
        EnsureVersion(expectedVersion); EnsureStatus(QueueSessionStatus.Paused);
        Status = QueueSessionStatus.Open; PausedAtUtc = null; Version++;
    }
    public void Close(DateTimeOffset now, int activeTickets, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status == QueueSessionStatus.Closed)
            throw new DomainException("QUEUE_ALREADY_CLOSED", "La jornada ya está cerrada.");
        if (activeTickets > 0)
            throw new DomainException("QUEUE_HAS_ACTIVE_TICKETS", "No puede cerrar mientras existan turnos activos.");
        Status = QueueSessionStatus.Closed; ClosedAtUtc = now; PausedAtUtc = null; Version++;
    }
    public void Touch(long expectedVersion) { EnsureVersion(expectedVersion); Version++; }
    private void EnsureOpen()
    {
        if (Status != QueueSessionStatus.Open)
            throw new DomainException("QUEUE_NOT_OPEN", "La fila no está abierta para recibir turnos.");
    }
    private void EnsureStatus(QueueSessionStatus expected)
    {
        if (Status != expected)
            throw new DomainException("INVALID_QUEUE_SESSION_TRANSITION", "La jornada no permite esta acción.");
    }
    private void EnsureVersion(long expected)
    {
        if (Version != expected)
            throw new DomainException("CONCURRENCY_CONFLICT", "La fila cambió. Recargue la información.");
    }
}

public sealed class QueueTicket : IBusinessOwned
{
    private QueueTicket() { }
    public QueueTicket(Guid id, Guid businessId, Guid queueSessionId, int number,
        string publicCodeHash, string? protectedAlias, QueueTicketSource source, DateTimeOffset now)
    {
        if (number < 1 || string.IsNullOrWhiteSpace(publicCodeHash))
            throw new DomainException("INVALID_QUEUE_TICKET", "El turno no es válido.");
        Id = id; BusinessId = businessId; QueueSessionId = queueSessionId; Number = number;
        PublicCodeHash = publicCodeHash; ProtectedAlias = protectedAlias;
        Source = source; Status = QueueTicketStatus.Waiting; CreatedAtUtc = UpdatedAtUtc = now;
    }
    public Guid Id { get; private set; }
    public Guid BusinessId { get; private set; }
    public Guid QueueSessionId { get; private set; }
    public int Number { get; private set; }
    public string PublicCodeHash { get; private set; } = "";
    public int CodeVersion { get; private set; } = 1;
    public string? ProtectedAlias { get; private set; }
    public QueueTicketSource Source { get; private set; }
    public QueueTicketStatus Status { get; private set; }
    public int RestoreCount { get; private set; }
    public int CallCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? CalledAtUtc { get; private set; }
    public DateTimeOffset? ServiceStartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Version { get; private set; }

    public void Call(DateTimeOffset now, long expectedVersion)
    {
        Ensure(expectedVersion, QueueTicketStatus.Waiting);
        Status = QueueTicketStatus.Called; CalledAtUtc = now; CallCount++; Touch(now);
    }
    public void Recall(DateTimeOffset now, long expectedVersion)
    {
        Ensure(expectedVersion, QueueTicketStatus.Called);
        CalledAtUtc = now; CallCount++; Touch(now);
    }
    public void Start(DateTimeOffset now, long expectedVersion)
    {
        Ensure(expectedVersion, QueueTicketStatus.Called);
        Status = QueueTicketStatus.InService; ServiceStartedAtUtc = now; Touch(now);
    }
    public void Complete(DateTimeOffset now, long expectedVersion)
    {
        Ensure(expectedVersion, QueueTicketStatus.InService);
        Status = QueueTicketStatus.Completed; CompletedAtUtc = now; Touch(now);
    }
    public void Skip(DateTimeOffset now, long expectedVersion)
    {
        Ensure(expectedVersion, QueueTicketStatus.Called);
        Status = QueueTicketStatus.Skipped; Touch(now);
    }
    public void Restore(DateTimeOffset now, long expectedVersion)
    {
        Ensure(expectedVersion, QueueTicketStatus.Skipped);
        if (RestoreCount >= 1)
            throw new DomainException("QUEUE_RESTORE_LIMIT", "El turno solo puede devolverse una vez a espera.");
        Status = QueueTicketStatus.Waiting; RestoreCount++; Touch(now);
    }
    public void Cancel(DateTimeOffset now, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status is not (QueueTicketStatus.Waiting or QueueTicketStatus.Called or
            QueueTicketStatus.InService or QueueTicketStatus.Skipped))
            throw new DomainException("INVALID_QUEUE_TICKET_TRANSITION", "El turno ya no se puede cancelar.");
        Status = QueueTicketStatus.Cancelled; Touch(now);
    }
    private void Ensure(long expectedVersion, QueueTicketStatus status)
    {
        EnsureVersion(expectedVersion);
        if (Status != status)
            throw new DomainException("INVALID_QUEUE_TICKET_TRANSITION", "El estado actual no permite esta acción.");
    }
    private void EnsureVersion(long expected)
    {
        if (Version != expected)
            throw new DomainException("CONCURRENCY_CONFLICT", "El turno cambió. Recargue la información.");
    }
    private void Touch(DateTimeOffset now) { UpdatedAtUtc = now; Version++; }
}
