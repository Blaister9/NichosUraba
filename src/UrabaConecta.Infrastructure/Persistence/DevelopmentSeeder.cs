using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using UrabaConecta.Application;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;

namespace UrabaConecta.Infrastructure.Persistence;

public static class DevelopmentSeeder
{
    public const string BellaOwnerEmail = "propietaria@bella.demo";
    public const string PlatformAdminEmail = "admin@urabaconecta.demo";
    public const string PartnerOperatorEmail = "socia@urabaconecta.demo";
    public const string OtherOwnerEmail = "propietario@otro.demo";
    public const string BellaWorkerEmail = "trabajadora@bella.demo";
    public const string BellaConfigurationWorkerEmail = "configuradora@bella.demo";
    public const string CorteOwnerEmail = "propietario@corte.demo";
    public const string CorteQueueWorkerEmail = "turnos@corte.demo";
    public const string CorteNoPermissionEmail = "sinasignacion@corte.demo";
    public const string SazonOwnerEmail = "propietario@sazon.demo";
    public const string SazonOrdersWorkerEmail = "pedidos@sazon.demo";
    public const string SazonNoPermissionEmail = "sinpedidos@sazon.demo";
    // Credencial exclusivamente local para Development y pruebas. Demo exige secretos externos distintos.
    public const string DemoPassword = "UrabaDemo!2026";
    public static readonly Guid BellaBusinessId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherBusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CorteBusinessId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    public static readonly Guid SazonBusinessId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    public static async Task SeedDevelopmentAsync(this IServiceProvider services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Demo")) return;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var codes = scope.ServiceProvider.GetRequiredService<IPublicCodeService>();
        var protector = scope.ServiceProvider.GetRequiredService<UrabaConecta.Application.IPersonalDataProtector>();
        await db.Database.MigrateAsync();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        if (environment.IsEnvironment("Demo") &&
            !configuration.GetValue<bool>("DemoSeed:Enabled"))
            return;
        var businessPassword = environment.IsDevelopment()
            ? DemoPassword
            : RequiredSecret(configuration, "DemoSeed:BusinessPassword");
        var adminPassword = environment.IsDevelopment()
            ? DemoPassword
            : RequiredSecret(configuration, "DemoSeed:AdminPassword");
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "PlatformAdmin", "PartnerOperator", "BusinessOwner", "BusinessWorker" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var platformAdmin = await EnsureUser(userManager, PlatformAdminEmail, "Administración UrabáConecta", adminPassword);
        var partnerOperator = await EnsureUser(userManager, PartnerOperatorEmail, "Socia demostrativa", businessPassword);
        var bellaOwner = await EnsureUser(userManager, BellaOwnerEmail, "Propietaria Bella", businessPassword);
        var otherOwner = await EnsureUser(userManager, OtherOwnerEmail, "Propietario negocio aislado", businessPassword);
        var bellaWorker = await EnsureUser(userManager, BellaWorkerEmail, "Trabajadora Bella", businessPassword);
        var configurationWorker = await EnsureUser(userManager, BellaConfigurationWorkerEmail, "Configuradora Bella", businessPassword);
        var corteOwner = await EnsureUser(userManager, CorteOwnerEmail, "Propietario El Corte", businessPassword);
        var queueWorker = await EnsureUser(userManager, CorteQueueWorkerEmail, "Operador de turnos", businessPassword);
        var noPermission = await EnsureUser(userManager, CorteNoPermissionEmail, "Trabajador sin permiso", businessPassword);
        var sazonOwner = await EnsureUser(userManager, SazonOwnerEmail, "Propietario Sazón Local", businessPassword);
        var ordersWorker = await EnsureUser(userManager, SazonOrdersWorkerEmail, "Operador de pedidos", businessPassword);
        var noOrdersPermission = await EnsureUser(userManager, SazonNoPermissionEmail, "Trabajador sin pedidos", businessPassword);
        if (!await userManager.IsInRoleAsync(platformAdmin, "PlatformAdmin"))
            await userManager.AddToRoleAsync(platformAdmin, "PlatformAdmin");
        if (!await userManager.IsInRoleAsync(partnerOperator, "PartnerOperator"))
            await userManager.AddToRoleAsync(partnerOperator, "PartnerOperator");
        if (!await userManager.IsInRoleAsync(bellaOwner, "BusinessOwner")) await userManager.AddToRoleAsync(bellaOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(otherOwner, "BusinessOwner")) await userManager.AddToRoleAsync(otherOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(bellaWorker, "BusinessWorker")) await userManager.AddToRoleAsync(bellaWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(configurationWorker, "BusinessWorker"))
            await userManager.AddToRoleAsync(configurationWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(corteOwner, "BusinessOwner")) await userManager.AddToRoleAsync(corteOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(queueWorker, "BusinessWorker")) await userManager.AddToRoleAsync(queueWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(noPermission, "BusinessWorker")) await userManager.AddToRoleAsync(noPermission, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(sazonOwner, "BusinessOwner")) await userManager.AddToRoleAsync(sazonOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(ordersWorker, "BusinessWorker")) await userManager.AddToRoleAsync(ordersWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(noOrdersPermission, "BusinessWorker")) await userManager.AddToRoleAsync(noOrdersPermission, "BusinessWorker");

        if (await db.Businesses.AnyAsync())
        {
            await EnsureMembership(db, BellaBusinessId, bellaOwner.Id, MembershipRole.Owner);
            await EnsureMembership(db, OtherBusinessId, otherOwner.Id, MembershipRole.Owner);
            await EnsureMembership(db, BellaBusinessId, bellaWorker.Id, MembershipRole.Worker);
            await EnsureMembership(db, BellaBusinessId, configurationWorker.Id, MembershipRole.Worker, true);
            await EnsureQueueDemo(db, corteOwner.Id, queueWorker.Id, noPermission.Id);
            await EnsureOrderingDemo(db, sazonOwner.Id, ordersWorker.Id, noOrdersPermission.Id);
            await db.SaveChangesAsync();
            await EnsureModules(db);
            await EnsureShortDescriptions(db);
            await EnsureHistoricalDemo(db, codes, protector);
            await db.SaveChangesAsync();
            return;
        }
        var apartado = new Municipality(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "apartado", "Apartadó");
        var turbo = new Municipality(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "turbo", "Turbo");
        var beauty = new Category(Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "belleza-cuidado-personal", "Belleza y cuidado personal");
        var otherCategory = new Category(Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"), "servicios-profesionales", "Servicios profesionales");
        db.AddRange(apartado, turbo, beauty, otherCategory);

        var bella = new Business(BellaBusinessId, "salon-bella-uraba", "Salón Bella Urabá", apartado.Id, beauty.Id,
            "Salón ficticio para demostrar agendamiento local.", "Calle 100 # 00-00, Apartadó", "300 000 0000");
        var other = new Business(OtherBusinessId, "negocio-aislado-demo", "Negocio Aislado Demo", turbo.Id, otherCategory.Id,
            "Segundo negocio ficticio para comprobar aislamiento.", "Carrera 00 # 00-00, Turbo", "300 000 0001");
        other.Unpublish();
        db.AddRange(bella, other);
        db.AddRange(
            new BusinessMembership(Guid.NewGuid(), bella.Id, bellaOwner.Id, MembershipRole.Owner),
            new BusinessMembership(Guid.NewGuid(), other.Id, otherOwner.Id, MembershipRole.Owner),
            new BusinessMembership(Guid.NewGuid(), bella.Id, bellaWorker.Id, MembershipRole.Worker),
            new BusinessMembership(Guid.NewGuid(), bella.Id, configurationWorker.Id, MembershipRole.Worker, true));
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                     DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday })
            db.BusinessHours.Add(new BusinessHour(Guid.NewGuid(), bella.Id, day, new TimeOnly(8, 0), new TimeOnly(18, 0)));
        var worker = new StaffMember(Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), bella.Id, "Profesional Bella");
        db.StaffMembers.Add(worker);
        var demoServices = new[]
        {
            new Service(Guid.Parse("10000000-0000-0000-0000-000000000001"), bella.Id, "Corte femenino", 60, 35000),
            new Service(Guid.Parse("10000000-0000-0000-0000-000000000002"), bella.Id, "Cepillado", 45, 25000),
            new Service(Guid.Parse("10000000-0000-0000-0000-000000000003"), bella.Id, "Manicure tradicional", 45, 30000)
        };
        db.Services.AddRange(demoServices);
        foreach (var service in demoServices) db.StaffServices.Add(new StaffService(bella.Id, worker.Id, service.Id));
        await db.SaveChangesAsync();
        await EnsureQueueDemo(db, corteOwner.Id, queueWorker.Id, noPermission.Id);
        await EnsureOrderingDemo(db, sazonOwner.Id, ordersWorker.Id, noOrdersPermission.Id);
        await db.SaveChangesAsync();
        await EnsureModules(db);
        await EnsureShortDescriptions(db);
        await EnsureHistoricalDemo(db, codes, protector);
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureUser(UserManager<ApplicationUser> manager, string email,
        string displayName, string password)
    {
        var existing = await manager.FindByEmailAsync(email);
        if (existing is not null)
        {
            if (existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                await manager.UpdateAsync(existing);
            }
            return existing;
        }
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true, DisplayName = displayName
        };
        var result = await manager.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }

    private static string RequiredSecret(IConfiguration configuration, string key)
        => !string.IsNullOrWhiteSpace(configuration[key])
            ? configuration[key]!
            : throw new InvalidOperationException($"Falta {key} para el ambiente Demo.");

    private static async Task EnsureMembership(AppDbContext db, Guid businessId, Guid userId, MembershipRole role,
        bool canManageConfiguration = false, bool canManageQueues = false, bool canManageOrders = false)
    {
        if (!await db.BusinessMemberships.AnyAsync(x => x.BusinessId == businessId && x.UserId == userId))
            db.BusinessMemberships.Add(new BusinessMembership(Guid.NewGuid(), businessId, userId, role,
                canManageConfiguration, canManageQueues: canManageQueues, canManageOrders: canManageOrders));
    }

    private static async Task EnsureQueueDemo(AppDbContext db, Guid ownerId, Guid queueWorkerId, Guid noPermissionId)
    {
        var municipality = await db.Municipalities.SingleOrDefaultAsync(x => x.Slug == "chigorodo");
        if (municipality is null)
        {
            municipality = new Municipality(Guid.Parse("abababab-abab-abab-abab-abababababab"), "chigorodo", "Chigorodó");
            db.Add(municipality);
        }
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Slug == "barberia");
        if (category is null)
        {
            category = new Category(Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd"), "barberia", "Barbería");
            db.Add(category);
        }
        if (!await db.Businesses.AnyAsync(x => x.Id == CorteBusinessId))
            db.Add(new Business(CorteBusinessId, "barberia-el-corte", "Barbería El Corte", municipality.Id,
                category.Id, "Barbería ficticia con fila virtual para atención por orden de llegada.",
                "Centro, Chigorodó", "300 000 0002"));
        await db.SaveChangesAsync();
        await EnsureMembership(db, CorteBusinessId, ownerId, MembershipRole.Owner);
        await EnsureMembership(db, CorteBusinessId, queueWorkerId, MembershipRole.Worker, canManageQueues: true);
        await EnsureMembership(db, CorteBusinessId, noPermissionId, MembershipRole.Worker);
        var definition = await db.QueueDefinitions.SingleOrDefaultAsync(x => x.BusinessId == CorteBusinessId && x.IsActive);
        if (definition is null)
        {
            definition = new QueueDefinition(Guid.Parse("44444444-4444-4444-4444-444444444444"),
                CorteBusinessId, "Atención general", 25, 20,
                "Toma tu turno y revisa aquí cuándo se acerca tu atención.", true);
            db.Add(definition);
            await db.SaveChangesAsync();
        }
        if (!await db.QueueSessions.AnyAsync(x => x.BusinessId == CorteBusinessId &&
            (x.Status == QueueSessionStatus.Open || x.Status == QueueSessionStatus.Paused)))
            db.Add(new QueueSession(Guid.NewGuid(), CorteBusinessId, definition.Id, DateTimeOffset.UtcNow));
    }

    private static async Task EnsureOrderingDemo(AppDbContext db, Guid ownerId, Guid ordersWorkerId, Guid noPermissionId)
    {
        var municipality = await db.Municipalities.SingleOrDefaultAsync(x => x.Slug == "carepa");
        if (municipality is null)
        {
            municipality = new Municipality(Guid.Parse("acacacac-acac-acac-acac-acacacacacac"), "carepa", "Carepa");
            db.Add(municipality);
        }
        var category = await db.Categories.SingleOrDefaultAsync(x => x.Slug == "restaurante");
        if (category is null)
        {
            category = new Category(Guid.Parse("dededede-dede-dede-dede-dededededede"), "restaurante", "Restaurante");
            db.Add(category);
        }
        if (!await db.Businesses.AnyAsync(x => x.Id == SazonBusinessId))
            db.Add(new Business(SazonBusinessId, "restaurante-sazon-local", "Restaurante Sazón Local",
                municipality.Id, category.Id, "Restaurante ficticio con pedidos para recoger.",
                "Centro, Carepa", "300 000 0003"));
        await db.SaveChangesAsync();
        await EnsureMembership(db, SazonBusinessId, ownerId, MembershipRole.Owner);
        await EnsureMembership(db, SazonBusinessId, ordersWorkerId, MembershipRole.Worker, canManageOrders: true);
        await EnsureMembership(db, SazonBusinessId, noPermissionId, MembershipRole.Worker);
        if (!await db.BusinessHours.AnyAsync(x => x.BusinessId == SazonBusinessId))
            foreach (var day in Enum.GetValues<DayOfWeek>())
                db.BusinessHours.Add(new BusinessHour(Guid.NewGuid(), SazonBusinessId, day,
                    new TimeOnly(11, 0), new TimeOnly(21, 0)));
        if (!await db.PickupOrderSettings.AnyAsync(x => x.BusinessId == SazonBusinessId))
            db.PickupOrderSettings.Add(new PickupOrderSettings(Guid.Parse("56565656-5656-5656-5656-565656565656"),
                SazonBusinessId, true, "Haz tu pedido y recógelo sin filas.", 30, 15, 5,
                new TimeOnly(11, 0), new TimeOnly(20, 30)));
        var categories = new[]
        {
            new ProductCategory(Guid.Parse("60000000-0000-0000-0000-000000000001"), SazonBusinessId, "Hamburguesas", 1),
            new ProductCategory(Guid.Parse("60000000-0000-0000-0000-000000000002"), SazonBusinessId, "Acompañamientos", 2),
            new ProductCategory(Guid.Parse("60000000-0000-0000-0000-000000000003"), SazonBusinessId, "Bebidas", 3)
        };
        foreach (var item in categories)
            if (!await db.ProductCategories.AnyAsync(x => x.Id == item.Id)) db.ProductCategories.Add(item);
        var products = new[]
        {
            new Product(Guid.Parse("70000000-0000-0000-0000-000000000001"), SazonBusinessId, categories[0].Id, "Hamburguesa tradicional", "Carne, queso y vegetales.", 18000, 1),
            new Product(Guid.Parse("70000000-0000-0000-0000-000000000002"), SazonBusinessId, categories[0].Id, "Hamburguesa especial", "Hamburguesa especial de la casa.", 24000, 2),
            new Product(Guid.Parse("70000000-0000-0000-0000-000000000003"), SazonBusinessId, categories[1].Id, "Papas fritas", "Porción personal.", 8000, 1),
            new Product(Guid.Parse("70000000-0000-0000-0000-000000000004"), SazonBusinessId, categories[2].Id, "Limonada natural", "Vaso de 16 oz.", 6000, 1),
            new Product(Guid.Parse("70000000-0000-0000-0000-000000000005"), SazonBusinessId, categories[2].Id, "Gaseosa personal", "Presentación personal.", 5000, 2)
        };
        foreach (var item in products)
            if (!await db.Products.AnyAsync(x => x.Id == item.Id)) db.Products.Add(item);
    }

    private static async Task EnsureHistoricalDemo(AppDbContext db, IPublicCodeService codes,
        UrabaConecta.Application.IPersonalDataProtector protector)
    {
        var now = DateTimeOffset.UtcNow;
        var appointmentId = Guid.Parse("81000000-0000-0000-0000-000000000001");
        if (!await db.Appointments.AnyAsync(x => x.Id == appointmentId))
        {
            var consent = new ConsentReceipt(Guid.Parse("82000000-0000-0000-0000-000000000001"),
                BellaBusinessId, "pilot-1", "Cita demostrativa", now.AddDays(-2));
            consent.LinkAppointment(appointmentId);
            var code = codes.Generate();
            var appointment = new Appointment(appointmentId, BellaBusinessId,
                Guid.Parse("10000000-0000-0000-0000-000000000001"),
                Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"), now.AddDays(2), 60,
                "Corte femenino", 35000, protector.Protect("Cliente demo"),
                protector.Protect("3001234567"), "4567", protector.Protect("Registro histórico de demo"),
                code.Hash, code.Version, consent.Id, now.AddDays(-2));
            appointment.ChangeStatus(AppointmentStatus.Confirmed, now.AddDays(-1));
            appointment.ChangeStatus(AppointmentStatus.Completed, now);
            db.AddRange(consent, appointment);
        }

        var ticketId = Guid.Parse("83000000-0000-0000-0000-000000000001");
        if (!await db.QueueTickets.AnyAsync(x => x.Id == ticketId))
        {
            var session = await db.QueueSessions.Where(x => x.BusinessId == CorteBusinessId)
                .OrderByDescending(x => x.OpenedAtUtc).FirstAsync();
            var ticket = new QueueTicket(ticketId, CorteBusinessId, session.Id, 900,
                codes.Hash("demo-historical-ticket"), protector.Protect("Turno demo"),
                QueueTicketSource.WalkIn, now.AddHours(-2));
            ticket.Call(now.AddMinutes(-90), 0);
            ticket.Start(now.AddMinutes(-80), 1);
            ticket.Complete(now.AddMinutes(-60), 2);
            db.Add(ticket);
        }

        var orderId = Guid.Parse("84000000-0000-0000-0000-000000000001");
        if (!await db.PickupOrders.AnyAsync(x => x.Id == orderId))
        {
            var consent = new ConsentReceipt(Guid.Parse("85000000-0000-0000-0000-000000000001"),
                SazonBusinessId, "pilot-1", "Pedido demostrativo", now.AddDays(-1));
            consent.LinkPickupOrder(orderId);
            var line = new PickupOrderLine(Guid.Parse("86000000-0000-0000-0000-000000000001"),
                SazonBusinessId, orderId, Guid.Parse("70000000-0000-0000-0000-000000000001"),
                "Hamburguesa tradicional", 18000, 1);
            var order = new PickupOrder(orderId, SazonBusinessId, 9001, now.AddHours(2), now.AddHours(2.25),
                protector.Protect("Pedido demo"), protector.Protect("3001234567"), "4567", null,
                codes.Hash("demo-historical-order"), "pilot-1", now.AddDays(-1), now.AddDays(-1), [line]);
            order.Transition(PickupOrderStatus.Accepted, now.AddHours(-3), 0);
            order.Transition(PickupOrderStatus.Preparing, now.AddHours(-2), 1);
            order.Transition(PickupOrderStatus.ReadyForPickup, now.AddHours(-1), 2);
            order.Transition(PickupOrderStatus.Delivered, now, 3);
            db.AddRange(consent, order);
        }
    }

    /// <summary>
    /// Los negocios ficticios se crearon antes de V5, cuando no existía la descripción breve.
    /// Se completa aquí para que el checklist de onboarding refleje su estado real.
    /// Los archivados se excluyen: el dominio prohíbe editarlos, así que intentarlo lanzaba una
    /// excepción no controlada durante el arranque y el contenedor no llegaba a escuchar. Basta
    /// un negocio archivado sin descripción breve para dejar la aplicación sin arrancar.
    /// </summary>
    private static async Task EnsureShortDescriptions(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var business in await db.Businesses
            .Where(x => x.ShortDescription == "" && x.Status != BusinessStatus.Archived).ToListAsync())
            business.UpdateCommercialProfile(new BusinessProfileEdit(business.Slug, business.Name,
                business.MunicipalityId, business.CategoryId,
                Shorten(business.Description, business.Name), business.Description, business.Address,
                business.ReferencePoint, business.PublicPhone, business.WhatsAppUrl, business.PublicEmail,
                business.InstagramUrl, business.FacebookUrl, business.LocationUrl,
                business.CustomerInstructions), now, business.Version);
    }

    private static string Shorten(string description, string name)
    {
        var source = string.IsNullOrWhiteSpace(description) ? name : description;
        return source.Length <= 160 ? source : source[..160];
    }

    private static async Task EnsureModules(AppDbContext db)
    {
        var now = DateTimeOffset.UtcNow;
        var expected = new[]
        {
            new BusinessModule(BellaBusinessId, BusinessModuleKind.Appointments, true, now),
            new BusinessModule(CorteBusinessId, BusinessModuleKind.VirtualQueues, true, now),
            new BusinessModule(SazonBusinessId, BusinessModuleKind.PickupOrders, true, now)
        };
        foreach (var module in expected)
            if (!await db.BusinessModules.AnyAsync(x => x.BusinessId == module.BusinessId && x.Module == module.Module))
                db.BusinessModules.Add(module);
        await db.SaveChangesAsync();
    }
}
