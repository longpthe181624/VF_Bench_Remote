namespace BenchConsole.Core.Messaging;

/// <summary>Định dạng nén nhận ra được từ mấy byte đầu file.</summary>
public enum DangNen
{
    KhongRo = 0,
    Zip = 1,
    SevenZip = 2,
}

/// <summary>Nhận dạng gói test case bằng **byte đầu file**, không tin phần mở rộng.</summary>
public static class NhanDangNen
{
    private static readonly byte[] ChuKyZip = [0x50, 0x4B, 0x03, 0x04];        // PK\x03\x04
    private static readonly byte[] ChuKy7z = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];

    /// <summary>Số byte đầu cần đọc để kết luận. Nơi gọi chỉ cần đọc chừng này.</summary>
    public const int SoByteCanDoc = 6;

    public static DangNen Doan(ReadOnlySpan<byte> dauFile)
    {
        if (BatDau(dauFile, ChuKy7z)) return DangNen.SevenZip;
        if (BatDau(dauFile, ChuKyZip)) return DangNen.Zip;
        return DangNen.KhongRo;
    }

    /// <summary>Câu giải thích cho người dùng khi gói không nhận được, hoặc null khi gói hợp lệ.</summary>
    public static string? LyDoTuChoi(ReadOnlySpan<byte> dauFile)
        => Doan(dauFile) switch
        {
            DangNen.Zip => null,
            // Agent chỉ bung được ZIP: Python có sẵn `zipfile`, còn 7z phải cài thêm.
            DangNen.SevenZip =>
                "Gói đang là 7z. Agent trên máy bench chỉ bung được ZIP. "
                + "Hãy nén lại bằng ZIP rồi tải lên.",
            _ => "Gói không phải file nén ZIP. Kiểm tra lại file đã chọn.",
        };

    private static bool BatDau(ReadOnlySpan<byte> data, ReadOnlySpan<byte> chuKy)
        => data.Length >= chuKy.Length && data[..chuKy.Length].SequenceEqual(chuKy);
}
