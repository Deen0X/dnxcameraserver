using DNXCameraServer.Domain;
using Microsoft.EntityFrameworkCore;

namespace DNXCameraServer.Data;

public class CameraDbContext(DbContextOptions<CameraDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<XiaomiAccount> Accounts => Set<XiaomiAccount>();
    public DbSet<Camera> Cameras => Set<Camera>();
    public DbSet<CameraGrant> Grants => Set<CameraGrant>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<AuditEvent> Audit => Set<AuditEvent>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite no puede ordenar por DateTimeOffset; se guarda como texto UTC.
        builder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.Role).HasMaxLength(16);
        });

        b.Entity<XiaomiAccount>(e =>
        {
            e.HasIndex(x => new { x.XiaomiUserId, x.Region }).IsUnique();
            e.Property(x => x.XiaomiUserId).HasMaxLength(64);
            e.Property(x => x.Region).HasMaxLength(8);
        });

        b.Entity<Camera>(e =>
        {
            e.HasIndex(x => x.StreamName).IsUnique();
            e.HasIndex(x => x.AccountId);
            e.HasIndex(x => x.Did);
            e.Property(x => x.Name).HasMaxLength(128);
            e.Property(x => x.StreamName).HasMaxLength(64);
            e.HasOne(x => x.Account).WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<CameraGrant>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CameraId }).IsUnique();
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Camera).WithMany().HasForeignKey(x => x.CameraId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<DeviceToken>(e =>
        {
            e.HasIndex(x => x.TokenId).IsUnique();
            e.HasIndex(x => x.CameraId);
            e.Property(x => x.TokenId).HasMaxLength(32);
            e.HasOne(x => x.Camera).WithMany().HasForeignKey(x => x.CameraId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.CameraId);
            e.Property(x => x.Action).HasMaxLength(32);
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(64);
        });
    }
}
