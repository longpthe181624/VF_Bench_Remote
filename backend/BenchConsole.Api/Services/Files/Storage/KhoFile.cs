using System.Security.Cryptography;

namespace BenchConsole.Api.Services.Files.Storage;

/// <summary>Kết quả cất một file: băm nội dung và cỡ thật đã ghi xuống đĩa.</summary>
public record KetQuaLuuFile(string Sha256, long KichThuoc);

/// <summary>
/// Lưu file theo SHA-256 cho kho cá nhân, dữ liệu chung, DBC và báo cáo.
/// </summary>
public class KhoFile
{
    public KhoaKho Khoa { get; } = new();
    /// <summary>Trần cho MỘT file.</summary>
    public const long KichThuocToiDa = 256L * 1024 * 1024;

    private readonly string _thuMuc;

    protected KhoFile(IConfiguration cfg, IHostEnvironment env, string khoaCauHinh, string thuMucMacDinh)
    {
        var duong = cfg[khoaCauHinh];
        _thuMuc = string.IsNullOrWhiteSpace(duong)
            ? Path.Combine(env.ContentRootPath, "App_Data", thuMucMacDinh) : duong;
        Directory.CreateDirectory(_thuMuc);
    }

    public string DuongDan(string sha256) => Path.Combine(_thuMuc, sha256);

    public async Task<KetQuaLuuFile> LuuAsync(Stream nguon, CancellationToken ct)
    {
        // Ghi ra file tạm rồi mới đổi tên: chưa đọc hết thì chưa biết băm là gì, mà đặt tên theo băm là điều kiện để chống trùng.
        var tam = Path.Combine(_thuMuc, "tam-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            long kichThuoc;
            await using (var ra = File.Create(tam))
            {
                kichThuoc = 0;
                var buffer = new byte[81920];
                int n;
                while ((n = await nguon.ReadAsync(buffer, ct)) > 0)
                {
                    kichThuoc += n;
                    if (kichThuoc > KichThuocToiDa)
                        throw new TepKhongHopLe("File vượt giới hạn 256 MB.");
                    await ra.WriteAsync(buffer.AsMemory(0, n), ct);
                }
                if (kichThuoc == 0) throw new TepKhongHopLe("File rỗng.");
            }

            string sha;
            await using (var f = File.OpenRead(tam))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();

            var dich = DuongDan(sha);
            // Cùng nội dung thì file cũ đã đúng, không ghi đè.
            if (File.Exists(dich)) File.Delete(tam);
            else
            {
                try { File.Move(tam, dich); }
                catch (IOException) when (File.Exists(dich)) { File.Delete(tam); }
            }

            return new KetQuaLuuFile(sha, kichThuoc);
        }
        finally
        {
            // Hỏng giữa chừng thì dọn file dở, đừng để lại rác `.part` trong kho.
            if (File.Exists(tam)) File.Delete(tam);
        }
    }

    public void XoaFile(string sha256)
    {
        var d = DuongDan(sha256);
        if (File.Exists(d)) File.Delete(d);
    }

    /// <summary>Dọn file `.part` còn sót do tiến trình chết giữa lúc đang ghi.</summary>
    public int DonFileDo(TimeSpan cuHon)
    {
        var moc = DateTimeOffset.UtcNow - cuHon;
        var so = 0;
        foreach (var f in Directory.EnumerateFiles(_thuMuc, "tam-*.part"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(f) >= moc) continue;
                File.Delete(f);
                so++;
            }
            catch (IOException) { /* ai đó đang ghi, bỏ qua lượt này */ }
        }
        return so;
    }
}

/// <summary>
/// Chỗ cất file bằng chứng của các lượt chạy.
///
/// Khác <see cref="KhoGoiTestCase"/> ở một điểm cốt lõi: kho này **không kiểm
/// định dạng**. Gói test case thì phải là ZIP vì agent còn phải bung ra, còn
/// báo cáo thì máy bench gửi gì nhận nấy — log văn bản, trace CAN, `.blf` nhị
/// phân, ảnh chụp màn hình. Ép định dạng ở đây là tự chặn mình về sau.
/// </summary>
public class KhoBaoCao(IConfiguration cfg, IHostEnvironment env)
    : KhoFile(cfg, env, "BaoCao:ThuMuc", "bao-cao");

/// <summary>
/// Kho file riêng của từng người dùng. Không ai xem được kho người khác, kể cả
/// Admin — xem <see cref="Controllers.KhoController"/>.
///
/// Không kiểm định dạng và không ràng buộc tên file: chưa chốt sẽ chứa gì, ép
/// luật bây giờ là tự chặn mình khi biết rõ yêu cầu.
/// </summary>
public class KhoNguoiDung(IConfiguration cfg, IHostEnvironment env)
    : KhoFile(cfg, env, "KhoNguoiDung:ThuMuc", "nguoi-dung");

/// <summary>Dữ liệu dùng chung của cả hệ thống: file DBC, phiên bản phần mềm, tài liệu.</summary>
public class KhoDuLieuChung(IConfiguration cfg, IHostEnvironment env)
    : KhoFile(cfg, env, "DuLieuChung:ThuMuc", "du-lieu-chung");

public class KhoDatabase(IConfiguration cfg, IHostEnvironment env)
    : KhoFile(cfg, env, "DatabaseFiles:ThuMuc", "database");
