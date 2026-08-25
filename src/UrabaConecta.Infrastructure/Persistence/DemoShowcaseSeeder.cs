using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrabaConecta.Application;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;

namespace UrabaConecta.Infrastructure.Persistence;

/// <summary>
/// Los dos negocios ficticios que completan la demostración comercial junto al negocio real.
/// No reutiliza el seed histórico: sólo conoce estos identificadores y nunca enumera ni modifica
/// otros negocios. La comprobación doble de ambiente hace que activarlo en Production sea un error.
/// </summary>
public static class DemoShowcaseSeeder
{
    private const string EnabledKey = "ShowcaseSeed:Enabled";
    public const string BarberOwnerEmail = "propietario@corte.demo";
    public const string BeautyOwnerEmail = "propietario@sazon.demo";

    public static readonly Guid BarberBusinessId = Guid.Parse("77777777-7777-7777-7777-777777777701");
    public static readonly Guid BeautyBusinessId = Guid.Parse("77777777-7777-7777-7777-777777777702");

    private static readonly Guid ChigorodoId = Guid.Parse("abababab-abab-abab-abab-abababababab");
    private static readonly Guid CarepaId = Guid.Parse("acacacac-acac-acac-acac-acacacacacac");
    private static readonly Guid BarberCategoryId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdcd");
    private static readonly Guid BeautyCategoryId = Guid.Parse("cdcdcdcd-cdcd-cdcd-cdcd-cdcdcdcdcdce");

    private static readonly Guid QueueDefinitionId = Guid.Parse("77777777-7777-7777-7777-777777777710");
    private static readonly Guid PickupSettingsId = Guid.Parse("77777777-7777-7777-7777-777777777720");
    private static readonly Guid BeautyPromotionId = Guid.Parse("77777777-7777-7777-7777-777777777760");

    private static readonly Guid[] BarberServiceIds =
    [
        Guid.Parse("77777777-7777-7777-7777-777777777711"),
        Guid.Parse("77777777-7777-7777-7777-777777777712"),
        Guid.Parse("77777777-7777-7777-7777-777777777713")
    ];

    private static readonly Guid[] BeautyProductIds =
    [
        Guid.Parse("77777777-7777-7777-7777-777777777721"),
        Guid.Parse("77777777-7777-7777-7777-777777777722"),
        Guid.Parse("77777777-7777-7777-7777-777777777723"),
        Guid.Parse("77777777-7777-7777-7777-777777777724")
    ];

    public static async Task SeedDemoShowcaseAsync(this IServiceProvider services,
        IHostEnvironment environment, IConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        configuration ??= services.GetRequiredService<IConfiguration>();
        var enabled = configuration.GetValue<bool>(EnabledKey);
        if (!environment.IsEnvironment("Demo"))
        {
            if (enabled)
                throw new InvalidOperationException("ShowcaseSeed__Enabled sólo puede utilizarse en Demo.");
            return;
        }
        if (!enabled) return;

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        using var readinessGuard = db.SuppressOperationalReadinessGuardForSeeding();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("UrabaConecta.DemoShowcase");

        await EnsureTaxonomyAsync(db, cancellationToken);
        await EnsureBusinessesAsync(db, cancellationToken);
        await EnsureBarberAsync(db, cancellationToken);
        await EnsureBeautyStoreAsync(db, cancellationToken);
        await EnsurePromotionAsync(db, cancellationToken);
        await EnsureOwnersAsync(scope.ServiceProvider, db, configuration, logger, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await EnsureImagesAsync(scope.ServiceProvider, db, environment, cancellationToken);
            scope.ServiceProvider.GetRequiredService<IPublicDirectoryCache>().Invalidate();
        }
        catch (Exception ex)
        {
            // Una fotografía ausente no puede tumbar Demo. Los respaldos visuales mantienen las
            // fichas utilizables y el error queda visible en los logs del despliegue.
            logger.LogError(ex, "No se pudieron completar las imágenes del showcase. " +
                                "La aplicación seguirá arrancando con sus respaldos visuales.");
        }
    }

    private static async Task EnsureTaxonomyAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        if (!await db.Municipalities.AnyAsync(x => x.Slug == "chigorodo", cancellationToken))
            db.Municipalities.Add(new Municipality(ChigorodoId, "chigorodo", "Chigorodó"));
        if (!await db.Municipalities.AnyAsync(x => x.Slug == "carepa", cancellationToken))
            db.Municipalities.Add(new Municipality(CarepaId, "carepa", "Carepa"));
        if (!await db.Categories.AnyAsync(x => x.Slug == "barberia", cancellationToken))
            db.Categories.Add(new Category(BarberCategoryId, "barberia", "Barberías"));
        if (!await db.Categories.AnyAsync(x => x.Slug == "maquillaje-y-cosmeticos", cancellationToken))
            db.Categories.Add(new Category(BeautyCategoryId, "maquillaje-y-cosmeticos", "Maquillaje y cosméticos"));
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureBusinessesAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var chigorodo = await db.Municipalities.SingleAsync(x => x.Slug == "chigorodo", cancellationToken);
        var carepa = await db.Municipalities.SingleAsync(x => x.Slug == "carepa", cancellationToken);
        var barberCategory = await db.Categories.SingleAsync(x => x.Slug == "barberia", cancellationToken);
        var beautyCategory = await db.Categories.SingleAsync(x => x.Slug == "maquillaje-y-cosmeticos", cancellationToken);

        if (!await db.Businesses.AnyAsync(x => x.Id == BarberBusinessId, cancellationToken))
        {
            var barber = new Business(BarberBusinessId, "brio-nativo-barberia-demo",
                "Brío Nativo Barbería · DEMO", chigorodo.Id, barberCategory.Id,
                "Barbería ficticia de demostración con servicios y fila virtual.",
                "Ubicación ficticia de demostración, Chigorodó", "");
            barber.UpdateCommercialProfile(new BusinessProfileEdit(barber.Slug, barber.Name,
                chigorodo.Id, barberCategory.Id,
                "Cortes, barba y fila virtual para llegar cuando falte poco.",
                barber.Description, barber.Address, "Sector de demostración", null, null, null,
                null, null, null,
                "Este es un negocio ficticio. Toma tu turno en línea y conserva el código de seguimiento."),
                now, barber.Version);
            db.Businesses.Add(barber);
        }

        if (!await db.Businesses.AnyAsync(x => x.Id == BeautyBusinessId, cancellationToken))
        {
            var beauty = new Business(BeautyBusinessId, "lumina-coral-beauty-demo",
                "Lúmina Coral Beauty Store · DEMO", carepa.Id, beautyCategory.Id,
                "Tienda ficticia de demostración con maquillaje y cuidado personal para recoger.",
                "Ubicación ficticia de demostración, Carepa", "");
            beauty.UpdateCommercialProfile(new BusinessProfileEdit(beauty.Slug, beauty.Name,
                carepa.Id, beautyCategory.Id,
                "Maquillaje y cuidado personal: arma tu carrito y recoge tu pedido.",
                beauty.Description, beauty.Address, "Sector de demostración", null, null, null,
                null, null, null,
                "Este es un negocio ficticio. Confirma el pedido y revisa su estado con el código recibido."),
                now, beauty.Version);
            db.Businesses.Add(beauty);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureBarberAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureHoursAsync(db, BarberBusinessId, new TimeOnly(8, 0), new TimeOnly(19, 0),
            includeSunday: false, cancellationToken);

        if (!await db.BusinessModules.AnyAsync(x => x.BusinessId == BarberBusinessId &&
                x.Module == BusinessModuleKind.VirtualQueues, cancellationToken))
            db.BusinessModules.Add(new BusinessModule(BarberBusinessId,
                BusinessModuleKind.VirtualQueues, true, DateTimeOffset.UtcNow));

        var services = new[]
        {
            new Service(BarberServiceIds[0], BarberBusinessId, "Corte clásico y fade", 40, 30000,
                "Corte personalizado con acabado y peinado.", 1),
            new Service(BarberServiceIds[1], BarberBusinessId, "Perfilado de barba", 25, 18000,
                "Diseño, perfilado y acabado de barba.", 2),
            new Service(BarberServiceIds[2], BarberBusinessId, "Combo corte + barba", 60, 42000,
                "Servicio completo de corte y perfilado.", 3)
        };
        foreach (var service in services)
            if (!await db.Services.AnyAsync(x => x.Id == service.Id, cancellationToken))
                db.Services.Add(service);

        if (!await db.QueueDefinitions.AnyAsync(x => x.Id == QueueDefinitionId, cancellationToken))
            db.QueueDefinitions.Add(new QueueDefinition(QueueDefinitionId, BarberBusinessId,
                "Fila barbería", 25, 18,
                "Toma tu turno desde el celular y revisa cuántas personas faltan.", true));
        await db.SaveChangesAsync(cancellationToken);

        if (!await db.QueueSessions.AnyAsync(x => x.BusinessId == BarberBusinessId &&
                (x.Status == QueueSessionStatus.Open || x.Status == QueueSessionStatus.Paused), cancellationToken))
            db.QueueSessions.Add(new QueueSession(Guid.NewGuid(), BarberBusinessId,
                QueueDefinitionId, DateTimeOffset.UtcNow));
    }

    private static async Task EnsureBeautyStoreAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        await EnsureHoursAsync(db, BeautyBusinessId, new TimeOnly(9, 0), new TimeOnly(18, 30),
            includeSunday: true, cancellationToken);

        if (!await db.BusinessModules.AnyAsync(x => x.BusinessId == BeautyBusinessId &&
                x.Module == BusinessModuleKind.PickupOrders, cancellationToken))
            db.BusinessModules.Add(new BusinessModule(BeautyBusinessId,
                BusinessModuleKind.PickupOrders, true, DateTimeOffset.UtcNow));

        if (!await db.PickupOrderSettings.AnyAsync(x => x.Id == PickupSettingsId, cancellationToken))
            db.PickupOrderSettings.Add(new PickupOrderSettings(PickupSettingsId, BeautyBusinessId, true,
                "Arma tu pedido y recógelo en el horario elegido.", 25, 15, 6,
                new TimeOnly(9, 0), new TimeOnly(18, 30), 7001));

        var makeupId = Guid.Parse("77777777-7777-7777-7777-777777777730");
        var careId = Guid.Parse("77777777-7777-7777-7777-777777777731");
        if (!await db.ProductCategories.AnyAsync(x => x.Id == makeupId, cancellationToken))
            db.ProductCategories.Add(new ProductCategory(makeupId, BeautyBusinessId, "Maquillaje", 1));
        if (!await db.ProductCategories.AnyAsync(x => x.Id == careId, cancellationToken))
            db.ProductCategories.Add(new ProductCategory(careId, BeautyBusinessId, "Cuidado personal", 2));

        var products = new[]
        {
            new Product(BeautyProductIds[0], BeautyBusinessId, makeupId, "Labial Coral Satín",
                "Color coral de acabado satinado.", 29900, 1),
            new Product(BeautyProductIds[1], BeautyBusinessId, makeupId, "Paleta Tropical 6 tonos",
                "Seis tonos cálidos para looks de día o noche.", 49900, 2),
            new Product(BeautyProductIds[2], BeautyBusinessId, careId, "Sérum facial vitamina C",
                "Sérum ligero de uso diario, presentación DEMO.", 39900, 1),
            new Product(BeautyProductIds[3], BeautyBusinessId, careId, "Mascarilla nutritiva capilar",
                "Cuidado intensivo para el cabello, presentación DEMO.", 34900, 2)
        };
        foreach (var product in products)
            if (!await db.Products.AnyAsync(x => x.Id == product.Id, cancellationToken))
                db.Products.Add(product);
    }

    private static async Task EnsurePromotionAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var promotion = await db.BusinessPromotions.SingleOrDefaultAsync(x => x.Id == BeautyPromotionId,
            cancellationToken);
        if (promotion is null)
        {
            db.BusinessPromotions.Add(new BusinessPromotion(BeautyPromotionId, BeautyBusinessId,
                "¿Buscas un detalle para hoy?", "Labiales y paletas DEMO disponibles para recoger en Carepa.",
                "Ver tienda", "/negocios/lumina-coral-beauty-demo/pedidos", now.AddHours(-1),
                now.AddDays(14), true, now));
        }
        else if (!promotion.IsCurrent(now))
        {
            promotion.Update("¿Buscas un detalle para hoy?",
                "Labiales y paletas DEMO disponibles para recoger en Carepa.", "Ver tienda",
                "/negocios/lumina-coral-beauty-demo/pedidos", now.AddHours(-1), now.AddDays(14),
                true, promotion.Version, now);
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task EnsureHoursAsync(AppDbContext db, Guid businessId, TimeOnly from,
        TimeOnly until, bool includeSunday, CancellationToken cancellationToken)
    {
        if (await db.BusinessHours.AnyAsync(x => x.BusinessId == businessId, cancellationToken)) return;
        var days = includeSunday
            ? Enum.GetValues<DayOfWeek>()
            : Enum.GetValues<DayOfWeek>().Where(x => x != DayOfWeek.Sunday);
        foreach (var day in days)
            db.BusinessHours.Add(new BusinessHour(Guid.NewGuid(), businessId, day, from, until));
    }

    private static async Task EnsureOwnersAsync(IServiceProvider services, AppDbContext db,
        IConfiguration configuration, ILogger logger, CancellationToken cancellationToken)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        if (!await roles.RoleExistsAsync("BusinessOwner"))
            EnsureSucceeded(await roles.CreateAsync(new IdentityRole<Guid>("BusinessOwner")));

        var password = configuration["ShowcaseSeed:BusinessPassword"]
                       ?? configuration["DemoAccess:SharedPassword"]
                       ?? configuration["DemoSeed:BusinessPassword"];
        await EnsureOwnerAsync(users, db, BarberOwnerEmail, "Operación Brío Nativo",
            BarberBusinessId, password, logger, cancellationToken);
        await EnsureOwnerAsync(users, db, BeautyOwnerEmail, "Operación Lúmina Coral",
            BeautyBusinessId, password, logger, cancellationToken);
    }

    private static async Task EnsureOwnerAsync(UserManager<ApplicationUser> users, AppDbContext db,
        string email, string displayName, Guid businessId, string? password, ILogger logger,
        CancellationToken cancellationToken)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("El showcase público quedó listo, pero no se creó el acceso Owner {Email}: " +
                                  "falta ShowcaseSeed__BusinessPassword (o un secreto Demo compatible).", email);
                return;
            }
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(), UserName = email, Email = email, EmailConfirmed = true,
                DisplayName = displayName, MustChangePassword = false
            };
            EnsureSucceeded(await users.CreateAsync(user, password));
        }
        if (!await users.IsInRoleAsync(user, "BusinessOwner"))
            EnsureSucceeded(await users.AddToRoleAsync(user, "BusinessOwner"));

        var membership = await db.BusinessMemberships.SingleOrDefaultAsync(
            x => x.BusinessId == businessId && x.UserId == user.Id, cancellationToken);
        if (membership is null)
            db.BusinessMemberships.Add(new BusinessMembership(Guid.NewGuid(), businessId,
                user.Id, MembershipRole.Owner));
        else
        {
            if (!membership.IsActive)
                membership.Activate(DateTimeOffset.UtcNow, membership.Version);
            if (membership.Role != MembershipRole.Owner)
                membership.GrantOwnership(DateTimeOffset.UtcNow, membership.Version);
        }
    }

    private static async Task EnsureImagesAsync(IServiceProvider services, AppDbContext db,
        IHostEnvironment environment, CancellationToken cancellationToken)
    {
        var root = Path.Combine(environment.ContentRootPath, "DemoAssets", "Showcase");
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("No se encontró DemoAssets/Showcase en la publicación.");
        var storage = services.GetRequiredService<IObjectStorage>();
        var processor = services.GetRequiredService<IImageProcessor>();
        var assignments = new[]
        {
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777741"), BarberBusinessId,
                BusinessImageKind.Logo, "barber-logo.webp", "Emblema ficticio de Brío Nativo Barbería DEMO"),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777742"), BarberBusinessId,
                BusinessImageKind.Cover, "barber-cover.webp", "Interior ficticio de Brío Nativo Barbería DEMO"),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777743"), BarberBusinessId,
                BusinessImageKind.Service, "barber-fade.webp", "Corte clásico y fade de demostración", BarberServiceIds[0]),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777744"), BarberBusinessId,
                BusinessImageKind.Service, "barber-beard.webp", "Perfilado de barba de demostración", BarberServiceIds[1]),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777745"), BarberBusinessId,
                BusinessImageKind.Service, "barber-combo.webp", "Herramientas del combo corte y barba DEMO", BarberServiceIds[2]),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777751"), BeautyBusinessId,
                BusinessImageKind.Logo, "beauty-logo.webp", "Emblema ficticio de Lúmina Coral Beauty Store DEMO"),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777752"), BeautyBusinessId,
                BusinessImageKind.Cover, "beauty-cover.webp", "Tienda ficticia Lúmina Coral Beauty Store DEMO"),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777753"), BeautyBusinessId,
                BusinessImageKind.Product, "beauty-lipstick.webp", "Labial Coral Satín de demostración", ProductId: BeautyProductIds[0]),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777754"), BeautyBusinessId,
                BusinessImageKind.Product, "beauty-palette.webp", "Paleta Tropical de seis tonos DEMO", ProductId: BeautyProductIds[1]),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777755"), BeautyBusinessId,
                BusinessImageKind.Product, "beauty-serum.webp", "Sérum facial vitamina C de demostración", ProductId: BeautyProductIds[2]),
            new ImageSeed(Guid.Parse("77777777-7777-7777-7777-777777777756"), BeautyBusinessId,
                BusinessImageKind.Product, "beauty-mask.webp", "Mascarilla nutritiva capilar de demostración", ProductId: BeautyProductIds[3])
        };

        foreach (var item in assignments)
        {
            var existing = await db.BusinessImages.SingleOrDefaultAsync(x => x.Id == item.Id, cancellationToken);
            var file = Path.Combine(root, item.File);
            if (existing is not null)
            {
                if (!existing.IsDeleted && await storage.OpenReadAsync(existing.StorageKey, cancellationToken) is null)
                {
                    var recovered = processor.Normalize(await File.ReadAllBytesAsync(file, cancellationToken), item.Kind);
                    await storage.PutAsync(existing.StorageKey, recovered.Content,
                        recovered.ContentType, cancellationToken);
                }
                continue;
            }
            if (await db.BusinessImages.AnyAsync(x => !x.IsDeleted && x.BusinessId == item.BusinessId &&
                    (x.Kind == item.Kind && (item.Kind == BusinessImageKind.Logo ||
                                             item.Kind == BusinessImageKind.Cover) ||
                     item.ServiceId != null && x.ServiceId == item.ServiceId ||
                     item.ProductId != null && x.ProductId == item.ProductId), cancellationToken))
                continue;

            var normalized = processor.Normalize(await File.ReadAllBytesAsync(file, cancellationToken), item.Kind);
            var key = $"businesses/{item.BusinessId:N}/{item.Kind.ToString().ToLowerInvariant()}/showcase-{item.Id:N}.webp";
            await storage.PutAsync(key, normalized.Content, normalized.ContentType, cancellationToken);
            db.BusinessImages.Add(new BusinessImage(item.Id, item.BusinessId, item.Kind, key,
                normalized.ContentType, normalized.Width, normalized.Height, normalized.Content.LongLength,
                item.AltText, 0, DateTimeOffset.UtcNow, item.ServiceId, item.ProductId));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void EnsureSucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("ASP.NET Identity rechazó la creación del acceso Owner del showcase.");
    }

    private sealed record ImageSeed(Guid Id, Guid BusinessId, BusinessImageKind Kind, string File,
        string AltText, Guid? ServiceId = null, Guid? ProductId = null);
}
