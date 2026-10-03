using BenchConsole.Core.Messaging;
using BenchConsole.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseModel> DatabaseModels => Set<DatabaseModel>();
    public DbSet<DatabaseCategory> DatabaseCategories => Set<DatabaseCategory>();
    public DbSet<DatabaseType> DatabaseTypes => Set<DatabaseType>();
    public DbSet<DatabaseFile> DatabaseFiles => Set<DatabaseFile>();
    public DbSet<DatabaseChange> DatabaseChanges => Set<DatabaseChange>();
    public DbSet<Bench> Benches => Set<Bench>();
    public DbSet<DuAn> DuAns => Set<DuAn>();
    public DbSet<TepDuLieuChung> TepDuLieuChungs => Set<TepDuLieuChung>();
    public DbSet<MucDuLieuChung> MucDuLieuChungs => Set<MucDuLieuChung>();
    public DbSet<ThietBiDuAn> ThietBiDuAns => Set<ThietBiDuAn>();
    public DbSet<BenchCommand> Commands => Set<BenchCommand>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<TelemetrySample> TelemetrySamples => Set<TelemetrySample>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<GoiTestCase> GoiTestCases => Set<GoiTestCase>();
    public DbSet<BaoCaoChay> BaoCaoChays => Set<BaoCaoChay>();
    public DbSet<TepNguoiDung> TepNguoiDungs => Set<TepNguoiDung>();

    public DbSet<User> Users => Set<User>();
    public DbSet<MaKhoiPhuc> MaKhoiPhucs => Set<MaKhoiPhuc>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    private static void DanhMuc<T>(ModelBuilder b) where T : class, IDatabaseDanhMuc
    {
        b.Entity<T>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Ma).HasMaxLength(32).IsRequired();
            e.Property(x => x.Ten).HasMaxLength(128).IsRequired();
            e.HasIndex(x => x.Ma).IsUnique();
            e.HasIndex(x => x.Ten).IsUnique();
        });
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        DanhMuc<DatabaseModel>(b);
        DanhMuc<DatabaseCategory>(b);
        DanhMuc<DatabaseType>(b);
        b.Entity<DatabaseFile>(e =>
        {
            e.Property(x => x.TenFile).HasMaxLength(260).IsRequired();
            e.Property(x => x.PhienBan).HasMaxLength(64).IsRequired();
            e.Property(x => x.Status).HasMaxLength(16).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.MoTa).HasMaxLength(512);
            e.Property(x => x.NguoiTaiLen).HasMaxLength(128);
            e.Property(x => x.NguoiThayDoi).HasMaxLength(128);
            e.Property(x => x.Revision).IsConcurrencyToken();
            e.HasOne(x => x.Model).WithMany().HasForeignKey(x => x.ModelId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Type).WithMany().HasForeignKey(x => x.TypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ModelId, x.CategoryId, x.TypeId, x.TenFile, x.PhienBan }).IsUnique();
            e.HasIndex(x => x.Status);
        });
        b.Entity<DatabaseChange>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(16);
            e.Property(x => x.FromStatus).HasMaxLength(16);
            e.Property(x => x.ToStatus).HasMaxLength(16);
            e.Property(x => x.NguoiThayDoi).HasMaxLength(128);
            e.HasIndex(x => new { x.FileId, x.Id });
            e.HasIndex(x => x.PublishedAt);
        });
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
            e.Property(x => x.Tang).HasMaxLength(32);
            e.Property(x => x.Ten).HasMaxLength(128);

            // Lọc theo loại là truy vấn thường xuyên nhất sau khi có ba loại
            // thiết bị trong cùng một bảng.
            e.HasIndex(x => x.Loai);

            // Xoá thiết bị chứa thì thiết bị con thành ĐỨNG RIÊNG, không bị
            // xoá theo: tháo bench đi thì con MHU vẫn còn ngoài đời. Cascade ở
            // đây là âm thầm xoá mất cả lịch sử chạy của con MHU đó.
            //
            // ClientSetNull chứ KHÔNG phải SetNull. SQL Server từ chối
            // ON DELETE SET NULL trên khoá ngoại TỰ THAM CHIẾU — lỗi 1785
            // "may cause cycles or multiple cascade paths" — migration đổ
            // ngay lúc áp và backend chết lặp. ClientSetNull giữ nguyên ý đồ
            // trên: EF gỡ liên kết cho thiết bị con ĐÃ NẠP, còn phía database
            // khai NO ACTION nên SQL Server chấp nhận. Hệ quả phải nhớ: muốn
            // xoá thiết bị chứa thì phải nạp kèm danh sách con, không thì
            // database chặn vì còn ràng buộc.
            e.HasOne(x => x.ThuocVe).WithMany(x => x.ChuaNhung)
                .HasForeignKey(x => x.ThuocVeId)
                .OnDelete(DeleteBehavior.ClientSetNull);
            e.Property(x => x.TenMay).HasMaxLength(64);
            e.Property(x => x.PrimaryChannel).HasMaxLength(64);
            e.Property(x => x.PrimaryUnit).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(256);
        });

        b.Entity<DuAn>(e =>
        {
            e.HasIndex(x => x.Ma).IsUnique();
            e.Property(x => x.Ma).HasMaxLength(64).IsRequired();
            e.Property(x => x.Ten).HasMaxLength(128).IsRequired();
            e.Property(x => x.MoTa).HasMaxLength(512);
        });

        b.Entity<ThietBiDuAn>(e =>
        {
            e.HasKey(x => new { x.BenchId, x.DuAnId });
            // Xoá thiết bị hay xoá dự án thì gỡ luôn dòng nối. Để lại là danh
            // sách dự án của thiết bị hiện ra một cái tên không còn tồn tại.
            e.HasOne(x => x.Bench).WithMany(x => x.DuAns)
                .HasForeignKey(x => x.BenchId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.DuAn).WithMany(x => x.ThietBis)
                .HasForeignKey(x => x.DuAnId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MaKhoiPhuc>(e =>
        {
            e.Property(x => x.Hash).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.UserId);
            // Xoá người dùng thì mã khôi phục đi theo — giữ lại là để sót một
            // nắm hash trỏ vào tài khoản không còn tồn tại.
            e.HasOne(x => x.User).WithMany(x => x.MaKhoiPhucs)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<MucDuLieuChung>(e =>
        {
            e.HasIndex(x => x.Ma).IsUnique();
            e.Property(x => x.Ma).HasMaxLength(LoaiDuLieuChung.DoDaiMaToiDa).IsRequired();
            e.Property(x => x.Ten).HasMaxLength(128).IsRequired();
            e.Property(x => x.MoTa).HasMaxLength(512);
        });

        b.Entity<TepDuLieuChung>(e =>
        {
            e.Property(x => x.Loai).HasMaxLength(32).IsRequired();
            e.Property(x => x.Ten).HasMaxLength(200).IsRequired();
            e.Property(x => x.TenFile).HasMaxLength(260).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.MoTa).HasMaxLength(512);
            e.Property(x => x.NguoiTaiLen).HasMaxLength(128);

            // Lọc theo loại là truy vấn duy nhất của màn này.
            e.HasIndex(x => x.Loai);

            // Trùng tên trong cùng một loại là chặn: hai file "NP_11.6.4" trong
            // mục DBC thì không ai biết bản nào đang dùng. Khác loại trùng tên
            // thì không sao, chúng là hai thứ khác nhau.
            e.HasIndex(x => new { x.Loai, x.Ten }).IsUnique();
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

        b.Entity<GoiTestCase>(e =>
        {
            // Tên gói là tên thư mục agent bung ra trên máy bench. Trùng tên là
            // gói sau đè lên gói trước, nên chặn ngay từ đây. Duy nhất theo
            // (loại, tên): gói testcase và gói config cùng tên là hai thứ khác
            // nhau, bung vào hai thư mục khác nhau, không đè nhau.
            e.HasIndex(x => new { x.Loai, x.Ten }).IsUnique();
            e.Property(x => x.Loai).HasMaxLength(16).IsRequired();
            e.Property(x => x.KieuTest).HasMaxLength(16).HasDefaultValue("auto").IsRequired();
            e.Property(x => x.Ten).HasMaxLength(TenGoi.DaiToiDa).IsRequired();
            e.Property(x => x.TenFileGoc).HasMaxLength(260).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.NguoiTaiLen).HasMaxLength(128);
        });

        b.Entity<BaoCaoChay>(e =>
        {
            // Truy vấn chính: lấy mọi file của một lệnh. Và chống trùng khi máy
            // bench gửi lại sau khi mạng đứt — cùng lệnh, cùng tên file, cùng
            // nội dung thì chỉ giữ một bản.
            e.HasIndex(x => new { x.CmdId, x.TenFile, x.Sha256 }).IsUnique();
            e.Property(x => x.CmdId).HasMaxLength(64).IsRequired();
            e.Property(x => x.BenchCode).HasMaxLength(64).IsRequired();
            e.Property(x => x.TestCase).HasMaxLength(256);
            e.Property(x => x.TenFile).HasMaxLength(260).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        });

        b.Entity<TepNguoiDung>(e =>
        {
            // Truy vấn chính: lấy kho của một người. Và chống trùng khi tải lại
            // đúng file cũ — cùng người, cùng tên, cùng nội dung thì một bản ghi.
            e.HasIndex(x => new { x.NguoiDung, x.TenFile, x.Sha256 }).IsUnique();
            e.Property(x => x.NguoiDung).HasMaxLength(128).IsRequired();
            e.Property(x => x.TenFile).HasMaxLength(260).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.MoTa).HasMaxLength(512);
        });

        // ------------------------------------------------ xác thực, phân quyền

        b.Entity<User>(e =>
        {
            e.Property(x => x.TotpBiMat).HasMaxLength(64);
            // Đăng nhập bằng email nên nó phải duy nhất. Không có index này
            // thì hai tài khoản cùng email, và đăng nhập trả về cái nào là
            // tuỳ thứ tự trong bảng.
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(256).IsRequired();
            e.Property(x => x.HoTen).HasMaxLength(128).IsRequired();
            e.Property(x => x.MatKhauHash).HasMaxLength(256).IsRequired();
            e.Property(x => x.RefreshToken).HasMaxLength(256);
        });

        b.Entity<Role>(e =>
        {
            e.HasIndex(x => x.Ma).IsUnique();
            e.Property(x => x.Ma).HasMaxLength(64).IsRequired();
            e.Property(x => x.Ten).HasMaxLength(128).IsRequired();
            e.Property(x => x.MoTa).HasMaxLength(512);
        });

        b.Entity<Permission>(e =>
        {
            e.HasIndex(x => x.Ma).IsUnique();
            e.Property(x => x.Ma).HasMaxLength(64).IsRequired();
            e.Property(x => x.Module).HasMaxLength(32).IsRequired();
            e.Property(x => x.Action).HasMaxLength(32).IsRequired();
            e.Property(x => x.Ten).HasMaxLength(256).IsRequired();
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            // Xoá user thì gỡ luôn vai trò đã gán. Để lại dòng mồ côi thì lần
            // sau tạo user trùng Id sẽ thừa hưởng quyền của người cũ.
            e.HasOne(x => x.User).WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany(x => x.UserRoles)
                .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasKey(x => new { x.RoleId, x.PermissionId });
            e.HasOne(x => x.Role).WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Permission).WithMany(x => x.RolePermissions)
                .HasForeignKey(x => x.PermissionId).OnDelete(DeleteBehavior.Cascade);
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
