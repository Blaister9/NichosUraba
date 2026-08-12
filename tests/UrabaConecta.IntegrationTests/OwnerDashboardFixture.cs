using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UrabaConecta.Application;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.IntegrationTests;

/// <summary>
/// Construye negocios con actividad real —citas, turnos y pedidos— para medir el resumen operativo
/// contra PostgreSQL. Todo se coloca respecto al reloj congelado y a la ventana del día local de cada
/// negocio, no respecto a la hora de la máquina: así una prueba que corre a las once de la noche mide
/// lo mismo que una que corre al mediodía.
/// </summary>
internal sealed class OwnerDashboardFixture(DashboardWebFactory factory)
{
    private static readonly DateTimeOffset Now = DashboardWebFactory.Now;

    /// <summary>Cuántas filas por estado siembra <see cref="WithActivityAsync"/> en cada negocio.</summary>
    internal const int AppointmentsToday = 4;      // completada, pendiente, confirmada y cancelada
    internal const int QueueServedToday = 2;
    internal const int OrdersDeliveredToday = 2;

    internal async Task<Guid> UserAsync(string email, string role = "BusinessOwner")
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true,
            DisplayName = "Persona de prueba"
        };
        var created = await users.CreateAsync(user, DevelopmentSeeder.DemoPassword);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(x => x.Description)));
        await users.AddToRoleAsync(user, role);
        return user.Id;
    }

    /// <summary>
    /// Un negocio con los módulos indicados, su membresía y —si se pide— actividad suficiente para que
    /// los contadores no sean todos cero. Devuelve el identificador y la ventana de su día local, que
    /// es lo que las pruebas necesitan para afirmar qué queda dentro y qué queda fuera.
    /// </summary>
    internal async Task<BusinessDayWindow> BusinessAsync(Guid ownerId, string name,
        bool appointments = false, bool queues = false, bool orders = false,
        string timeZone = "America/Bogota", bool withActivity = true, MembershipRole role = MembershipRole.Owner)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var municipality = await db.Municipalities.AsNoTracking().Select(x => x.Id).FirstAsync();
        var category = await db.Categories.AsNoTracking().Select(x => x.Id).FirstAsync();

        var id = Guid.NewGuid();
        var business = new Business(id, $"prueba-{id:N}", name, municipality, category,
            "Negocio de prueba del resumen operativo.", "Sin dirección", "300 000 0000");
        db.Add(business);
        // La zona horaria no tiene modificador de dominio —se configura en el alta— así que para la
        // prueba se escribe por la propiedad mapeada.
        db.Entry(business).Property(x => x.TimeZoneId).CurrentValue = timeZone;

        db.Add(new BusinessMembership(Guid.NewGuid(), id, ownerId, role,
            canManageConfiguration: true, canManageAppointments: true, canManageMembers: true,
            canManageQueues: true, canManageOrders: true));

        if (appointments) db.Add(new BusinessModule(id, BusinessModuleKind.Appointments, true, Now));
        if (queues) db.Add(new BusinessModule(id, BusinessModuleKind.VirtualQueues, true, Now));
        if (orders) db.Add(new BusinessModule(id, BusinessModuleKind.PickupOrders, true, Now));

        await db.SaveChangesAsync();

        var window = OwnerDashboardUseCases.LocalDay(id, timeZone, Now);
        if (withActivity) await WithActivityAsync(db, window, appointments, queues, orders);
        return window;
    }

    /// <summary>
    /// Siembra actividad dentro y fuera de la ventana del negocio. Lo de fuera es tan importante como
    /// lo de dentro: es lo que delata una consulta que se olvidó del día local.
    /// </summary>
    private static async Task WithActivityAsync(AppDbContext db, BusinessDayWindow window,
        bool appointments, bool queues, bool orders)
    {
        if (appointments) Appointments(db, window);
        if (queues) Queues(db, window);
        if (orders) Orders(db, window);
        await db.SaveChangesAsync();
    }

    private static void Appointments(AppDbContext db, BusinessDayWindow window)
    {
        var businessId = window.BusinessId;
        var serviceId = Guid.NewGuid();
        var staffId = Guid.NewGuid();
        db.Add(new Service(serviceId, businessId, "Corte", 60, 35000));
        db.Add(new StaffMember(staffId, businessId, "Profesional"));

        // Ya atendida hoy.
        Add(Now.AddHours(-4), AppointmentStatus.Completed, "Corte");
        // La que viene: la más próxima de las que siguen vivas.
        Add(Now.AddHours(1), AppointmentStatus.Pending, "Cepillado");
        Add(Now.AddHours(2), AppointmentStatus.Confirmed, "Manicure");
        // Antes que la anterior, pero cancelada: no puede ser "la próxima".
        Add(Now.AddMinutes(30), AppointmentStatus.Cancelled, "Cancelada");
        // Mañana: fuera de la ventana, no cuenta ni como total ni como próxima.
        Add(window.ToUtc.AddHours(1), AppointmentStatus.Pending, "De mañana");

        void Add(DateTimeOffset startAtUtc, AppointmentStatus status, string serviceName)
        {
            var appointmentId = Guid.NewGuid();
            var consent = new ConsentReceipt(Guid.NewGuid(), businessId, "pruebas", "Cita de prueba",
                startAtUtc.AddHours(-2));
            consent.LinkAppointment(appointmentId);
            var created = startAtUtc.AddHours(-1);
            var appointment = new Appointment(appointmentId, businessId, serviceId, staffId, startAtUtc, 60,
                serviceName, 35000, "protegido", "protegido", "0000", "protegido",
                Guid.NewGuid().ToString("N"), 1, consent.Id, created);
            switch (status)
            {
                case AppointmentStatus.Confirmed:
                    appointment.ChangeStatus(AppointmentStatus.Confirmed, created);
                    break;
                case AppointmentStatus.Completed:
                    appointment.ChangeStatus(AppointmentStatus.Confirmed, created);
                    appointment.ChangeStatus(AppointmentStatus.Completed, startAtUtc);
                    break;
                case AppointmentStatus.Cancelled:
                    appointment.ChangeStatus(AppointmentStatus.Cancelled, created);
                    break;
            }
            db.AddRange(consent, appointment);
        }
    }

    private static void Queues(AppDbContext db, BusinessDayWindow window)
    {
        var businessId = window.BusinessId;
        var definitionId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        db.Add(new QueueDefinition(definitionId, businessId, "Fila", 15, 100, null, true, Now));
        db.Add(new QueueSession(sessionId, businessId, definitionId, Now.AddHours(-8)));

        var number = 1;
        Waiting(); Waiting();
        InService(7);
        Completed(Now.AddHours(-3)); Completed(Now.AddHours(-2));
        // De ayer: completado, pero fuera de la ventana de este negocio.
        Completed(window.FromUtc.AddHours(-1));

        QueueTicket New(int value, DateTimeOffset created)
        {
            var ticket = new QueueTicket(Guid.NewGuid(), businessId, sessionId, value,
                Guid.NewGuid().ToString("N"), "protegido", QueueTicketSource.WalkIn, created);
            db.Add(ticket);
            return ticket;
        }
        void Waiting() => New(number++, Now.AddHours(-1));
        void InService(int value)
        {
            var ticket = New(value, Now.AddHours(-1));
            ticket.Call(Now.AddMinutes(-50), 0);
            ticket.Start(Now.AddMinutes(-40), 1);
        }
        void Completed(DateTimeOffset completedAt)
        {
            var ticket = New(number++, completedAt.AddHours(-1));
            ticket.Call(completedAt.AddMinutes(-20), 0);
            ticket.Start(completedAt.AddMinutes(-10), 1);
            ticket.Complete(completedAt, 2);
        }
    }

    private static void Orders(AppDbContext db, BusinessDayWindow window)
    {
        var businessId = window.BusinessId;
        var categoryId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        db.Add(new ProductCategory(categoryId, businessId, "Platos"));
        db.Add(new Product(productId, businessId, categoryId, "Bandeja", null, 18000));

        var number = 1;
        Order(PickupOrderStatus.Pending, Now.AddHours(-5));
        Order(PickupOrderStatus.Preparing, Now.AddHours(-4));
        Order(PickupOrderStatus.ReadyForPickup, Now.AddHours(-3));
        Order(PickupOrderStatus.Delivered, Now.AddHours(-3));
        Order(PickupOrderStatus.Delivered, Now.AddHours(-1));
        // De ayer: entregado, pero fuera de la ventana de este negocio.
        Order(PickupOrderStatus.Delivered, window.FromUtc.AddHours(-2));

        void Order(PickupOrderStatus target, DateTimeOffset reachedAt)
        {
            var orderId = Guid.NewGuid();
            var consent = new ConsentReceipt(Guid.NewGuid(), businessId, "pruebas", "Pedido de prueba",
                reachedAt.AddHours(-3));
            consent.LinkPickupOrder(orderId);
            var line = new PickupOrderLine(Guid.NewGuid(), businessId, orderId, productId, "Bandeja", 18000, 1);
            var order = new PickupOrder(orderId, businessId, number++, reachedAt.AddHours(3),
                reachedAt.AddHours(4), "protegido", "protegido", "0000", null,
                Guid.NewGuid().ToString("N"), "pruebas", reachedAt.AddHours(-3), reachedAt.AddHours(-3), [line]);
            // El estado se alcanza recorriendo la máquina real: es la única forma de que UpdatedAtUtc
            // signifique lo que la métrica de entregados supone que significa.
            if (target is not PickupOrderStatus.Pending)
            {
                order.Transition(PickupOrderStatus.Accepted, reachedAt, 0);
                if (target is not PickupOrderStatus.Accepted)
                {
                    order.Transition(PickupOrderStatus.Preparing, reachedAt, 1);
                    if (target is not PickupOrderStatus.Preparing)
                    {
                        order.Transition(PickupOrderStatus.ReadyForPickup, reachedAt, 2);
                        if (target is PickupOrderStatus.Delivered)
                            order.Transition(PickupOrderStatus.Delivered, reachedAt, 3);
                    }
                }
            }
            db.AddRange(consent, order);
        }
    }
}
