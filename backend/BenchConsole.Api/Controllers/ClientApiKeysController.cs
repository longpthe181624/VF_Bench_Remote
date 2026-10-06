using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BenchConsole.Api.Auth;
using BenchConsole.Api.Data;
using BenchConsole.Core.Auth;
using BenchConsole.Core.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.WebUtilities;

namespace BenchConsole.Api.Controllers;

public sealed class CreateClientKey
{
    [Required, StringLength(128)] public string Name { get; set; } = "";
    [Required, MinLength(1), MaxLength(4)] public string[] Permissions { get; set; } = [];
    [MaxLength(200)] public string[] Devices { get; set; } = [];
    public DateTimeOffset? ExpiresAt { get; set; }
}
public sealed record ChangeClientKeyPermissions([Required, MinLength(1), MaxLength(4)] string[] Permissions, long Revision, [MaxLength(200)] string[]? Devices = null);
public sealed record RevokeClientKey(long Revision);

// Key máy không được quản trị key khác. Chỉ JWT của người có vai trò Admin.
[ApiController, Route("api/client-api-keys"), Authorize(Roles = "Admin")]
public class ClientApiKeysController(AppDbContext db) : ControllerBase
{
    private static object Dto(ClientApiKey row) => new
    {
        row.Id, row.KeyId, row.Name, devices = JsonSerializer.Deserialize<string[]>(row.DevicesJson), permissions = ClientKeyAccess.ReadPermissions(row.PermissionsJson),
        row.CreatedBy, row.CreatedAt, row.ExpiresAt, row.RevokedAt, row.Revision,
        state = row.RevokedAt.HasValue ? "revoked" : row.ExpiresAt <= DateTimeOffset.UtcNow ? "expired" : "active",
    };
    private static string[]? Normalize(string[]? permissions)
    {
        if (permissions is null || permissions.Length == 0 || permissions.Any(p => !ClientKeyAccess.Permissions.Contains(p))) return null;
        return permissions.Distinct().Order().ToArray();
    }

    private async Task<string[]?> Devices(string[]? input, string[] permissions, CancellationToken ct)
    {
        var codes = (input ?? []).Select(x => x?.Trim() ?? "").Distinct().Order().ToArray();
        if (codes.Any(x => x.Length is 0 or > 64)) return null;
        if (permissions.Contains(MaQuyen.ClientJobsExecute) && codes.Length == 0) return null;
        var existing = await db.Benches.Where(x => codes.Contains(x.Code)).Select(x => x.Code).ToListAsync(ct);
        return codes.All(existing.Contains) ? codes : null;
    }

    [HttpGet("devices")]
    public async Task<object> Devices(CancellationToken ct) => await db.Benches.AsNoTracking()
        .OrderBy(x => x.Code).Select(x => new { x.Code, x.Ten }).ToListAsync(ct);

    [HttpGet("permissions")]
    public object Permissions() => MaQuyen.TatCa.Where(p => ClientKeyAccess.Permissions.Contains(p.Ma));

    [HttpGet]
    public async Task<object> List(CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        return (await db.ClientApiKeys.AsNoTracking().OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(Dto);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateClientKey input, CancellationToken ct)
    {
        var permissions = Normalize(input.Permissions);
        if (permissions is null) return BadRequest(new { error = "Chọn quyền xem, upload file hoặc thêm danh mục." });
        if (input.ExpiresAt <= DateTimeOffset.UtcNow) return BadRequest(new { error = "Ngày hết hạn phải ở tương lai." });
        var devices = await Devices(input.Devices, permissions, ct);
        if (devices is null) return BadRequest(new { error = "Chọn thiết bị hợp lệ; quyền nhận việc bắt buộc gán ít nhất một thiết bị." });
        var keyId = Guid.NewGuid().ToString("N");
        var raw = "bck_" + keyId + "." + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var row = new ClientApiKey { KeyId = keyId, Name = input.Name.Trim(), CreatedBy = User.Email()!,
            SecretHash = SHA256.HashData(Encoding.UTF8.GetBytes(raw)), PermissionsJson = JsonSerializer.Serialize(permissions),
            DevicesJson = JsonSerializer.Serialize(devices), CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = input.ExpiresAt };
        db.ClientApiKeys.Add(row);
        await db.SaveChangesAsync(ct);
        Response.Headers.CacheControl = "no-store";
        return StatusCode(201, new { key = Dto(row), apiKey = raw });
    }

    [HttpPatch("{id:int}/permissions")]
    public async Task<IActionResult> Permissions(int id, ChangeClientKeyPermissions input, CancellationToken ct)
    {
        var row = await db.ClientApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (row.RevokedAt.HasValue) return Conflict(new { error = "Key đã thu hồi; tạo key mới nếu cần." });
        if (input.Revision != row.Revision) return Conflict(new { error = "Key đã thay đổi. Tải lại." });
        var permissions = Normalize(input.Permissions);
        if (permissions is null) return BadRequest(new { error = "Quyền không hợp lệ cho client." });
        var devices = await Devices(input.Devices ?? JsonSerializer.Deserialize<string[]>(row.DevicesJson), permissions, ct);
        if (devices is null) return BadRequest(new { error = "Quyền nhận việc cần danh sách thiết bị hợp lệ." });
        if (await db.TestJobs.AnyAsync(x => x.Owner == row.KeyId && x.ActiveDevice != null && !devices.Contains(x.Device), ct))
            return Conflict(new { error = "Tool còn giữ thiết bị; kết thúc hoặc xác nhận dừng việc trước khi gỡ thiết bị." });
        row.DevicesJson = JsonSerializer.Serialize(devices);
        row.PermissionsJson = JsonSerializer.Serialize(permissions); row.Revision++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Key đã thay đổi. Tải lại." }); }
        Response.Headers.CacheControl = "no-store";
        return Ok(Dto(row));
    }

    [HttpPost("{id:int}/revoke")]
    public async Task<IActionResult> Revoke(int id, RevokeClientKey input, CancellationToken ct)
    {
        var row = await db.ClientApiKeys.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (row.RevokedAt.HasValue) return Ok(Dto(row));
        if (input.Revision != row.Revision) return Conflict(new { error = "Key đã thay đổi. Tải lại." });
        row.RevokedAt = DateTimeOffset.UtcNow; row.Revision++;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "Key đã thay đổi. Tải lại." }); }
        Response.Headers.CacheControl = "no-store";
        return Ok(Dto(row));
    }
}
