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
    public DbSet<BusinessHour> BusinessHours => Set<BusinessHour>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffService> StaffServices => Set<StaffService>();
    public DbSet<AvailabilityException> AvailabilityExceptions => Set<AvailabilityException>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<ConsentReceipt> ConsentReceipts => Set<ConsentReceipt>();

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
            x.Property(e => e.Role).HasConversion<string>().HasMaxLength(20);
            x.HasOne<Business>().WithMany().HasForeignKey(e => e.BusinessId).OnDelete(DeleteBehavior.Cascade);
            x.HasOne<ApplicationUser>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });
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
    }
}
