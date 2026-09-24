using System.IO.Compression;
using System.Security.Cryptography;
using BenchConsole.Core.Messaging;

namespace BenchConsole.Api.Services;

/// <summary>Gói tải lên không dùng được. Thông điệp đưa thẳng ra cho người tải.</summary>
public class GoiKhongHopLe(string message) : Exception(message);

public record KetQuaLuuGoi(string Sha256, long KichThuoc, int SoTestCase);

/// <summary>
/// Chỗ cất gói test case trên máy A.
///
/// File nằm trên đĩa chứ không trong DB, tên file là chính sha256 của nội dung.
/// Hai điều lợi: tải lên cùng một gói hai lần không tốn thêm chỗ, và agent ở xa
/// có đúng một con số để kiểm gói tải về có nguyên vẹn không.
/// </summary>
public class KhoGoiTestCase
{
    /// <summary>
    /// Trần kích thước. Đo thật trên máy bench 24/09: cả thư mục AutoTests của
    /// Qauto là 31 MB cho 3138 bài, file .tc lớn nhất 21 KB. Một gói người ta
    /// đẩy xuống thường là một thư mục con, cỡ vài MB. Để 64 MB là rộng gấp
    /// đôi cả kho, đủ cho mọi trường hợp hợp lý mà vẫn chặn được cú tải nhầm
    /// một file cài đặt.
    /// </summary>
    public const long KichThuocToiDa = 64L * 1024 * 1024;

    private readonly string _thuMuc;
    private readonly ILogger<KhoGoiTestCase> _log;

    public KhoGoiTestCase(IConfiguration cfg, IHostEnvironment env, ILogger<KhoGoiTestCase> log)
    {
        _log = log;
        _thuMuc = cfg["GoiTestCase:ThuMuc"]
                  ?? Path.Combine(env.ContentRootPath, "App_Data", "goi-test-case");
        Directory.CreateDirectory(_thuMuc);
    }

    public string DuongDan(string sha256) => Path.Combine(_thuMuc, sha256 + ".zip");

    /// <summary>
    /// Nhận luồng tải lên, kiểm rồi cất. Ném <see cref="GoiKhongHopLe"/> kèm câu
    /// giải thích khi gói không dùng được.
    /// </summary>
    public async Task<KetQuaLuuGoi> LuuAsync(Stream nguon, CancellationToken ct)
    {
        // Ghi ra file tạm trước: phải đọc lại toàn bộ để tính sha và đếm số bài,
        // mà luồng HTTP thì không tua lại được.
        var tam = Path.Combine(_thuMuc, "tam-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            long kichThuoc;
            await using (var ra = File.Create(tam))
            {
                await nguon.CopyToAsync(ra, ct);
                kichThuoc = ra.Length;
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

            var soTc = DemTestCase(tam);
            if (soTc == 0)
                throw new GoiKhongHopLe(
                    "Gói không chứa file .tc hay .mtc nào. Kiểm tra lại thư mục đã nén.");

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
                File.Move(tam, dich);
            }

            return new KetQuaLuuGoi(sha, kichThuoc, soTc);
        }
        finally
        {
            // Hỏng ở bất kỳ bước nào cũng không để lại rác .part trong kho.
            if (File.Exists(tam)) File.Delete(tam);
        }
    }

    /// <summary>
    /// Đếm số bài trong gói, đồng thời là phép thử "ZIP này có mở được không".
    /// Chữ ký PK đúng mà cấu trúc hỏng thì vẫn phải chặn ở máy A.
    /// </summary>
    private static int DemTestCase(string duongDan)
    {
        try
        {
            using var z = ZipFile.OpenRead(duongDan);
            return z.Entries.Count(e =>
                e.Name.EndsWith(".tc", StringComparison.OrdinalIgnoreCase) ||
                e.Name.EndsWith(".mtc", StringComparison.OrdinalIgnoreCase));
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

    /// <summary>
    /// Xoá file của một gói. Chỉ gọi khi không còn bản ghi nào trỏ vào sha này —
    /// hai gói khác tên mà cùng nội dung sẽ dùng chung một file.
    /// </summary>
    public void XoaFile(string sha256)
    {
        var d = DuongDan(sha256);
        if (File.Exists(d)) File.Delete(d);
    }
}
