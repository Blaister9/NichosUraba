using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using UrabaConecta.Domain;
using UrabaConecta.Infrastructure.Identity;

namespace UrabaConecta.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Municipality> Municipalities => Set<Municipality>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<BusinessModule> BusinessModules => Set<BusinessModule>();
    public DbSet<PlatformAuditEntry> PlatformAuditEntries => Set<PlatformAuditEntry>();
    public DbSet<BusinessMembership> BusinessMemberships => Set<BusinessMembership>();
    public DbSet<MembershipAuditEntry> MembershipAuditEntries => Set<MembershipAuditEntry>();
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffService> StaffServices => Set<StaffService>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentDepositAudit> AppointmentDepositAudits => Set<AppointmentDepositAudit>();
    public DbSet<ConsentReceipt> ConsentReceipts => Set<ConsentReceipt>();
    public DbSet<QueueDefinition> QueueDefinitions => Set<QueueDefinition>();
    public DbSet<QueueSession> QueueSessions => Set<QueueSession>();
    public DbSet<QueueTicket> QueueTickets => Set<QueueTicket>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PickupOrderSettings> PickupOrderSettings => Set<PickupOrderSettings>();
    public DbSet<PickupOrder> PickupOrders => Set<PickupOrder>();
    public DbSet<PickupOrderLine> PickupOrderLines => Set<PickupOrderLine>();
    public DbSet<BusinessImage> BusinessImages => Set<BusinessImage>();
    public DbSet<WebPushSubscription> WebPushSubscriptions => Set<WebPushSubscription>();
    public DbSet<BusinessPromotion> BusinessPromotions => Set<BusinessPromotion>();
    public DbSet<AccessInvitation> AccessInvitations => Set<AccessInvitation>();
    public DbSet<BusinessStatusChange> BusinessStatusChanges => Set<BusinessStatusChange>();
    public DbSet<PlatformAccessAudit> PlatformAccessAudits => Set<PlatformAccessAudit>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresExtension("btree_gist");

        builder.Entity<Municipality>(x =>
        {
            x.ToTable("municipalities"); x.HasKey(e => e.Id); x.HasIndex(e => e.Slug).IsUnique();
            x.Property(e => e.Slug).HasMaxLength(80); x.Property(e => e.Name).HasMaxLength(100);
        });
        builder.Entity<Category>(x =>
        {
            x.ToTable("categories"); x.HasKey(e => e.Id); x.HasIndex(e => e.Slug).IsUnique();
            x.Property(e => e.Slug).HasMaxLength(80); x.Property(e => e.Name).HasMaxLength(100);
        });
        builder.Entity<Business>(x =>
        {
            x.ToTable("businesses"); x.HasKey(e => e.Id); x.HasIndex(e => e.Slug).IsUnique();
            x.HasIndex(e => new { e.MunicipalityId, e.CategoryId, e.Status, e.IsPublished });
            x.Property(e => e.Slug).HasMaxLength(120); x.Property(e => e.Name).HasMaxLength(160);
            x.Property(e => e.Description).HasMaxLength(600); x.Property(e => e.Address).HasMaxLength(240);
            x.Property(e => e.PublicPhone).HasMaxLength(30); x.Property(e => e.TimeZoneId).HasMaxLength(80);
            x.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            x.Property(e => e.WhatsAppUrl).HasMaxLength(500); x.Property(e => e.LocationUrl).HasMaxLength(500);
            x.Property(e => e.SuspensionReason).HasMaxLength(240); x.Property(e => e.Version).IsConcurrencyToken();
            x.Property(e => e.ShortDescription).HasMaxLength(160).HasDefaultValue("");
            x.Property(e => e.ReferencePoint).HasMaxLength(160);
            x.Property(e => e.PublicEmail).HasMaxLength(160);
            x.Property(e => e.InstagramUrl).HasMaxLength(500);
            x.Property(e => e.FacebookUrl).HasMaxLength(500);
            x.Property(e => e.CustomerInstructions).HasMaxLength(600);
            x.Property(e => e.ReviewNotes).HasMaxLength(400);
            x.HasOne<Municipality>().WithMany().HasForeignKey(e => e.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<Category>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<BusinessModule>(x =>
        {
            x.ToTable("business_modules"); x.HasKey(e => new { e.BusinessId, e.Module });
            x.Property(e => e.Module).HasConversion<string>().HasMaxLength(32);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PlatformAuditEntry>(x =>
        {
            x.ToTable("platform_audit_entries"); x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.OccurredAtUtc });
            x.Property(e => e.Action).HasConversion<string>().HasMaxLength(48);
            x.Property(e => e.PreviousState).HasColumnType("jsonb");
            x.Property(e => e.NewState).HasColumnType("jsonb");
            x.Property(e => e.CorrelationId).HasMaxLength(64);
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<BusinessMembership>(x =>
        {
            x.ToTable("business_memberships"); x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.UserId }).IsUnique();
            x.HasIndex(e => new { e.UserId, e.IsActive });
            x.HasIndex(e => new { e.BusinessId, e.IsActive });
            x.HasIndex(e => new { e.BusinessId, e.Role, e.IsActive });
            x.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<MembershipAuditEntry>(x =>
        {
            x.ToTable("membership_audit_entries"); x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.OccurredAtUtc });
            x.HasIndex(e => new { e.BusinessId, e.MembershipId, e.OccurredAtUtc });
            x.Property(e => e.Action).HasConversion<string>().HasMaxLength(40);
            x.Property(e => e.PreviousState).HasColumnType("jsonb");
            x.Property(e => e.NewState).HasColumnType("jsonb");
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<BusinessMembership>().WithMany().HasForeignKey(e => e.MembershipId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ApplicationUser>().Property(e => e.DisplayName).HasMaxLength(100);
        builder.Entity<BusinessHour>(x =>
        {
            // El check sigue garantizando que un tramo no cruce la medianoche y dure al menos un
            // minuto. El índice deja de ser único porque un día admite varios tramos: una jornada
            // partida son dos filas del mismo día.
            x.ToTable("business_hours", t => t.HasCheckConstraint("ck_business_hours_range", "\"OpensAt\" < \"ClosesAt\""));
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.Day, e.SortOrder });
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<Service>(x =>
        {
            x.ToTable("services", t =>
            {
                t.HasCheckConstraint("ck_service_duration", "\"DurationMinutes\" BETWEEN 5 AND 480");
                t.HasCheckConstraint("ck_service_price", "\"ReferencePrice\" >= 0");
                t.HasCheckConstraint("ck_service_display_order", "\"DisplayOrder\" >= 0");
            });
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.IsActive });
            x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.Property(e => e.Name).HasMaxLength(120); x.Property(e => e.Description).HasMaxLength(500);
            x.Property(e => e.ReferencePrice).HasPrecision(12, 2); x.Property(e => e.Version).IsConcurrencyToken();
            x.Property(e => e.DepositType).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(DepositType.None);
            x.Property(e => e.DepositValue).HasPrecision(12, 2).HasDefaultValue(0m);
            x.Property(e => e.RequiresDeposit).HasDefaultValue(false);
            x.Property(e => e.DepositInstructions).HasMaxLength(400).HasDefaultValue("");
            x.Property(e => e.DepositWhatsAppNumber).HasMaxLength(20).HasDefaultValue("");
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<StaffMember>(x =>
        {
            x.ToTable("staff_members"); x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.Property(e => e.DisplayName).HasMaxLength(100); x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<StaffService>(x =>
        {
            x.ToTable("staff_services"); x.HasKey(e => new { e.BusinessId, e.StaffMemberId, e.ServiceId });
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<StaffMember>().WithMany().HasForeignKey(e => new { e.BusinessId, e.StaffMemberId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Service>().WithMany().HasForeignKey(e => new { e.BusinessId, e.ServiceId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<AvailabilityException>(x =>
        {
            x.ToTable("availability_exceptions", t =>
                t.HasCheckConstraint("ck_availability_exception_range",
                    "\"Type\" = 'ClosedAllDay' OR (\"OpensAt\" IS NOT NULL AND \"ClosesAt\" IS NOT NULL AND \"OpensAt\" < \"ClosesAt\")"));
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.StaffMemberId, e.Date }).IsUnique();
            x.Property(e => e.Type).HasConversion<string>().HasMaxLength(32);
            x.Property(e => e.Reason).HasMaxLength(160);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<StaffMember>().WithMany().HasForeignKey(e => new { e.BusinessId, e.StaffMemberId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<ConsentReceipt>(x =>
        {
            x.ToTable("consent_receipts"); x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.AcceptedAtUtc });
            x.HasIndex(e => e.AppointmentId).IsUnique(); x.Property(e => e.NoticeVersion).HasMaxLength(40);
            x.HasIndex(e => e.PickupOrderId).IsUnique();
            x.HasIndex(e => e.QueueTicketId).IsUnique();
            x.Property(e => e.Purpose).HasMaxLength(240);
            x.Property(e => e.IpAddress).HasMaxLength(45);
            x.HasOne<QueueTicket>().WithOne().HasForeignKey<ConsentReceipt>(e => e.QueueTicketId)
                .OnDelete(DeleteBehavior.Restrict);
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Appointment>(x =>
        {
            x.ToTable("appointments", t =>
            {
                t.HasCheckConstraint("ck_appointment_range", "\"StartAtUtc\" < \"EndAtUtc\"");
                t.HasCheckConstraint("ck_appointment_duration", "\"DurationMinutes\" BETWEEN 5 AND 480");
            });
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.StartAtUtc });
            x.HasIndex(e => e.PublicCodeHash).IsUnique();
            x.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            x.Property(e => e.ServiceName).HasMaxLength(120); x.Property(e => e.DisplayPrice).HasPrecision(12, 2);
            x.Property(e => e.ProtectedCustomerAlias).HasMaxLength(1000);
            x.Property(e => e.ProtectedPhone).HasMaxLength(1000); x.Property(e => e.PhoneLast4).HasMaxLength(4);
            x.Property(e => e.ProtectedNotes).HasMaxLength(2000); x.Property(e => e.PublicCodeHash).HasMaxLength(64);
            x.Property(e => e.RejectionReason).HasMaxLength(160);
            // Copia congelada del adelanto. El estado por omisión deja las citas anteriores como
            // NotRequired sin tener que tocarlas.
            x.Property(e => e.DepositStatus).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(DepositStatus.NotRequired);
            x.Property(e => e.DepositType).HasConversion<string>().HasMaxLength(20)
                .HasDefaultValue(DepositType.None);
            x.Property(e => e.DepositConfiguredValue).HasPrecision(12, 2).HasDefaultValue(0m);
            x.Property(e => e.DepositAmount).HasPrecision(12, 2).HasDefaultValue(0m);
            x.Property(e => e.DepositInstructions).HasMaxLength(400).HasDefaultValue("");
            x.Property(e => e.DepositWhatsAppNumber).HasMaxLength(20).HasDefaultValue("");
            x.Property(e => e.DepositRejectionReason).HasMaxLength(160);
            x.HasIndex(e => new { e.BusinessId, e.DepositStatus });
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<Service>().WithMany().HasForeignKey(e => new { e.BusinessId, e.ServiceId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<StaffMember>().WithMany().HasForeignKey(e => new { e.BusinessId, e.StaffMemberId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<ConsentReceipt>().WithOne().HasForeignKey<Appointment>(e => e.ConsentReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<AppointmentDepositAudit>(x =>
        {
            x.ToTable("appointment_deposit_audits"); x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.AppointmentId, e.OccurredAtUtc });
            x.HasIndex(e => new { e.BusinessId, e.OccurredAtUtc });
            x.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(20);
            x.Property(e => e.PreviousStatus).HasConversion<string>().HasMaxLength(20);
            x.Property(e => e.NewStatus).HasConversion<string>().HasMaxLength(20);
            x.Property(e => e.Reason).HasMaxLength(160);
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Appointment>().WithMany().HasForeignKey(e => e.AppointmentId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<QueueDefinition>(x =>
        {
            x.ToTable("queue_definitions", t =>
            {
                t.HasCheckConstraint("ck_queue_definition_duration", "\"AverageDurationMinutes\" BETWEEN 1 AND 480");
                t.HasCheckConstraint("ck_queue_definition_capacity", "\"MaximumWaiting\" BETWEEN 1 AND 500");
            });
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.HasIndex(e => e.BusinessId).IsUnique().HasFilter("\"IsActive\" = TRUE");
            x.Property(e => e.Name).HasMaxLength(80);
            x.Property(e => e.PublicMessage).HasMaxLength(160);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QueueSession>(x =>
        {
            x.ToTable("queue_sessions");
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.HasIndex(e => e.QueueDefinitionId).IsUnique()
                .HasFilter("\"Status\" IN ('Open', 'Paused')");
            x.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<QueueDefinition>().WithMany().HasForeignKey(e => new { e.BusinessId, e.QueueDefinitionId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<QueueTicket>(x =>
        {
            x.ToTable("queue_tickets");
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.QueueSessionId, e.Number }).IsUnique();
            x.HasIndex(e => e.PublicCodeHash).IsUnique();
            x.HasIndex(e => new { e.BusinessId, e.Status });
            x.HasIndex(e => new { e.QueueSessionId, e.Status, e.Number });
            x.Property(e => e.Status).HasConversion<string>().HasMaxLength(16);
            x.Property(e => e.Source).HasConversion<string>().HasMaxLength(16);
            x.Property(e => e.PublicCodeHash).HasMaxLength(64);
            x.Property(e => e.ProtectedAlias).HasMaxLength(1000);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<QueueSession>().WithMany().HasForeignKey(e => new { e.BusinessId, e.QueueSessionId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ProductCategory>(x =>
        {
            x.ToTable("ordering_product_categories", t =>
                t.HasCheckConstraint("ck_product_category_order", "\"DisplayOrder\" >= 0"));
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.HasIndex(e => new { e.BusinessId, e.IsActive, e.DisplayOrder });
            x.Property(e => e.Name).HasMaxLength(100); x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Product>(x =>
        {
            x.ToTable("ordering_products", t =>
            {
                t.HasCheckConstraint("ck_product_price", "\"ReferencePrice\" >= 0");
                t.HasCheckConstraint("ck_product_order", "\"DisplayOrder\" >= 0");
            });
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.HasIndex(e => new { e.BusinessId, e.ProductCategoryId, e.IsActive, e.DisplayOrder });
            x.Property(e => e.Name).HasMaxLength(120); x.Property(e => e.Description).HasMaxLength(500);
            x.Property(e => e.IsAvailable).HasDefaultValue(true);
            x.Property(e => e.ReferencePrice).HasPrecision(12, 2); x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<ProductCategory>().WithMany().HasForeignKey(e => new { e.BusinessId, e.ProductCategoryId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PickupOrderSettings>(x =>
        {
            x.ToTable("ordering_pickup_settings", t =>
            {
                t.HasCheckConstraint("ck_pickup_settings_range", "\"ReceivesFrom\" < \"ReceivesUntil\"");
                t.HasCheckConstraint("ck_pickup_settings_capacity", "\"MaximumActivePerSlot\" BETWEEN 1 AND 500");
            });
            x.HasKey(e => e.Id); x.HasIndex(e => e.BusinessId).IsUnique();
            x.Property(e => e.PublicMessage).HasMaxLength(200); x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PickupOrder>(x =>
        {
            x.ToTable("ordering_pickup_orders", t =>
            {
                t.HasCheckConstraint("ck_pickup_order_range", "\"PickupStartUtc\" < \"PickupEndUtc\"");
                t.HasCheckConstraint("ck_pickup_order_totals", "\"Subtotal\" >= 0 AND \"Total\" >= 0");
            });
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.Id }).IsUnique();
            x.HasIndex(e => new { e.BusinessId, e.PublicOrderNumber }).IsUnique();
            x.HasIndex(e => e.PublicCodeHash).IsUnique();
            x.HasIndex(e => new { e.BusinessId, e.Status, e.CreatedAtUtc });
            x.HasIndex(e => new { e.BusinessId, e.PickupStartUtc, e.Status });
            x.Property(e => e.Status).HasConversion<string>().HasMaxLength(24);
            x.Property(e => e.ProtectedCustomerAlias).HasMaxLength(1000);
            x.Property(e => e.ProtectedCustomerPhone).HasMaxLength(1000);
            x.Property(e => e.PhoneLast4).HasMaxLength(4); x.Property(e => e.ProtectedNotes).HasMaxLength(2000);
            x.Property(e => e.PublicCodeHash).HasMaxLength(64); x.Property(e => e.ConsentVersion).HasMaxLength(40);
            x.Property(e => e.CancellationReason).HasMaxLength(160);
            x.Property(e => e.Subtotal).HasPrecision(12, 2); x.Property(e => e.Total).HasPrecision(12, 2);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasMany(e => e.Lines).WithOne().HasForeignKey(e => new { e.BusinessId, e.PickupOrderId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<PickupOrderLine>(x =>
        {
            x.ToTable("ordering_pickup_order_lines", t =>
            {
                t.HasCheckConstraint("ck_pickup_line_quantity", "\"Quantity\" BETWEEN 1 AND 20");
                t.HasCheckConstraint("ck_pickup_line_prices", "\"UnitPriceSnapshot\" >= 0 AND \"LineTotal\" >= 0");
            });
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.PickupOrderId });
            x.Property(e => e.ProductNameSnapshot).HasMaxLength(120);
            x.Property(e => e.ProtectedNotes).HasMaxLength(1000);
            x.Property(e => e.UnitPriceSnapshot).HasPrecision(12, 2); x.Property(e => e.LineTotal).HasPrecision(12, 2);
            x.HasOne<Product>().WithMany().HasForeignKey(e => new { e.BusinessId, e.ProductId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<ConsentReceipt>().HasOne<PickupOrder>().WithOne()
            .HasForeignKey<ConsentReceipt>(e => e.PickupOrderId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<BusinessImage>(x =>
        {
            x.ToTable("business_images", t =>
            {
                t.HasCheckConstraint("ck_business_image_size", "\"Width\" > 0 AND \"Height\" > 0 AND \"ByteSize\" > 0");
                t.HasCheckConstraint("ck_business_image_order", "\"DisplayOrder\" >= 0");
            });
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.Kind, e.IsDeleted });
            // Logo y portada son únicos entre las imágenes vigentes de cada negocio.
            x.HasIndex(e => new { e.BusinessId, e.Kind }).IsUnique()
                .HasFilter("\"IsDeleted\" = FALSE AND \"Kind\" IN ('Logo', 'Cover')");
            // Y una sola foto vigente por fila del catálogo, por la misma razón: la tarjeta de un
            // servicio muestra una, y con dos vigentes cuál gana dependería del orden de lectura.
            x.HasIndex(e => e.ServiceId).IsUnique().HasFilter("\"IsDeleted\" = FALSE AND \"ServiceId\" IS NOT NULL");
            x.HasIndex(e => e.ProductId).IsUnique().HasFilter("\"IsDeleted\" = FALSE AND \"ProductId\" IS NOT NULL");
            x.HasIndex(e => e.StorageKey).IsUnique();
            x.Property(e => e.Kind).HasConversion<string>().HasMaxLength(16);
            // Borrar un servicio o un producto se lleva su foto: dejarla huérfana sólo acumularía
            // objetos que ya nadie sabe a qué pertenecían.
            x.HasOne<Service>().WithMany().HasForeignKey(e => e.ServiceId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<Product>().WithMany().HasForeignKey(e => e.ProductId).OnDelete(DeleteBehavior.Cascade);
            x.Property(e => e.StorageKey).HasMaxLength(400);
            x.Property(e => e.ContentType).HasMaxLength(60);
            x.Property(e => e.AltText).HasMaxLength(160);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<WebPushSubscription>(x =>
        {
            x.ToTable("web_push_subscriptions");
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.EndpointHash, e.Audience, e.ScopeKey }).IsUnique();
            x.HasIndex(e => new { e.BusinessId, e.Audience, e.IsActive });
            x.HasIndex(e => new { e.EntityId, e.Audience, e.IsActive });
            x.HasIndex(e => new { e.UserId, e.BusinessId, e.IsActive });
            x.Property(e => e.Audience).HasConversion<string>().HasMaxLength(24);
            x.Property(e => e.ScopeKey).HasMaxLength(64);
            x.Property(e => e.EndpointHash).HasMaxLength(64);
            x.Property(e => e.Endpoint).HasMaxLength(2048);
            x.Property(e => e.P256dh).HasMaxLength(256);
            x.Property(e => e.Auth).HasMaxLength(256);
            x.Property(e => e.ProtectedDeepLink).HasMaxLength(2000);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<BusinessPromotion>(x =>
        {
            x.ToTable("business_promotions");
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.IsActive, e.StartsAtUtc, e.EndsAtUtc });
            x.HasIndex(e => new { e.BusinessId, e.PushSentAtUtc });
            x.Property(e => e.Headline).HasMaxLength(90);
            x.Property(e => e.Body).HasMaxLength(220);
            x.Property(e => e.CtaLabel).HasMaxLength(32);
            x.Property(e => e.DeepLink).HasMaxLength(500);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<AccessInvitation>(x =>
        {
            x.ToTable("access_invitations");
            x.HasKey(e => e.Id);
            // El token nunca se guarda en claro: sólo el hash, y su unicidad es la que se consulta.
            x.HasIndex(e => e.TokenHash).IsUnique();
            x.HasIndex(e => new { e.Email, e.ExpiresAtUtc });
            x.HasIndex(e => new { e.BusinessId, e.CreatedAtUtc });
            x.Property(e => e.Email).HasMaxLength(160);
            x.Property(e => e.DisplayName).HasMaxLength(100);
            x.Property(e => e.TokenHash).HasMaxLength(128);
            x.Property(e => e.Grant).HasConversion<string>().HasMaxLength(24);
            x.Property(e => e.Purpose).HasConversion<string>().HasMaxLength(24);
            x.Property(e => e.Version).IsConcurrencyToken();
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<BusinessStatusChange>(x =>
        {
            x.ToTable("business_status_changes");
            x.HasKey(e => e.Id);
            x.HasIndex(e => new { e.BusinessId, e.OccurredAtUtc });
            x.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(24);
            x.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(24);
            x.Property(e => e.Notes).HasMaxLength(400);
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PlatformAccessAudit>(x =>
        {
            x.ToTable("platform_access_audits");
            x.HasKey(e => e.Id);
            x.HasIndex(e => e.OccurredAtUtc);
            x.HasIndex(e => new { e.BusinessId, e.OccurredAtUtc });
            x.Property(e => e.Action).HasConversion<string>().HasMaxLength(48);
            x.Property(e => e.Entity).HasMaxLength(80);
            x.Property(e => e.EntityId).HasMaxLength(64);
            x.Property(e => e.Summary).HasMaxLength(400);
            x.Property(e => e.IpAddress).HasMaxLength(45);
            x.Property(e => e.CorrelationId).HasMaxLength(64);
            // Sin clave foránea: un reinicio de acceso puede no pertenecer a ningún negocio.
        });
    }
}
