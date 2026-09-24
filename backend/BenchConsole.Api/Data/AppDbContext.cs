using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Bench> Benches => Set<Bench>();
    public DbSet<BenchCommand> Commands => Set<BenchCommand>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();
    public DbSet<Alert> Alerts => Set<Alert>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Bench>(e =>
        {
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(64).IsRequired();
            // Tên biến thể dài hơn hẳn 'vf6': 'VF8New ME', 'VF9VN'…
            e.Property(x => x.Model).HasMaxLength(64).IsRequired();
            e.Property(x => x.TopicPrefix).HasMaxLength(128).IsRequired();
            e.Property(x => x.Workshop).HasMaxLength(64);
            e.Property(x => x.Rack).HasMaxLength(64);
            e.Property(x => x.Firmware).HasMaxLength(32);
            e.Property(x => x.TenMay).HasMaxLength(64);
            e.Property(x => x.PrimaryChannel).HasMaxLength(64);
            e.Property(x => x.PrimaryUnit).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(256);
        });

        b.Entity<BenchCommand>(e =>
        {
            // Ghép ack với lệnh bằng cmd_id, nên phải tra nhanh và không trùng.
            e.HasIndex(x => x.CmdId).IsUnique();
            e.Property(x => x.CmdId).HasMaxLength(64).IsRequired();
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.TestCase).HasMaxLength(128);
            e.Property(x => x.Plan).HasMaxLength(64);
            e.Property(x => x.IssuedBy).HasMaxLength(128);
            e.Property(x => x.RejectReason).HasMaxLength(256);
            e.HasOne(x => x.Bench).WithMany(x => x.Commands).HasForeignKey(x => x.BenchId);
        });

        b.Entity<Run>(e =>
        {
            e.HasIndex(x => new { x.BenchId, x.FinishedAt });
            e.Property(x => x.TestCase).HasMaxLength(128).IsRequired();
            e.Property(x => x.Plan).HasMaxLength(64);
            e.Property(x => x.CmdId).HasMaxLength(64);
            e.Property(x => x.Reason).HasMaxLength(128);
            e.Property(x => x.RunBy).HasMaxLength(128);
            e.HasOne(x => x.Bench).WithMany(x => x.Runs).HasForeignKey(x => x.BenchId);
        });

        b.Entity<TelemetrySample>(e =>
        {
            // Truy vấn chính: lấy 5 phút gần nhất của một bench + một kênh, nên index theo đúng thứ tự đó.
            e.HasIndex(x => new { x.BenchId, x.Channel, x.At });
            e.Property(x => x.Channel).HasMaxLength(64).IsRequired();
        });

        b.Entity<Alert>(e =>
        {
            e.HasIndex(x => new { x.BenchId, x.ClosedAt });
            e.Property(x => x.Kind).HasMaxLength(64).IsRequired();
            e.Property(x => x.Message).HasMaxLength(512).IsRequired();
            e.Property(x => x.AcknowledgedBy).HasMaxLength(128);
            e.HasOne(x => x.Bench).WithMany().HasForeignKey(x => x.BenchId);
        });
    }
}
