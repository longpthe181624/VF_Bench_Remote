using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace BenchConsole.Api.Tests;

public static class SoftwareTypeChecks
{
    public static async Task Run(HttpClient http, string admin, string viewer, string engineer, Action<bool,string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method,string path,string token,object? body=null)
        {
            using var req=new HttpRequestMessage(method,path);req.Headers.Authorization=new("Bearer",token);
            if(body is not null)req.Content=JsonContent.Create(body);return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage r)=>JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        async Task<HttpResponseMessage> Upload(string name,int? typeId,string loai="phien-ban")
        {
            using var form=new MultipartFormDataContent();form.Add(new ByteArrayContent([1,2,3]),"file",name+".bin");form.Add(new StringContent(loai),"loai");form.Add(new StringContent(name),"ten");
            if(typeId.HasValue)form.Add(new StringContent(typeId.Value.ToString()),"softwareTypeId");
            using var req=new HttpRequestMessage(HttpMethod.Post,"/api/du-lieu-chung"){Content=form};req.Headers.Authorization=new("Bearer",engineer);return await http.SendAsync(req);
        }
        var types=await Json(await Send(HttpMethod.Get,"/api/software/types",viewer));
        check(types.EnumerateArray().Any(t=>t.GetProperty("ten").GetString()=="Ứng dụng")&&types.EnumerateArray().Any(t=>t.GetProperty("ten").GetString()=="Lib"),"Software: seed Type ứng dụng/Lib");
        check((await Send(HttpMethod.Post,"/api/software/types",engineer,new{ten="Forbidden"})).StatusCode==HttpStatusCode.Forbidden,"Software: Engineer không cấu hình Type");
        var created=await Json(await Send(HttpMethod.Post,"/api/software/types",admin,new{ten="Custom tool"}));var typeId=created.GetProperty("id").GetInt32();
        check(typeId>0,"Software: Admin thêm tên Type tuỳ chỉnh");
        check((await Send(HttpMethod.Post,"/api/software/types",admin,new{ten="custom TOOL"})).StatusCode==HttpStatusCode.Conflict,"Software: chặn tên trùng khác hoa thường");
        var uploaded=await Upload("software-typed",typeId);check(uploaded.IsSuccessStatusCode,"Software: upload file với Type thật");var file=await Json(uploaded);var fileId=file.GetProperty("id").GetInt32();
        check(file.GetProperty("softwareTypeId").GetInt32()==typeId&&file.GetProperty("softwareType").GetString()=="Custom tool","Software: DTO trả ID và tên Type");
        check((await Send(HttpMethod.Patch,$"/api/software/types/{typeId}",admin,new{ten="Public theo phần mềm"})).IsSuccessStatusCode,"Software: đổi tên Type đang có file");
        var filtered=await Json(await Send(HttpMethod.Get,$"/api/du-lieu-chung?loai=phien-ban&softwareTypeId={typeId}",viewer));
        check(filtered.GetArrayLength()==1&&filtered[0].GetProperty("softwareType").GetString()=="Public theo phần mềm","Software: tên mới hiện trên file và lọc đúng Type");
        var count=await Json(await Send(HttpMethod.Get,"/api/software/types",viewer));check(count.EnumerateArray().First(t=>t.GetProperty("id").GetInt32()==typeId).GetProperty("soFile").GetInt32()==1,"Software: số file theo Type");
        check((await Send(HttpMethod.Delete,$"/api/software/types/{typeId}",admin)).StatusCode==HttpStatusCode.Conflict,"Software: chặn xoá Type đang dùng");
        check((await Upload("software-invalid",999999)).StatusCode==HttpStatusCode.BadRequest,"Software: chặn ID Type không tồn tại");
        check((await Upload("software-not-version",typeId,"dbc")).StatusCode==HttpStatusCode.BadRequest,"Software: Type không gán vào file DBC");
        var legacy=await Json(await Upload("software-legacy",null));check(legacy.GetProperty("softwareTypeId").ValueKind==JsonValueKind.Null,"Software: file cũ/API cũ vẫn dùng được, chưa phân loại");
        var legacyId=legacy.GetProperty("id").GetInt32();
        check((await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{legacyId}",engineer,new{softwareTypeId=typeId})).IsSuccessStatusCode,"Software: phân loại file cũ");
        var libId=types.EnumerateArray().First(t=>t.GetProperty("ten").GetString()=="Lib").GetProperty("id").GetInt32();
        var changed=await Json(await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{fileId}",engineer,new{ten="Software renamed",softwareTypeId=libId}));
        check(changed.GetProperty("softwareType").GetString()=="Lib"&&changed.GetProperty("ten").GetString()=="Software renamed","Software: đổi Type và tên file hiển thị");
        check((await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{legacyId}",engineer,new{softwareTypeId=0})).IsSuccessStatusCode,"Software: bỏ phân loại khi cần");
        check((await Send(HttpMethod.Delete,$"/api/software/types/{typeId}",admin)).IsSuccessStatusCode,"Software: xoá Type không còn file");
        check((await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{fileId}",engineer,new{loai="khac"})).IsSuccessStatusCode,"Software: chuyển ra khỏi kho phần mềm");
        var moved=await Json(await Send(HttpMethod.Get,"/api/du-lieu-chung?loai=khac",viewer));
        check(moved.EnumerateArray().First(t=>t.GetProperty("id").GetInt32()==fileId).GetProperty("softwareTypeId").ValueKind==JsonValueKind.Null,"Software: chuyển mục xoá liên kết Type phần mềm");
    }
}
