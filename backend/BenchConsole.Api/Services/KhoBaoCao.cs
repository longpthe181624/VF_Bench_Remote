using System.Security.Cryptography;

namespace BenchConsole.Api.Services;

public record KetQuaLuuBaoCao(string Sha256, long KichThuoc);

/// <summary>
/// Chỗ cất file bằng chứng của các lượt chạy trên máy A.
///
/// Khác <see cref="KhoGoiTestCase"/> ở một điểm cốt lõi: kho này **không kiểm
/// định dạng**. Gói test case thì phải là ZIP vì agent còn phải bung ra, còn
/// báo cáo thì máy bench gửi gì nhận nấy — log văn bản, trace CAN, `.blf` nhị
/// phân, ảnh chụp màn hình. Ép định dạng ở đây là tự chặn mình về sau.
/// </summary>
public class KhoBaoCao
{
    /// <summary>
    /// Trần cho MỘT file. Trace CAN một lượt 40 giây đã 2 MB, lượt 20 phút
    /// (828.726 frame) thì hàng chục MB. Để 256 MB là rộng rãi mà vẫn chặn
    /// được cú gửi nhầm cả ổ đĩa.
    /// </summary>
    public const long KichThuocToiDa = 256L * 1024 * 1024;

    private readonly string _thuMuc;

    public KhoBaoCao(IConfiguration cfg, IHostEnvironment env)
    {
        _thuMuc = cfg["BaoCao:ThuMuc"]
                  ?? Path.Combine(env.ContentRootPath, "App_Data", "bao-cao");
        Directory.CreateDirectory(_thuMuc);
    }

    public string DuongDan(string sha256) => Path.Combine(_thuMuc, sha256);

    public async Task<KetQuaLuuBaoCao> LuuAsync(Stream nguon, CancellationToken ct)
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
            // Máy bench gửi lại sau khi mạng đứt là chuyện bình thường. Cùng nội
            // dung thì file cũ đã đúng rồi, không ghi đè.
            if (File.Exists(dich)) File.Delete(tam);
            else File.Move(tam, dich);

            return new KetQuaLuuBaoCao(sha, kichThuoc);
        }
        finally
        {
            if (File.Exists(tam)) File.Delete(tam);
        }
    }
}
