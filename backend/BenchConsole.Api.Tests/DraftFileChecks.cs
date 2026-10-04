using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BenchConsole.Api.Tests;

public static class DraftFileChecks
{
    public static async Task Run(HttpClient http, string admin, string viewer, string engineer, Action<bool,string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, string token, object? body = null)
        {
            using var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new("Bearer", token);
            if (body is not null) req.Content = JsonContent.Create(body);
            return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage response) => JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        async Task<HttpResponseMessage> Multipart(string path, string token, Dictionary<string,string> fields, string? content)
        {
            using var form = new MultipartFormDataContent();
            foreach (var pair in fields) form.Add(new StringContent(pair.Value), pair.Key);
            if (content is not null) form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(content)), "file", "draft-edit.bin");
            using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = form };
            req.Headers.Authorization = new("Bearer", token);
            return await http.SendAsync(req);
        }
        var lookup = await Json(await Send(HttpMethod.Get, "/api/database/lookups", admin));
        var fields = new Dictionary<string,string> { ["modelId"] = lookup.GetProperty("models")[0].GetProperty("id").ToString(), ["categoryId"] = lookup.GetProperty("categories")[0].GetProperty("id").ToString(), ["typeId"] = lookup.GetProperty("types")[0].GetProperty("id").ToString(), ["phienBan"] = "draft-edit" };
        var created = await Json(await Multipart("/api/database/files", engineer, fields, "original database"));
        var id = created.GetProperty("id").GetInt32();
        var originalSha = created.GetProperty("sha256").GetString();
        fields["revision"] = "1";
        check((await Multipart($"/api/database/files/{id}/update", viewer, fields, "denied")).StatusCode == HttpStatusCode.Forbidden, "Draft DBC: Viewer không thay file");
        var replaced = await Multipart($"/api/database/files/{id}/update", engineer, fields, "updated database");
        check(replaced.IsSuccessStatusCode, "Draft DBC: cập nhật nội dung trên cùng ID");
        var current = await Json(replaced);
        check(current.GetProperty("id").GetInt32() == id && current.GetProperty("revision").GetInt64() == 2 && current.GetProperty("sha256").GetString() != originalSha, "Draft DBC: giữ ID, tăng revision, đổi SHA");
        check(await (await Send(HttpMethod.Get, $"/api/database/files/{id}/download", viewer)).Content.ReadAsStringAsync() == "updated database", "Draft DBC: download đúng nội dung mới");
        check((await http.GetAsync($"/api/client/database/files/{id}/download?sha256={originalSha}&testing=true")).StatusCode == HttpStatusCode.Conflict, "Draft DBC: Client dùng SHA cũ phải chọn lại");
        check((await Multipart($"/api/database/files/{id}/update", engineer, fields, "stale")).StatusCode == HttpStatusCode.Conflict, "Draft DBC: chặn cập nhật từ revision cũ");
        fields["revision"] = "2";
        check((await Multipart($"/api/database/files/{id}/update", engineer, fields, "")).StatusCode == HttpStatusCode.BadRequest, "Draft DBC: file rỗng bị từ chối");
        var status = await Send(HttpMethod.Patch, $"/api/database/files/{id}/status", admin, new { status = "Release", revision = 2 });
        check(status.IsSuccessStatusCode, "Draft DBC: chuyển Release");
        fields["revision"] = "3";
        check((await Multipart($"/api/database/files/{id}/update", engineer, fields, "release overwrite")).StatusCode == HttpStatusCode.Conflict, "Release DBC: chặn thay nội dung");
        check((await Send(HttpMethod.Delete, $"/api/database/files/{id}?revision=3", admin)).StatusCode == HttpStatusCode.Conflict, "Release DBC: chặn xoá");
        check((await Send(HttpMethod.Patch, $"/api/database/files/{id}/status", admin, new { status = "Draft", revision = 3 })).IsSuccessStatusCode, "Release DBC: chuyển về Draft");
        fields["revision"] = "4"; fields["phienBan"] = "edited-version";
        var metadata = await Multipart($"/api/database/files/{id}/update", engineer, fields, null);
        check(metadata.IsSuccessStatusCode && (await Json(metadata)).GetProperty("phienBan").GetString() == "edited-version", "Draft DBC: sửa thông tin không bắt buộc thay file");
        var history = await Json(await Send(HttpMethod.Get, $"/api/database/files/{id}/history", viewer));
        check(history.EnumerateArray().Any(x => x.GetProperty("action").GetString() == "replace"), "Draft DBC: thay file có lịch sử/outbox Client");
        check((await Send(HttpMethod.Delete, $"/api/database/files/{id}?revision=5", engineer)).IsSuccessStatusCode, "Draft DBC: cho phép xoá");

        var types = await Json(await Send(HttpMethod.Get, "/api/software/types", viewer));
        var software = new Dictionary<string,string> { ["loai"] = "phien-ban", ["ten"] = "Software draft edit", ["softwareTypeId"] = types[0].GetProperty("id").ToString() };
        created = await Json(await Multipart("/api/du-lieu-chung", engineer, software, "original software"));
        id = created.GetProperty("id").GetInt32();
        check(created.GetProperty("status").GetString() == "Draft" && created.GetProperty("revision").GetInt64() == 1, "Software: mặc định Draft/revision 1");
        software["revision"] = "1";
        check((await Multipart($"/api/du-lieu-chung/{id}/update", viewer, software, "denied")).StatusCode == HttpStatusCode.Forbidden, "Draft Software: Viewer không thay file");
        replaced = await Multipart($"/api/du-lieu-chung/{id}/update", engineer, software, "updated software");
        current = await Json(replaced);
        check(replaced.IsSuccessStatusCode && current.GetProperty("revision").GetInt64() == 2, "Draft Software: thay file và tăng revision");
        check(await (await Send(HttpMethod.Get, $"/api/du-lieu-chung/{id}/download", viewer)).Content.ReadAsStringAsync() == "updated software", "Draft Software: download nội dung mới");
        check((await Multipart($"/api/du-lieu-chung/{id}/update", engineer, software, "stale")).StatusCode == HttpStatusCode.Conflict, "Draft Software: chặn revision cũ");
        check((await Send(HttpMethod.Patch, $"/api/du-lieu-chung/{id}/status", engineer, new { status = "Release", revision = 2 })).StatusCode == HttpStatusCode.Forbidden, "Software: chỉ quyền Release được chuyển trạng thái");
        check((await Send(HttpMethod.Patch, $"/api/du-lieu-chung/{id}/status", admin, new { status = "Release", revision = 2 })).IsSuccessStatusCode, "Software: chuyển Release");
        software["revision"] = "3";
        check((await Multipart($"/api/du-lieu-chung/{id}/update", engineer, software, "release overwrite")).StatusCode == HttpStatusCode.Conflict, "Release Software: chặn thay file");
        check((await Send(HttpMethod.Patch, $"/api/du-lieu-chung/{id}", engineer, new { loai = "khac" })).StatusCode == HttpStatusCode.Conflict, "Release Software: chặn sửa/chuyển mục qua API cũ");
        check((await Send(HttpMethod.Delete, $"/api/du-lieu-chung/{id}", admin)).StatusCode == HttpStatusCode.Conflict, "Release Software: chặn xoá qua API cũ");
        check(await (await Send(HttpMethod.Get, $"/api/du-lieu-chung/{id}/download", viewer)).Content.ReadAsStringAsync() == "updated software", "Release Software: nội dung không bị thao tác bị chặn làm thay đổi");
        check((await Send(HttpMethod.Patch, $"/api/du-lieu-chung/{id}/status", admin, new { status = "Draft", revision = 3 })).IsSuccessStatusCode, "Release Software: chuyển lại Draft");
        software["revision"] = "4";
        check((await Multipart($"/api/du-lieu-chung/{id}/update", engineer, software, "final software")).IsSuccessStatusCode, "Software: thay file sau khi về Draft");
        check((await Send(HttpMethod.Delete, $"/api/du-lieu-chung/{id}?revision=4", engineer)).StatusCode == HttpStatusCode.Conflict, "Draft Software: xoá với revision cũ bị chặn");
        check((await Send(HttpMethod.Delete, $"/api/du-lieu-chung/{id}?revision=5", engineer)).IsSuccessStatusCode, "Draft Software: cho phép xoá");
    }
}
