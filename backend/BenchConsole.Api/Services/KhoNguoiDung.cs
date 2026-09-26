using System.Security.Cryptography;

namespace BenchConsole.Api.Services;

public record KetQuaLuuTep(string Sha256, long KichThuoc);

/// <summary>
/// Kho file riêng của từng người dùng.
///
/// **Chưa biết sẽ chứa gì** — mục đích lúc này là dựng sẵn chỗ chứa và đường
/// vận chuyển, nội dung chốt sau. Nên kho này KHÔNG kiểm định dạng và không
/// ràng buộc tên file: ép luật bây giờ là tự chặn mình khi biết rõ yêu cầu.
///
/// Viết riêng thay vì gom chung với <see cref="KhoBaoCao"/> dù hai lớp gần
/// giống nhau: kho báo cáo đang chạy thật trên máy A, mà tầng Api chưa có phép
/// kiểm tự động nào che, nên gom lại là đánh cược vào thứ đang hoạt động.
/// Khi có kiểm thử cho tầng này thì hợp nhất.
/// </summary>
public class KhoNguoiDung
{
    /// <summary>Trần cho một file. Bằng kho báo cáo, không có lý do lệch nhau.</summary>
    public const long KichThuocToiDa = 256L * 1024 * 1024;

    private readonly string _thuMuc;

    public KhoNguoiDung(IConfiguration cfg, IHostEnvironment env)
    {
        _thuMuc = cfg["KhoNguoiDung:ThuMuc"]
                  ?? Path.Combine(env.ContentRootPath, "App_Data", "nguoi-dung");
        Directory.CreateDirectory(_thuMuc);
    }

    public string DuongDan(string sha256) => Path.Combine(_thuMuc, sha256);

    public async Task<KetQuaLuuTep> LuuAsync(Stream nguon, CancellationToken ct)
    {
        var tam = Path.Combine(_thuMuc, "tam-" + Guid.NewGuid().ToString("N") + ".part");
        try
        {
            long kichThuoc;
            await using (var ra = File.Create(tam))
            {
                await nguon.CopyToAsync(ra, ct);
                kichThuoc = ra.Length;
            }

            string sha;
            await using (var f = File.OpenRead(tam))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();

            var dich = DuongDan(sha);
            // Cùng nội dung thì file cũ đã đúng, không ghi đè. Hai người tải lên
            // cùng một file cũng chỉ tốn một chỗ.
            if (File.Exists(dich)) File.Delete(tam);
            else File.Move(tam, dich);

            return new KetQuaLuuTep(sha, kichThuoc);
        }
        finally
        {
            if (File.Exists(tam)) File.Delete(tam);
        }
    }

    public void XoaFile(string sha256)
    {
        var d = DuongDan(sha256);
        if (File.Exists(d)) File.Delete(d);
    }
}
