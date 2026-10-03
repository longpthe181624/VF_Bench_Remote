using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace BenchConsole.Api.Tests;

public static class DatabaseChecks
{
    public static async Task Run(HttpClient http, string admin, string viewer, string engineer, Action<bool,string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? token = null, object? body = null)
        {
            using var req = new HttpRequestMessage(method, path);
            if (token is not null) req.Headers.Authorization = new("Bearer", token);
            if (body is not null) req.Content = JsonContent.Create(body);
            return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        async Task<HttpResponseMessage> Upload(string token, int model, int category, int type, string version, string text)
        {
            using var form = new MultipartFormDataContent();
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", "same.dbc");
            form.Add(new StringContent(model.ToString()), "modelId");form.Add(new StringContent(category.ToString()), "categoryId");form.Add(new StringContent(type.ToString()), "typeId");form.Add(new StringContent(version), "phienBan");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/database/files") { Content = form };
            req.Headers.Authorization = new("Bearer", token); return await http.SendAsync(req);
        }
        var lookup = await Json(await Send(HttpMethod.Get, "/api/database/lookups", viewer));
        var model = lookup.GetProperty("models")[0].GetProperty("id").GetInt32();
        var category = lookup.GetProperty("categories")[0].GetProperty("id").GetInt32();
        var type = lookup.GetProperty("types")[0].GetProperty("id").GetInt32();
        check((await Send(HttpMethod.Get, "/api/database/files")).StatusCode == HttpStatusCode.Unauthorized, "Database: FE API yêu cầu đăng nhập");
        check((await Send(HttpMethod.Post, "/api/database/lookups/models", engineer, new { ma="TEST",ten="Test" })).StatusCode == HttpStatusCode.Forbidden, "Database: chỉ Admin cấu hình danh mục");
        var created = await Send(HttpMethod.Post, "/api/database/lookups/categories", admin, new { ma="TEST-CAT",ten="Test category" });
        check(created.IsSuccessStatusCode, "Database: Admin thêm danh mục");
        var extra = (await Json(created)).GetProperty("id").GetInt32();
        check((await Send(HttpMethod.Get, "/api/database/lookups", viewer)).IsSuccessStatusCode, "Database: Viewer xem được danh mục");
        check((await Send(HttpMethod.Post, "/api/database/lookups/categories", admin, new { ma="test-cat",ten="Other" })).StatusCode == HttpStatusCode.Conflict, "Database: chặn trùng mã danh mục khác hoa/thường");
        check((await Send(HttpMethod.Patch, $"/api/database/lookups/categories/{extra}", admin, new { ma="TEST-CAT",ten="Renamed category" })).IsSuccessStatusCode, "Database: đổi tên giữ ID");
        check((await Send(HttpMethod.Patch, $"/api/database/lookups/categories/{extra}", admin, new { ma="DIFFERENT",ten="Changed code" })).StatusCode == HttpStatusCode.BadRequest, "Database: mã danh mục cố định");
        check((await Upload(viewer,model,category,type,"1","old bytes")).StatusCode == HttpStatusCode.Forbidden,"Database: Viewer không upload");
        check((await Upload(engineer,999999,category,type,"1","old bytes")).StatusCode == HttpStatusCode.BadRequest,"Database: chặn danh mục không tồn tại");
        var olderUpload = await Upload(engineer,model,category,type,"1","old bytes");
        check(olderUpload.IsSuccessStatusCode,"Database: Engineer upload bản Draft");
        var older = await Json(olderUpload);var id=older.GetProperty("id").GetInt32();var sha=older.GetProperty("sha256").GetString()!;
        check(older.GetProperty("status").GetString()=="Draft"&&older.GetProperty("revision").GetInt64()==1,"Database: mặc định Draft, revision 1");
        check(sha==Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("old bytes"))).ToLowerInvariant(),"Database: SHA-256 đúng nội dung");
        check((await Upload(engineer,model,category,type,"1","overwrite bytes")).StatusCode==HttpStatusCode.Conflict,"Database: không ghi đè cùng file/phiên bản");
        var newer = await Json(await Upload(engineer,model,category,type,"2","new bytes"));var newerId=newer.GetProperty("id").GetInt32();
        check(newerId!=id,"Database: phiên bản mới giữ bản cũ");
        check((await Send(HttpMethod.Get,$"/api/client/database/files/{id}/download?sha256={sha}")).StatusCode==HttpStatusCode.Conflict,"Client: Draft không tải mặc định");
        var draftDownload=await Send(HttpMethod.Get,$"/api/client/database/files/{id}/download?sha256={sha}&testing=true");
        check(draftDownload.IsSuccessStatusCode&&(await draftDownload.Content.ReadAsStringAsync())=="old bytes","Client: Draft được tải khi chọn testing=true");
        check((await Send(HttpMethod.Get,$"/api/client/database/files/{id}/download?sha256={new string('0',64)}&testing=true")).StatusCode==HttpStatusCode.Conflict,"Client: chặn SHA không khớp");
        check((await Send(HttpMethod.Get,$"/api/client/database/files/{id}/download?testing=true")).StatusCode==HttpStatusCode.Conflict,"Client: bắt buộc SHA-256");
        check((await Send(HttpMethod.Patch,$"/api/database/files/{id}/status",engineer,new { status="Release",revision=1 })).StatusCode==HttpStatusCode.Forbidden,"Database: Engineer không tự Release");
        check((await Send(HttpMethod.Patch,$"/api/database/files/{id}/status",admin,new { status="Published",revision=1 })).StatusCode==HttpStatusCode.BadRequest,"Database: chỉ hai trạng thái");
        var changes=await Task.WhenAll(Send(HttpMethod.Patch,$"/api/database/files/{id}/status",admin,new {status="Release",revision=1}),Send(HttpMethod.Patch,$"/api/database/files/{id}/status",admin,new {status="Release",revision=1}));
        check(changes.Count(x=>x.IsSuccessStatusCode)==1&&changes.Count(x=>x.StatusCode==HttpStatusCode.Conflict)==1,"Database: hai người đổi cùng revision chỉ một thành công");
        check((await Send(HttpMethod.Patch,$"/api/database/files/{newerId}/status",admin,new {status="Release",revision=1})).IsSuccessStatusCode,"Database: hai bản Release cùng tồn tại");
        var manifest=await Json(await Send(HttpMethod.Get,"/api/client/database/manifest?status=Release"));
        check(manifest.GetProperty("items").GetArrayLength()==2,"Client: trả cả hai bản Release, không chọn latest");
        var oldDownload=await Send(HttpMethod.Get,$"/api/client/database/files/{id}/download?sha256={sha}");
        check(oldDownload.IsSuccessStatusCode&&(await oldDownload.Content.ReadAsStringAsync())=="old bytes","Client: chọn ID cũ vẫn tải đúng bản cũ dù có bản mới");
        check((await Send(HttpMethod.Delete,$"/api/database/files/{id}?revision=2",admin)).StatusCode==HttpStatusCode.Conflict,"Database: phải chuyển Draft trước khi xoá Release");
        check((await Send(HttpMethod.Patch,$"/api/database/files/{id}",engineer,new {modelId=model,categoryId=extra,typeId=type,revision=2})).StatusCode==HttpStatusCode.Conflict,"Database: Release không sửa phân loại");
        check((await Send(HttpMethod.Delete,$"/api/database/lookups/categories/{category}",admin)).StatusCode==HttpStatusCode.Conflict,"Database: chặn xoá danh mục còn file");
        check((await Send(HttpMethod.Patch,$"/api/database/files/{id}/status",admin,new {status="Draft",revision=2})).IsSuccessStatusCode,"Database: Release về Draft");
        check((await Send(HttpMethod.Get,$"/api/client/database/files/{id}/download?sha256={sha}")).StatusCode==HttpStatusCode.Conflict,"Client: Release bị thu hồi không tải thường được nữa");
        var moved=await Send(HttpMethod.Patch,$"/api/database/files/{id}",engineer,new {modelId=model,categoryId=extra,typeId=type,moTa="Moved",revision=3});
        check(moved.IsSuccessStatusCode&&(await Json(moved)).GetProperty("category").GetString()=="Renamed category","Database: sửa phân loại Draft và trả tên mới");
        var history=await Json(await Send(HttpMethod.Get,$"/api/database/files/{id}/history",viewer));
        check(history.GetArrayLength()==4&&history.EnumerateArray().Any(x=>x.GetProperty("fromStatus").GetString()=="Release"&&x.GetProperty("toStatus").GetString()=="Draft"),"Database: lịch sử upload/trạng thái/phân loại");
        check((await Send(HttpMethod.Delete,$"/api/database/files/{id}?revision=3",admin)).StatusCode==HttpStatusCode.Conflict,"Database: chặn xoá revision cũ");
        check((await Send(HttpMethod.Delete,$"/api/database/files/{id}?revision=4",admin)).IsSuccessStatusCode,"Database: xoá Draft");
        check((await Send(HttpMethod.Get,$"/api/client/database/files/{id}")).StatusCode==HttpStatusCode.NotFound,"Client: file đã xoá không còn trong manifest/detail");
        check((await Send(HttpMethod.Get,$"/api/database/files/{id}/history",viewer)).IsSuccessStatusCode,"Database: giữ lịch sử sau xoá");
        check((await Send(HttpMethod.Delete,$"/api/database/lookups/categories/{extra}",admin)).IsSuccessStatusCode,"Database: xoá danh mục không còn sử dụng");
        using var sharedForm = new MultipartFormDataContent();
        sharedForm.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("legacy bytes")), "file", "legacy.dbc");
        sharedForm.Add(new StringContent("dbc"), "loai");
        using var sharedReq = new HttpRequestMessage(HttpMethod.Post, "/api/du-lieu-chung") { Content = sharedForm };
        sharedReq.Headers.Authorization = new("Bearer",admin);
        var legacy = await Json(await http.SendAsync(sharedReq));var legacyId = legacy.GetProperty("id").GetInt32();
        var imported = await Send(HttpMethod.Post,"/api/database/import-shared",engineer,new { fileId=legacyId,modelId=model,categoryId=category,typeId=type,phienBan="legacy-1" });
        check(imported.IsSuccessStatusCode,"Database: nhập file cũ với phân loại rõ ràng");
        var importedFile = await Json(imported);
        check(importedFile.GetProperty("status").GetString()=="Draft"&&importedFile.GetProperty("sha256").GetString()==legacy.GetProperty("sha256").GetString(),"Database: nhập file cũ giữ đúng SHA và trạng thái Draft");
        check((await Send(HttpMethod.Get,$"/api/du-lieu-chung/{legacyId}/download",viewer)).IsSuccessStatusCode,"Database: nhập file không mất nguồn dữ liệu chung");
        var safeManifest = await Json(await Send(HttpMethod.Get,"/api/client/database/manifest"));
        check(!safeManifest.GetProperty("items")[0].TryGetProperty("nguoiTaiLen",out _),"Client: manifest không lộ email người upload");
    }
}
