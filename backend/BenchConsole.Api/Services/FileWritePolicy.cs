namespace BenchConsole.Api.Services;

/// <summary>Quy tắc chung cho DBC và phần mềm, kể cả endpoint tương thích cũ.</summary>
public static class FileWritePolicy
{
    public static bool IsValidStatus(string status) => status is "Draft" or "Release";

    public static string? RevisionError(long current, long? expected) =>
        expected.HasValue && current != expected.Value
            ? "Bản ghi đã thay đổi. Tải lại trước khi tiếp tục."
            : null;

    public static string? DraftError(string status, long current, long? expected)
    {
        if (status != "Draft")
            return "Chuyển về Draft trước khi chỉnh sửa hoặc xoá file Release.";
        return RevisionError(current, expected);
    }
}
