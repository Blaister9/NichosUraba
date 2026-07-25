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
    public const string DemoPassword = "UrabaDemo!2026";
    public static readonly Guid BellaBusinessId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherBusinessId = Guid.Parse("22222222-2222-2222-2222-222222222222");

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
        var bellaOwner = await EnsureUser(userManager, BellaOwnerEmail);
        var otherOwner = await EnsureUser(userManager, OtherOwnerEmail);
        if (!await userManager.IsInRoleAsync(bellaOwner, "BusinessOwner")) await userManager.AddToRoleAsync(bellaOwner, "BusinessOwner");
        if (!await userManager.IsInRoleAsync(otherOwner, "BusinessOwner")) await userManager.AddToRoleAsync(otherOwner, "BusinessOwner");

        if (await db.Businesses.AnyAsync()) return;
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
            new BusinessMembership(Guid.NewGuid(), other.Id, otherOwner.Id, MembershipRole.Owner));
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
    }

    private static async Task<ApplicationUser> EnsureUser(UserManager<ApplicationUser> manager, string email)
    {
        var existing = await manager.FindByEmailAsync(email);
        if (existing is not null) return existing;
        var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true };
        var result = await manager.CreateAsync(user, DemoPassword);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }
}
