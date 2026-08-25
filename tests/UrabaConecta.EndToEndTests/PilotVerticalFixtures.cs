using Microsoft.EntityFrameworkCore;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Persistence;

namespace UrabaConecta.EndToEndTests;

/// <summary>
/// Cinco negocios de mentira, uno por vertical del piloto, con la combinación de capacidades que
/// cada una necesita.
///
/// Existen por una razón concreta: la promesa de este trabajo es que UrabáConecta sea UNA
/// plataforma configurable, no cinco aplicaciones. Eso sólo se puede afirmar recorriendo las cinco
/// verticales sobre el mismo código y comprobando que cada negocio ve lo suyo y sólo lo suyo.
///
/// Se siembran directamente contra la base porque la aplicación corre en otro proceso, con
/// identificadores fijos que empiezan por "e2e" y slugs terminados en "-e2e": nunca se confunden
/// con un negocio real, y ningún camino de la aplicación los trata distinto. La propiedad recae en
/// una cuenta que el sembrado de desarrollo ya crea; no se toca ni se reasigna nada existente.
/// </summary>
public static class PilotVerticalFixtures
{
    public const string OwnerEmail = DevelopmentSeeder.BellaOwnerEmail;

    public sealed record Vertical(Guid BusinessId, string Slug, string Name, string CategorySlug,
        bool Appointments, bool Queues, bool Orders);

    public static readonly Vertical Dentistry = new(
        Guid.Parse("e2e00001-0000-4000-8000-000000000001"), "clinica-dental-e2e",
        "Clínica Dental E2E", "odontologia", Appointments: true, Queues: false, Orders: false);

    public static readonly Vertical Veterinary = new(
        Guid.Parse("e2e00002-0000-4000-8000-000000000002"), "veterinaria-e2e",
        "Veterinaria E2E", "veterinarias", Appointments: true, Queues: true, Orders: true);

    public static readonly Vertical Spa = new(
        Guid.Parse("e2e00003-0000-4000-8000-000000000003"), "spa-belleza-e2e",
        "Spa y Belleza E2E", "spa-y-belleza", Appointments: true, Queues: false, Orders: true);

    public static readonly Vertical Pharmacy = new(
        Guid.Parse("e2e00004-0000-4000-8000-000000000004"), "drogueria-e2e",
        "Droguería E2E", "droguerias", Appointments: false, Queues: false, Orders: true);

    public static readonly Vertical Optics = new(
        Guid.Parse("e2e00005-0000-4000-8000-000000000005"), "optica-e2e",
        "Óptica E2E", "opticas", Appointments: true, Queues: false, Orders: true);

    public static readonly Vertical[] All = [Dentistry, Veterinary, Spa, Pharmacy, Optics];

    /// <summary>
    /// Idempotente: se puede llamar en cada arranque de la suite sin duplicar nada ni tocar lo que
    /// ya exista. Sólo inserta lo que falta.
    /// </summary>
    public static async Task EnsureAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new AppDbContext(options);
        var now = DateTimeOffset.UtcNow;

        var municipalityId = await db.Municipalities.Where(x => x.Slug == "apartado")
            .Select(x => x.Id).FirstAsync();
        var ownerId = await db.Users.Where(x => x.Email == OwnerEmail).Select(x => x.Id).SingleAsync();

        foreach (var vertical in All)
        {
            var categoryId = await db.Categories.Where(x => x.Slug == vertical.CategorySlug)
                .Select(x => x.Id).SingleAsync();

            if (!await db.Businesses.AnyAsync(x => x.Id == vertical.BusinessId))
            {
                var business = new Business(vertical.BusinessId, vertical.Slug, vertical.Name,
                    municipalityId, categoryId,
                    $"Negocio de prueba de {vertical.Name} para el recorrido por verticales.",
                    "Calle 1 # 1-1, Apartadó", "3000000000");
                business.UpdateCommercialProfile(new BusinessProfileEdit(business.Slug, business.Name,
                    municipalityId, categoryId, $"{vertical.Name} de prueba.", business.Description,
                    business.Address, "Sector de prueba", "3000000000", null, null, null, null, null,
                    "Trae tu documento."), now, business.Version);
                business.Activate(true, now, business.Version);
                db.Businesses.Add(business);
            }

            await EnsureModule(db, vertical.BusinessId, BusinessModuleKind.Appointments, vertical.Appointments, now);
            await EnsureModule(db, vertical.BusinessId, BusinessModuleKind.VirtualQueues, vertical.Queues, now);
            await EnsureModule(db, vertical.BusinessId, BusinessModuleKind.PickupOrders, vertical.Orders, now);

            if (!await db.BusinessMemberships.AnyAsync(x => x.BusinessId == vertical.BusinessId &&
                    x.UserId == ownerId))
                db.BusinessMemberships.Add(new BusinessMembership(Guid.NewGuid(), vertical.BusinessId,
                    ownerId, MembershipRole.Owner));

            foreach (var kind in new[] { BusinessImageKind.Logo, BusinessImageKind.Cover })
                if (!await db.BusinessImages.AnyAsync(x => x.BusinessId == vertical.BusinessId &&
                        x.Kind == kind && !x.IsDeleted))
                    db.BusinessImages.Add(new BusinessImage(Deterministic(vertical.BusinessId,
                        kind == BusinessImageKind.Logo ? (byte)0x41 : (byte)0x42), vertical.BusinessId, kind,
                        $"e2e-fixtures/{vertical.BusinessId:N}/{kind}.png", "image/png", 100, 100, 100,
                        $"{kind} de fixture", 0, now));

            if (!await db.BusinessHours.AnyAsync(x => x.BusinessId == vertical.BusinessId))
                for (var day = DayOfWeek.Monday; day <= DayOfWeek.Saturday; day++)
                    db.BusinessHours.Add(new BusinessHour(Guid.NewGuid(), vertical.BusinessId, day,
                        new TimeOnly(8, 0), new TimeOnly(18, 0)));

            if (vertical.Appointments) await EnsureScheduling(db, vertical);
            if (vertical.Queues) await EnsureQueue(db, vertical, now);
            if (vertical.Orders) await EnsureCatalog(db, vertical);
        }
        await db.SaveChangesAsync();
    }

    private static async Task EnsureModule(AppDbContext db, Guid businessId, BusinessModuleKind module,
        bool enabled, DateTimeOffset now)
    {
        if (!await db.BusinessModules.AnyAsync(x => x.BusinessId == businessId && x.Module == module))
            db.BusinessModules.Add(new BusinessModule(businessId, module, enabled, now));
    }

    private static async Task EnsureScheduling(AppDbContext db, Vertical vertical)
    {
        var serviceId = Deterministic(vertical.BusinessId, 0x51);
        var staffId = Deterministic(vertical.BusinessId, 0x5A);
        if (!await db.Services.AnyAsync(x => x.Id == serviceId))
            db.Services.Add(new Service(serviceId, vertical.BusinessId, $"Consulta {vertical.Name}",
                30, 60000, "Servicio de prueba del recorrido por verticales."));
        if (!await db.StaffMembers.AnyAsync(x => x.Id == staffId))
            db.StaffMembers.Add(new StaffMember(staffId, vertical.BusinessId, "Profesional de prueba"));
        if (!await db.StaffServices.AnyAsync(x => x.StaffMemberId == staffId && x.ServiceId == serviceId))
            db.StaffServices.Add(new StaffService(vertical.BusinessId, staffId, serviceId));
    }

    private static async Task EnsureQueue(AppDbContext db, Vertical vertical, DateTimeOffset now)
    {
        var definitionId = Deterministic(vertical.BusinessId, 0x71);
        if (!await db.QueueDefinitions.AnyAsync(x => x.Id == definitionId))
            db.QueueDefinitions.Add(new QueueDefinition(definitionId, vertical.BusinessId,
                "Fila de urgencias", 15, 20, "Toma tu turno y sigue el avance desde el celular.", true, now));
        // La jornada se deja abierta: el recorrido comprueba tomar turno, no abrir la fila, y eso
        // ya lo cubre la prueba de turnos que existe.
        if (!await db.QueueSessions.AnyAsync(x => x.BusinessId == vertical.BusinessId &&
                (x.Status == QueueSessionStatus.Open || x.Status == QueueSessionStatus.Paused)))
            db.QueueSessions.Add(new QueueSession(Deterministic(vertical.BusinessId, 0x72),
                vertical.BusinessId, definitionId, now));
    }

    private static async Task EnsureCatalog(AppDbContext db, Vertical vertical)
    {
        var settingsId = Deterministic(vertical.BusinessId, 0x81);
        var categoryId = Deterministic(vertical.BusinessId, 0x82);
        var productId = Deterministic(vertical.BusinessId, 0x83);
        if (!await db.PickupOrderSettings.AnyAsync(x => x.Id == settingsId))
            db.PickupOrderSettings.Add(new PickupOrderSettings(settingsId, vertical.BusinessId, true,
                "Arma tu pedido y recógelo en el local.", 15, 30, 8,
                new TimeOnly(8, 0), new TimeOnly(18, 0), 9001));
        if (!await db.ProductCategories.AnyAsync(x => x.Id == categoryId))
            db.ProductCategories.Add(new ProductCategory(categoryId, vertical.BusinessId, "Catálogo", 1));
        if (!await db.Products.AnyAsync(x => x.Id == productId))
            db.Products.Add(new Product(productId, vertical.BusinessId, categoryId,
                $"Producto de {vertical.Name}", "Artículo de prueba del recorrido.", 25000));
    }

    /// <summary>
    /// Un identificador estable derivado del negocio y de un byte que dice qué es. Fijo a propósito:
    /// una prueba que vuelve a correr encuentra sus mismas filas en lugar de sembrar otras nuevas.
    /// </summary>
    private static Guid Deterministic(Guid businessId, byte marker)
    {
        var bytes = businessId.ToByteArray();
        bytes[^1] = marker;
        return new Guid(bytes);
    }
}
