using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;

namespace UrabaConecta.Infrastructure.Persistence;

public static class DevelopmentSeeder
{
    public const string BellaOwnerEmail = "propietaria@bella.demo";
    public const string OtherOwnerEmail = "propietario@otro.demo";
    public const string BellaWorkerEmail = "trabajadora@bella.demo";
    public const string BellaConfigurationWorkerEmail = "configuradora@bella.demo";
    public const string CorteOwnerEmail = "propietario@corte.demo";
    public const string CorteQueueWorkerEmail = "turnos@corte.demo";
    public const string CorteNoPermissionEmail = "sinasignacion@corte.demo";
    public const string DemoPassword = "UrabaDemo!2026";
    public static readonly Guid BellaBusinessId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherBusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid CorteBusinessId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static async Task SeedDevelopmentAsync(this IServiceProvider services, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment()) return;
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var role in new[] { "PlatformAdmin", "BusinessOwner", "BusinessWorker" })
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));

        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var bellaOwner = await EnsureUser(userManager, BellaOwnerEmail, "Propietaria Bella");
        var otherOwner = await EnsureUser(userManager, OtherOwnerEmail, "Propietario negocio aislado");
        var bellaWorker = await EnsureUser(userManager, BellaWorkerEmail, "Trabajadora Bella");
        var configurationWorker = await EnsureUser(userManager, BellaConfigurationWorkerEmail, "Configuradora Bella");
        var corteOwner = await EnsureUser(userManager, CorteOwnerEmail, "Propietario El Corte");
        var queueWorker = await EnsureUser(userManager, CorteQueueWorkerEmail, "Operador de turnos");
        var noPermission = await EnsureUser(userManager, CorteNoPermissionEmail, "Trabajador sin permiso");
        if (!await userManager.IsInRoleAsync(bellaOwner, "BusinessOwner")) await userManager.AddToRoleAsync(bellaOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(otherOwner, "BusinessOwner")) await userManager.AddToRoleAsync(otherOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(bellaWorker, "BusinessWorker")) await userManager.AddToRoleAsync(bellaWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(configurationWorker, "BusinessWorker"))
            await userManager.AddToRoleAsync(configurationWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(corteOwner, "BusinessOwner")) await userManager.AddToRoleAsync(corteOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(queueWorker, "BusinessWorker")) await userManager.AddToRoleAsync(queueWorker, "BusinessWorker");
        if (!await userManager.IsInRoleAsync(noPermission, "BusinessWorker")) await userManager.AddToRoleAsync(noPermission, "BusinessWorker");

        if (await db.Businesses.AnyAsync())
        {
            await EnsureMembership(db, BellaBusinessId, bellaOwner.Id, MembershipRole.Owner);
            await EnsureMembership(db, OtherBusinessId, otherOwner.Id, MembershipRole.Owner);
            await EnsureMembership(db, BellaBusinessId, bellaWorker.Id, MembershipRole.Worker);
            await EnsureMembership(db, BellaBusinessId, configurationWorker.Id, MembershipRole.Worker, true);
            await EnsureQueueDemo(db, corteOwner.Id, queueWorker.Id, noPermission.Id);
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
        await db.SaveChangesAsync();
    }

    private static async Task<ApplicationUser> EnsureUser(UserManager<ApplicationUser> manager, string email,
        string displayName)
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
        var result = await manager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }

    private static async Task EnsureMembership(AppDbContext db, Guid businessId, Guid userId, MembershipRole role,
        bool canManageConfiguration = false, bool canManageQueues = false)
    {
        if (!await db.BusinessMemberships.AnyAsync(x => x.BusinessId == businessId && x.UserId == userId))
            db.BusinessMemberships.Add(new BusinessMembership(Guid.NewGuid(), businessId, userId, role,
                canManageConfiguration, canManageQueues: canManageQueues));
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
}
