using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BenchConsole.Api.Tests;

public static class DocumentChecks
{
    public static async Task Run(HttpClient http, string admin, string viewer, string engineer, Action<bool, string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, string? token = null, string? key = null)
        {
            using var req = new HttpRequestMessage(method, path);
            if (token is not null) req.Headers.Authorization = new("Bearer", token);
            if (key is not null) req.Headers.Add("X-API-Key", key);
            if (body is not null) req.Content = JsonContent.Create(body);
            return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage response)
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) throw new Exception($"Document check: HTTP {response.StatusCode}: {text}");
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        var bytes = Encoding.UTF8.GetBytes("document fixture bytes");
        async Task<HttpResponseMessage> Upload(string name, string loai, string? token, string? key = null, string program = " FRS ")
        {
            using var form = new MultipartFormDataContent();
            foreach (var pair in new Dictionary<string, string> { ["ten"] = name, ["loai"] = loai, ["documentProgram"] = program,
                ["documentCategory"] = " Body ", ["documentFunction"] = " Door Lock ", ["documentType"] = " Specification " })
                form.Add(new StringContent(pair.Value), pair.Key);
            form.Add(new ByteArrayContent(bytes), "file", "document.txt");
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/du-lieu-chung") { Content = form };
            if (token is not null) req.Headers.Authorization = new("Bearer", token);
            if (key is not null) req.Headers.Add("X-API-Key", key);
            return await http.SendAsync(req);
        }
        check((await Upload("Doc forbidden", "tai-lieu", viewer)).StatusCode == HttpStatusCode.Forbidden, "Tài liệu: Viewer không upload metadata");
        var row = await Json(await Upload("Classified document", "tai-lieu", engineer)); var id = row.GetProperty("id").GetInt32();
        check(row.GetProperty("documentProgram").GetString() == "FRS" && row.GetProperty("documentCategory").GetString() == "Body"
            && row.GetProperty("documentFunction").GetString() == "Door Lock" && row.GetProperty("documentType").GetString() == "Specification", "Tài liệu: lưu và trim cả bốn trường");
        var path = "/api/du-lieu-chung/" + id;
        var filtered = await Json(await Send(HttpMethod.Get, "/api/du-lieu-chung?loai=tai-lieu&documentProgram=FRS&documentCategory=Body&documentFunction=Door%20Lock&documentType=Specification", token: viewer));
        check(filtered.EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == id), "Tài liệu: lọc kết hợp bốn trường");
        check((await Json(await Send(HttpMethod.Get, "/api/du-lieu-chung?loai=tai-lieu&documentProgram=UNKNOWN", token: viewer))).GetArrayLength() == 0, "Tài liệu: lọc không khớp trả rỗng");
        var search = await Json(await Send(HttpMethod.Get, "/api/du-lieu-chung?loai=tai-lieu&q=Door%20Lock", token: viewer));
        check(search.EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == id), "Tài liệu: tìm theo Function");
        var choices = await Json(await Send(HttpMethod.Get, "/api/du-lieu-chung/document-lookups", token: viewer));
        check(choices.GetProperty("documentProgram").EnumerateArray().Any(x => x.GetString() == "FRS"), "Tài liệu: gợi ý giá trị đã dùng");
        check((await Upload("Wrong section", "khac", engineer)).StatusCode == HttpStatusCode.BadRequest, "Tài liệu: metadata không áp dụng cho mục Khác");
        check((await Upload("Too long document", "tai-lieu", engineer, program: new string('x', 129))).StatusCode == HttpStatusCode.BadRequest, "Tài liệu: giới hạn 128 ký tự khi upload");
        check((await Send(HttpMethod.Patch, path, new { documentType = new string('x', 129) }, engineer)).StatusCode == HttpStatusCode.BadRequest, "Tài liệu: giới hạn 128 ký tự khi sửa");
        check((await Send(HttpMethod.Patch, path, new { documentProgram = "VF6" }, viewer)).StatusCode == HttpStatusCode.Forbidden, "Tài liệu: Viewer không sửa metadata");
        var edited = await Json(await Send(HttpMethod.Patch, path, new { documentProgram = "VF6", documentType = "Guide", revision = 1 }, engineer));
        check(edited.GetProperty("documentProgram").GetString() == "VF6" && edited.GetProperty("documentFunction").GetString() == "Door Lock", "Tài liệu: sửa một trường giữ các trường chưa truyền");
        check((await Send(HttpMethod.Patch, path, new { documentType = "Stale", revision = 1 }, engineer)).StatusCode == HttpStatusCode.Conflict, "Tài liệu: revision chặn ghi đè metadata");
        var cleared = await Json(await Send(HttpMethod.Patch, path, new { documentCategory = "", revision = 2 }, engineer));
        check(cleared.GetProperty("documentCategory").ValueKind == JsonValueKind.Null, "Tài liệu: cho phép xoá giá trị trường");
        var download = await Send(HttpMethod.Get, path + "/download", token: viewer);
        check((await download.Content.ReadAsByteArrayAsync()).SequenceEqual(bytes), "Tài liệu: sửa metadata giữ đúng bytes file");
        var moved = await Json(await Send(HttpMethod.Patch, path, new { loai = "khac", revision = 3 }, engineer));
        check(moved.GetProperty("documentProgram").ValueKind == JsonValueKind.Null && moved.GetProperty("documentType").ValueKind == JsonValueKind.Null,
            "Tài liệu: chuyển sang mục khác gỡ phân loại tài liệu");
        var issued = await Json(await Send(HttpMethod.Post, "/api/client-api-keys", new { name = "Document client", permissions = new[] { "DULIEU.UPLOAD", "DULIEU.VIEW" } }, admin));
        var key = issued.GetProperty("apiKey").GetString()!;
        var fromClient = await Json(await Upload("Client document", "tai-lieu", null, key));
        check(fromClient.GetProperty("documentProgram").GetString() == "FRS", "Tài liệu: API key upload đủ metadata");
        check((await Send(HttpMethod.Get, "/api/du-lieu-chung/document-lookups", key: key)).IsSuccessStatusCode, "Tài liệu: API key View đọc gợi ý");
    }
}
