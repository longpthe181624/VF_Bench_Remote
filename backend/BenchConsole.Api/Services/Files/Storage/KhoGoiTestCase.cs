using System.IO.Compression;
using System.Security.Cryptography;
using BenchConsole.Core.Messaging;

namespace BenchConsole.Api.Services.Files.Storage;

/// <summary>Gói tải lên không dùng được. Thông điệp đưa thẳng ra cho người tải.</summary>
public class GoiKhongHopLe(string message) : Exception(message);

public record KetQuaLuuGoi(string Sha256, long KichThuoc, int SoTestCase);

/// <summary>Chỗ cất gói test case trên máy A.</summary>
public class KhoGoiTestCase
{
    public KhoaKho Khoa { get; } = new();
    /// <summary>Trần kích thước.</summary>
    public const long KichThuocToiDa = 64L * 1024 * 1024;

    private readonly string _thuMuc;
    private readonly ILogger<KhoGoiTestCase> _log;

    public KhoGoiTestCase(IConfiguration cfg, IHostEnvironment env, ILogger<KhoGoiTestCase> log)
    {
        _log = log;
        // Phải kiểm chuỗi rỗng chứ không chỉ null: "ThuMuc": null trong appsettings.json được .NET Configuration đọc thành chuỗi rỗng, nên toán tử ??
        var thuMucCauHinh = cfg["GoiTestCase:ThuMuc"];
        _thuMuc = string.IsNullOrWhiteSpace(thuMucCauHinh)
                  ? Path.Combine(env.ContentRootPath, "App_Data", "goi-test-case")
                  : thuMucCauHinh;
        Directory.CreateDirectory(_thuMuc);
    }

    public string DuongDan(string sha256) => Path.Combine(_thuMuc, sha256 + ".zip");

    /// <summary>
    /// Nhận luồng tải lên, kiểm rồi cất. Ném <see cref="GoiKhongHopLe"/> kèm câu
    /// giải thích khi gói không dùng được.
    /// </summary>
    /// <param name="batBuocCoTestCase">
    /// Gói testcase rỗng bài là gói sai nên phải chặn. Gói config thì không
    /// chứa `.tc` nào cả — bắt buộc ở đó là từ chối oan.
    /// </param>
    public async Task<KetQuaLuuGoi> LuuAsync(
        Stream nguon, CancellationToken ct, bool batBuocCoTestCase = true, string kieuTest = "auto")
    {
        // Ghi ra file tạm trước: phải đọc lại toàn bộ để tính sha và đếm số bài, mà luồng HTTP thì không tua lại được.
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
                    if (kichThuoc > KichThuocToiDa) throw new GoiKhongHopLe("Gói vượt giới hạn 64 MB.");
                    await ra.WriteAsync(buffer.AsMemory(0, n), ct);
                }
            }

            if (kichThuoc == 0)
                throw new GoiKhongHopLe("File rỗng.");
            if (kichThuoc > KichThuocToiDa)
                throw new GoiKhongHopLe(
                    $"Gói {kichThuoc / 1024 / 1024} MB, vượt trần {KichThuocToiDa / 1024 / 1024} MB.");

            // Nhận dạng theo byte đầu, không tin đuôi file.
            var dau = new byte[NhanDangNen.SoByteCanDoc];
            await using (var doc = File.OpenRead(tam))
            {
                var da = await doc.ReadAsync(dau, ct);
                var lyDo = NhanDangNen.LyDoTuChoi(dau.AsSpan(0, da));
                if (lyDo is not null) throw new GoiKhongHopLe(lyDo);
            }

            // Vẫn đếm dù không bắt buộc: đây đồng thời là phép thử "ZIP này có mở được không".
            var soTc = DemTestCase(tam, kieuTest);
            if (batBuocCoTestCase && soTc == 0)
                throw new GoiKhongHopLe(
                    kieuTest == "manual" ? "Gói manual cần chứa file Excel .xlsx hoặc .xls."
                    : "Gói không chứa file .tc hoặc .mtc. Kiểm tra lại thư mục đã nén.");

            var sha = await TinhShaAsync(tam, ct);
            var dich = DuongDan(sha);

            if (File.Exists(dich))
            {
                // Cùng nội dung thì file cũ đã đúng rồi, không ghi đè.
                File.Delete(tam);
                _log.LogInformation("Gói {Sha} đã có sẵn, dùng lại file cũ", sha[..8]);
            }
            else
            {
                try { File.Move(tam, dich); }
                catch (IOException) when (File.Exists(dich)) { File.Delete(tam); }
            }

            return new KetQuaLuuGoi(sha, kichThuoc, soTc);
        }
        finally
        {
            // Hỏng ở bất kỳ bước nào cũng không để lại rác .part trong kho.
            if (File.Exists(tam)) File.Delete(tam);
        }
    }

    /// <summary>Đếm số bài trong gói, đồng thời là phép thử "ZIP này có mở được không".</summary>
    private static int DemTestCase(string duongDan, string kieuTest)
    {
        try
        {
            using var z = ZipFile.OpenRead(duongDan);
            if (z.Entries.Count > 10000) throw new GoiKhongHopLe("Gói có quá 10.000 mục.");
            long tong = 0;
            foreach (var entry in z.Entries)
            {
                var name = entry.FullName.Replace('\\', '/');
                if (name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p => p is ".." or "."))
                    throw new GoiKhongHopLe("ZIP chứa đường dẫn không hợp lệ.");
                if (entry.Length > KhoFile.KichThuocToiDa - tong)
                    throw new GoiKhongHopLe("Nội dung ZIP sau giải nén vượt 256 MB.");
                tong += entry.Length;
            }
            return z.Entries.Count(e =>
                e.Length > 0 && (kieuTest == "manual"
                    ? e.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)
                    : e.Name.EndsWith(".tc", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".mtc", StringComparison.OrdinalIgnoreCase)));
        }
        catch (InvalidDataException ex)
        {
            throw new GoiKhongHopLe("Gói ZIP hỏng, không mở được: " + ex.Message);
        }
    }

    private static async Task<string> TinhShaAsync(string duongDan, CancellationToken ct)
    {
        await using var f = File.OpenRead(duongDan);
        var hash = await SHA256.HashDataAsync(f, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Xoá file của một gói.</summary>
    public void XoaFile(string sha256)
    {
        var d = DuongDan(sha256);
        if (File.Exists(d)) File.Delete(d);
    }
}
