using System.Security.Cryptography;
using System.Text.Json;
using System.Text;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Contracts;
using BenchConsole.Api.Data;
using BenchConsole.Api.Services.Common;
using BenchConsole.Api.Services.Identity;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace BenchConsole.Api.Services.Client;

public sealed class ClientApiKeysService(AppDbContext db, ICurrentCaller caller)
{
    private static object Dto(ClientApiKey row)
    => new
    {
        row.Id,
        row.KeyId,
        row.Name,
        devices = JsonSerializer.Deserialize<string[]>(row.DevicesJson),
        permissions = ClientKeyAccess.ReadPermissions(row.PermissionsJson),
        row.CreatedBy,
        row.CreatedAt,
        row.ExpiresAt,
        row.RevokedAt,
        row.Revision,
        state = row.RevokedAt.HasValue ? "revoked" : row.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "active",
    };

    private static string[]? Normalize(string[]? permissions)
    {
        if (permissions is null || permissions.Length == 0 || permissions.Any(p => !ClientKeyAccess.Permissions.Contains(p)))
            return null;
        return permissions.Distinct().Order().ToArray();
    }

    private async Task<string[]?> Devices(string[]? input, string[] permissions, CancellationToken ct)
    {
        var codes = (input ?? []).Select(x => x?.Trim() ?? "").Distinct().Order().ToArray();
        if (codes.Any(x => x.Length is 0 or > 64))
            return null;
        if (permissions.Contains(MaQuyen.ClientJobsExecute) && codes.Length == 0)
            return null;
        var existing = await db.Benches.Where(x => codes.Contains(x.Code)).Select(x => x.Code).ToListAsync(ct);
        return codes.All(existing.Contains) ? codes : null;
    }

    public async Task<object> Devices(CancellationToken ct)
    => await db.Benches.AsNoTracking()
        .OrderBy(x => x.Code).Select(x => new { x.Code, x.Ten }).ToListAsync(ct);

    public object Permissions()
    => MaQuyen.TatCa.Where(p => ClientKeyAccess.Permissions.Contains(p.Ma));

    public async Task<object> List(CancellationToken ct)
    {
        return (await db.ClientApiKeys.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(Dto);
    }

    public async Task<object> Create(CreateClientKey input, CancellationToken ct)
    {
        var permissions = Normalize(input.Permissions);
        if (permissions is null)
            throw ApiException.BadRequest("Chọn quyền xem, upload file hoặc thêm danh mục.");
        if (input.ExpiresAt <= DateTimeOffset.UtcNow)
            throw ApiException.BadRequest("Ngày hết hạn phải ở tương lai.");
        var devices = await Devices(input.Devices, permissions, ct);
        if (devices is null)
            throw ApiException.BadRequest("Chọn thiết bị hợp lệ; quyền nhận việc bắt buộc gán ít nhất một thiết bị.");
        var keyId = Guid.NewGuid().ToString("N");
        var raw = "bck_" + keyId + "." + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new ClientApiKey
        {
            KeyId = keyId,
            Name = input.Name.Trim(),
            CreatedBy = caller.Email!,
            SecretHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw)),
            PermissionsJson = JsonSerializer.Serialize(permissions),
            DevicesJson = JsonSerializer.Serialize(devices),
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = input.ExpiresAt
        };
        db.ClientApiKeys.Add(row);
        await db.SaveChangesAsync(ct);
        return new { key = Dto(row), apiKey = raw };
    }

    public async Task<object> Permissions(int id, ChangeClientKeyPermissions input, CancellationToken ct)
    {
        var row = await db.ClientApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
            throw ApiException.NotFound();
        if (row.RevokedAt.HasValue)
            throw ApiException.Conflict("Key đã thu hồi; tạo key mới nếu cần.");
        if (input.Revision != row.Revision)
            throw ApiException.Conflict("Key đã thay đổi. Tải lại.");
        var permissions = Normalize(input.Permissions);
        if (permissions is null)
            throw ApiException.BadRequest("Quyền không hợp lệ cho client.");
        var devices = await Devices(input.Devices ?? JsonSerializer.Deserialize<string[]>(row.DevicesJson), permissions, ct);
        if (devices is null)
            throw ApiException.BadRequest("Quyền nhận việc cần danh sách thiết bị hợp lệ.");
        if (await db.TestJobs.AnyAsync(x => x.Owner == row.KeyId && x.ActiveDevice != null && !devices.Contains(x.Device), ct))
            throw ApiException.Conflict("Tool còn giữ thiết bị; kết thúc hoặc xác nhận dừng việc trước khi gỡ thiết bị.");
        row.DevicesJson = JsonSerializer.Serialize(devices);
        row.PermissionsJson = JsonSerializer.Serialize(permissions);
        row.Revision++;
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Key đã thay đổi. Tải lại."); }
        return Dto(row);
    }

    public async Task<object> Revoke(int id, RevokeClientKey input, CancellationToken ct)
    {
        var row = await db.ClientApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null)
            throw ApiException.NotFound();
        if (row.RevokedAt.HasValue)
            return Dto(row);
        if (input.Revision != row.Revision)
            throw ApiException.Conflict("Key đã thay đổi. Tải lại.");
        row.RevokedAt = DateTimeOffset.UtcNow;
        row.Revision++;
        try
        { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw ApiException.Conflict("Key đã thay đổi. Tải lại."); }
        return Dto(row);
    }
}
