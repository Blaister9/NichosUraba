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
    public DbSet<BusinessMembership> BusinessMemberships => Set<BusinessMembership>();
    public DbSet<MembershipAuditEntry> MembershipAuditEntries => Set<MembershipAuditEntry>();
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffService> StaffServices => Set<StaffService>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ConsentReceipt> ConsentReceipts => Set<ConsentReceipt>();
    public DbSet<QueueDefinition> QueueDefinitions => Set<QueueDefinition>();
    public DbSet<QueueSession> QueueSessions => Set<QueueSession>();
    public DbSet<QueueTicket> QueueTickets => Set<QueueTicket>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<PickupOrderSettings> PickupOrderSettings => Set<PickupOrderSettings>();
    public DbSet<PickupOrder> PickupOrders => Set<PickupOrder>();
    public DbSet<PickupOrderLine> PickupOrderLines => Set<PickupOrderLine>();

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
            x.HasOne<Municipality>().WithMany().HasForeignKey(e => e.MunicipalityId).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<Category>().WithMany().HasForeignKey(e => e.CategoryId).OnDelete(DeleteBehavior.Restrict);
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
            x.ToTable("business_hours", t => t.HasCheckConstraint("ck_business_hours_range", "\"OpensAt\" < \"ClosesAt\""));
            x.HasKey(e => e.Id); x.HasIndex(e => new { e.BusinessId, e.Day }).IsUnique();
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
            x.Property(e => e.Purpose).HasMaxLength(240);
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
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<Service>().WithMany().HasForeignKey(e => new { e.BusinessId, e.ServiceId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<StaffMember>().WithMany().HasForeignKey(e => new { e.BusinessId, e.StaffMemberId })
                .HasPrincipalKey(e => new { e.BusinessId, e.Id }).OnDelete(DeleteBehavior.Restrict);
            x.HasOne<ConsentReceipt>().WithOne().HasForeignKey<Appointment>(e => e.ConsentReceiptId)
                .OnDelete(DeleteBehavior.Restrict);
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
    }
}
