using UrabaConecta.Domain;

namespace UrabaConecta.Domain.Tests;

public sealed class QueueTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Session_supports_pause_resume_and_safe_close()
    {
        var session = Session();
        session.Pause(Now, 0);
        Assert.Equal(QueueSessionStatus.Paused, session.Status);
        session.Resume(1);
        var error = Assert.Throws<DomainException>(() => session.Close(Now, 1, 2));
        Assert.Equal("QUEUE_HAS_ACTIVE_TICKETS", error.Code);
        session.Close(Now, 0, 2);
        Assert.Equal(QueueSessionStatus.Closed, session.Status);
    }

    [Fact]
    public void Session_allocates_strictly_increasing_numbers_only_while_open()
    {
        var session = Session();
        Assert.Equal(1, session.AllocateNumber(0));
        Assert.Equal(2, session.AllocateNumber(1));
        session.Pause(Now, 2);
        Assert.Equal("QUEUE_NOT_OPEN", Assert.Throws<DomainException>(() => session.AllocateNumber(3)).Code);
    }

    [Fact]
    public void Ticket_happy_path_is_explicit()
    {
        var ticket = Ticket();
        ticket.Call(Now, 0);
        ticket.Start(Now, 1);
        ticket.Complete(Now, 2);
        Assert.Equal(QueueTicketStatus.Completed, ticket.Status);
        Assert.Equal("INVALID_QUEUE_TICKET_TRANSITION",
            Assert.Throws<DomainException>(() => ticket.Cancel(Now, 3)).Code);
    }

    [Fact]
    public void Skipped_ticket_can_return_to_waiting_only_once()
    {
        var ticket = Ticket();
        ticket.Call(Now, 0); ticket.Skip(Now, 1); ticket.Restore(Now, 2);
        ticket.Call(Now, 3); ticket.Skip(Now, 4);
        Assert.Equal("QUEUE_RESTORE_LIMIT", Assert.Throws<DomainException>(() => ticket.Restore(Now, 5)).Code);
    }

    [Fact]
    public void Stale_ticket_version_is_rejected()
        => Assert.Equal("CONCURRENCY_CONFLICT",
            Assert.Throws<DomainException>(() => Ticket().Call(Now, 9)).Code);

    [Fact]
    public void Queue_configuration_validates_limits_and_version()
    {
        var q = new QueueDefinition(Guid.NewGuid(), Guid.NewGuid(), "General", 25, 20, "Mensaje", true, Now);
        q.Update("General", 30, 15, "Nuevo", true, Now, 0);
        Assert.Equal(30, q.AverageDurationMinutes);
        Assert.Equal("CONCURRENCY_CONFLICT",
            Assert.Throws<DomainException>(() => q.Update("X", 20, 10, "", true, Now, 0)).Code);
    }

    [Fact]
    public void Queue_permission_is_effective_for_owner_and_explicit_worker()
    {
        var owner = new BusinessMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MembershipRole.Owner);
        var worker = new BusinessMembership(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), MembershipRole.Worker,
            canManageQueues: true);
        Assert.True(owner.HasPermission(false, false, false, true));
        Assert.True(worker.HasPermission(false, false, false, true));
    }

    private static QueueSession Session() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
    private static QueueTicket Ticket() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1,
        new string('a', 64), null, QueueTicketSource.Online, Now);
}
