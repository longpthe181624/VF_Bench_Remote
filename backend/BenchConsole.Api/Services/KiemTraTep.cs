using Microsoft.AspNetCore.Http;

namespace BenchConsole.Api.Services;

public static class KiemTraTep
{
    // Chừa phần multipart ngoài nội dung file.
    public const long TranYeuCau = KhoFile.KichThuocToiDa + 1024 * 1024;
    public const long TranGoi = KhoGoiTestCase.KichThuocToiDa + 1024 * 1024;

    public static string TenGoc(string ten) => ten.Replace('\\', '/').Split('/').Last();

    public static string? LoiTen(string? ten)
        => string.IsNullOrWhiteSpace(ten) || ten.Length > 260 || ten is "." or ".."
           || ten.Any(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c))
            ? "Tên file không hợp lệ hoặc dài quá 260 ký tự." : null;

    public static string? Loi(IEnumerable<IFormFile>? files, long tran = KhoFile.KichThuocToiDa)
    {
        var ds = files?.ToList();
        if (ds is null || ds.Count == 0) return "Chưa chọn file.";
        if (ds.Count > 100) return "Mỗi lần chỉ tải tối đa 100 file.";
        long tong = 0;
        foreach (var f in ds)
        {
            if (LoiTen(TenGoc(f.FileName)) is { } loi) return loi;
            if (f.Length <= 0) return $"File '{TenGoc(f.FileName)}' rỗng.";
            if (f.Length > tran || f.Length > tran - tong)
                return $"Tổng dung lượng mỗi lần tải không được vượt {tran / 1024 / 1024} MB.";
            tong += f.Length;
        }
        return null;
    }
}

public class TepKhongHopLe(string message) : Exception(message);
