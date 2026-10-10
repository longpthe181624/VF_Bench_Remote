using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace BenchConsole.Api.Tests;

public static class FileStorageChecks
{
    public static async Task Run(HttpClient http, string admin, string viewer, string engineer, Action<bool,string> check)
    {
        async Task<HttpResponseMessage> Send(HttpMethod method,string path,string token,object? data=null)
        {
            var req=new HttpRequestMessage(method,path);
            req.Headers.Authorization=new("Bearer",token);
            if(data is not null)req.Content=JsonContent.Create(data);
            return await http.SendAsync(req);
        }
        async Task<HttpResponseMessage> Upload(string path,string token,Dictionary<string,string> fields,params (string Name,byte[] Bytes)[] files)
        {
            using var req=new HttpRequestMessage(HttpMethod.Post,path);
            req.Headers.Authorization=new("Bearer",token);
            var content=new MultipartFormDataContent();
            foreach(var f in files)content.Add(new ByteArrayContent(f.Bytes),"file",f.Name);
            foreach(var (k,v)in fields)content.Add(new StringContent(v),k);
            req.Content=content;return await http.SendAsync(req);
        }
        async Task<JsonElement> Json(HttpResponseMessage r)=>JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
        byte[] Zip(string name,string text)
        {
            using var bytes=new MemoryStream();
            using(var z=new ZipArchive(bytes,ZipArchiveMode.Create,true))
            using(var w=new StreamWriter(z.CreateEntry(name).Open()))w.Write(text);
            return bytes.ToArray();
        }

        var payload=Encoding.UTF8.GetBytes("Nội dung file thật\nBO_ 123 Test:");
        var upload=await Upload("/api/storage",admin,new(),("store.txt",payload),("store.txt",payload));
        check(upload.IsSuccessStatusCode,"Kho: hai file trùng cùng batch không gây lỗi unique");
        var uploaded=await Json(upload);var id=uploaded[0].GetProperty("id").GetInt32();
        check(uploaded[1].GetProperty("id").GetInt32()==id,"Kho: chống trùng ngay trong cùng batch");
        var download=await Send(HttpMethod.Get,$"/api/storage/files/{id}/download",admin);
        check((await download.Content.ReadAsByteArrayAsync()).SequenceEqual(payload),"Kho: tải xuống đúng byte gốc");
        var range=new HttpRequestMessage(HttpMethod.Get,$"/api/storage/files/{id}/download");range.Headers.Authorization=new("Bearer",admin);range.Headers.Range=new(0,3);
        var part=await http.SendAsync(range);
        check(part.StatusCode==HttpStatusCode.PartialContent&&(await part.Content.ReadAsByteArrayAsync()).SequenceEqual(payload[..4]),"Kho: hỗ trợ tải tiếp bằng Range");
        var other=await Send(HttpMethod.Get,$"/api/storage/files/{id}/download",viewer);
        check(other.StatusCode==HttpStatusCode.Forbidden,"Kho: người khác không được đọc file");
        var rename=await Send(HttpMethod.Patch,$"/api/storage/files/{id}",admin,new{tenFile="renamed.txt",moTa="Ghi chú mới"});
        check(rename.IsSuccessStatusCode&&(await Json(rename)).GetProperty("tenFile").GetString()=="renamed.txt","Kho: đổi tên và ghi chú");
        var badName=await Send(HttpMethod.Patch,$"/api/storage/files/{id}",admin,new{tenFile="../escape.txt"});
        check(badName.StatusCode==HttpStatusCode.BadRequest,"Kho: chặn tên file chứa đường dẫn");
        var forbiddenEdit=await Send(HttpMethod.Patch,$"/api/storage/files/{id}",viewer,new{moTa="Không được sửa"});
        check(forbiddenEdit.StatusCode==HttpStatusCode.Forbidden,"Kho: người khác không được sửa");
        var privateOther=await Upload("/api/storage",engineer,new(),("private-engineer.txt",payload));
        var otherId=(await Json(privateOther))[0].GetProperty("id").GetInt32();
        var adminRead=await Send(HttpMethod.Get,$"/api/storage/files/{otherId}/download",admin);
        var adminEdit=await Send(HttpMethod.Patch,$"/api/storage/files/{otherId}",admin,new{moTa="Không được sửa"});
        var adminDelete=await Send(HttpMethod.Delete,$"/api/storage/files/{otherId}",admin);
        check(adminRead.StatusCode==HttpStatusCode.Forbidden&&adminEdit.StatusCode==HttpStatusCode.Forbidden&&adminDelete.StatusCode==HttpStatusCode.Forbidden,"Kho: Admin cũng không được đọc / sửa / xoá file riêng người khác");
        var mixedEmpty=await Upload("/api/storage",admin,new(),("empty.txt",[]),("must-not-save.txt",payload));
        check(mixedEmpty.StatusCode==HttpStatusCode.BadRequest,"Kho: một file rỗng phải từ chối cả batch trước khi ghi");
        var notSaved=await Send(HttpMethod.Get,"/api/storage?q=must-not-save",admin);
        check((await Json(notSaved)).GetArrayLength()==0,"Kho: batch không hợp lệ không lưu một phần");
        var longDescription=await Send(HttpMethod.Patch,$"/api/storage/files/{id}",admin,new{moTa=new string('x',513)});
        check(longDescription.StatusCode==HttpStatusCode.BadRequest,"Kho: mô tả quá giới hạn trả 400 thay vì lỗi SQL");
        var empty=await Upload("/api/storage",admin,new(),("empty.txt",[]));
        check(empty.StatusCode==HttpStatusCode.BadRequest,"Kho: báo lỗi file rỗng");
        var query=await Send(HttpMethod.Get,"/api/storage?q=renamed",admin);
        check((await Json(query)).EnumerateArray().Any(r=>r.GetProperty("id").GetInt32()==id),"Kho: tìm kiếm tên file đã đổi");
        var parallel=await Task.WhenAll(Enumerable.Range(0,4).Select(_=>Upload("/api/storage",admin,new(),("parallel.txt",payload))));
        var parallelIds=new List<int>();
        foreach(var result in parallel)parallelIds.Add((await Json(result))[0].GetProperty("id").GetInt32());
        check(parallel.All(r=>r.IsSuccessStatusCode)&&parallelIds.Distinct().Count()==1,"Kho: upload trùng đồng thời trả cùng bản ghi");

        var shared=await Upload("/api/du-lieu-chung",admin,new(){["loai"]="dbc",["ten"]="File storage integration"},("sample.dbc",payload));
        var sharedId=(await Json(shared)).GetProperty("id").GetInt32();
        var moved=await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{sharedId}",admin,new{ten="Đã đổi tên",loai="khac",moTa="Đã chuyển mục"});
        var movedJson=await Json(moved);
        check(moved.IsSuccessStatusCode&&movedJson.GetProperty("loai").GetString()=="khac","Dữ liệu chung: đổi tên, chuyển mục, sửa mô tả");
        var sharedCopy=await Upload("/api/du-lieu-chung",admin,new(){["loai"]="khac",["ten"]="Giữ nội dung"},("copy.dbc",payload));
        var copyId=(await Json(sharedCopy)).GetProperty("id").GetInt32();
        var duplicate=await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{sharedId}",admin,new{ten="Giữ nội dung"});
        check(duplicate.StatusCode==HttpStatusCode.Conflict,"Dữ liệu chung: chặn trùng tên khi chuyển / sửa");
        var unknownCategory=await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{sharedId}",admin,new{loai="missing-storage-category"});
        check(unknownCategory.StatusCode==HttpStatusCode.BadRequest,"Dữ liệu chung: chặn mục không tồn tại");
        var denied=await Send(HttpMethod.Patch,$"/api/du-lieu-chung/{sharedId}",viewer,new{ten="Không được sửa"});
        check(denied.StatusCode==HttpStatusCode.Forbidden,"Dữ liệu chung: Viewer không được sửa");
        await Send(HttpMethod.Delete,$"/api/du-lieu-chung/{sharedId}",admin);
        var copy=await Send(HttpMethod.Get,$"/api/du-lieu-chung/{copyId}/download",admin);
        check((await copy.Content.ReadAsByteArrayAsync()).SequenceEqual(payload),"Dữ liệu chung: xoá một bản không mất file còn được dùng");

        var manual=await Upload("/api/test-cases",admin,new(){["ten"]="Manual_storage",["kieuTest"]="manual"},("manual.zip",Zip("cases/sample.xlsx","Excel fixture")));
        var manualJson=await Json(manual);var packageId=manualJson.GetProperty("id").GetInt32();
        check(manual.Headers.Location?.OriginalString.EndsWith($"/api/test-cases/{packageId}") == true,
            "Gói: upload trả Location trỏ đến gói vừa tạo");
        using var packageDownload = await Send(HttpMethod.Get, $"/api/test-cases/{packageId}/download", admin);
        check(packageDownload.Content.Headers.ContentType?.MediaType == "application/zip"
            && (packageDownload.Content.Headers.ContentDisposition?.FileNameStar
                ?? packageDownload.Content.Headers.ContentDisposition?.FileName?.Trim('"')) == "Manual_storage.zip",
            "Gói: giữ MIME ZIP và tên file tải xuống");
        check(manual.StatusCode==HttpStatusCode.Created&&manualJson.GetProperty("kieuTest").GetString()=="manual"&&manualJson.GetProperty("soTestCase").GetInt32()==1,"Gói: lưu ZIP Excel manual và số file");
        var blocked=await Send(HttpMethod.Post,"/api/devices/BENCH-CAY/deploy",admin,new{goiId=packageId});
        check(blocked.StatusCode==HttpStatusCode.BadRequest,"Gói: không triển khai Excel manual tới agent tự động");
        var auto=await Upload("/api/test-cases",admin,new(){["ten"]="Auto_storage"},("auto.zip",Zip("cases/test.tc","TC data")));
        check(auto.StatusCode==HttpStatusCode.Created,"Gói: tương thích upload auto cũ");
        var wrong=await Upload("/api/test-cases",admin,new(){["ten"]="Wrong_storage"},("wrong.zip",Zip("cases/sample.xlsx","Excel fixture")));
        check(wrong.StatusCode==HttpStatusCode.BadRequest,"Gói: Excel không được nhận thành gói auto");
        var traversal=await Upload("/api/test-cases",admin,new(){["ten"]="Traversal_storage"},("unsafe.zip",Zip("../test.tc","TC data")));
        check(traversal.StatusCode==HttpStatusCode.BadRequest,"Gói: chặn ZIP chứa đường dẫn thoát thư mục");
        var broken=await Upload("/api/test-cases",admin,new(){["ten"]="Broken_storage"},("broken.zip",payload));
        check(broken.StatusCode==HttpStatusCode.BadRequest,"Gói: chặn ZIP giả / hỏng");
        var samePackage=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Upload("/api/test-cases",admin,new(){["ten"]="Concurrent_storage"},("parallel.zip",Zip("test.tc","TC data")))));
        check(samePackage.Count(r=>r.StatusCode==HttpStatusCode.Created)==1&&samePackage.Count(r=>r.StatusCode==HttpStatusCode.Conflict)==1,"Gói: upload cùng tên đồng thời trả 201 / 409, không lỗi SQL");
        var report=await Upload("/api/runs/storage-test/report",admin,new(),("result.txt",payload),("result.txt",payload));
        var reports=await Json(report);var reportId=reports[0].GetProperty("id").GetInt32();
        check(report.IsSuccessStatusCode&&reports[1].GetProperty("id").GetInt32()==reportId,"Báo cáo: chống trùng trong batch");
        var reportFile=await Send(HttpMethod.Get,$"/api/runs/reports/{reportId}/download",admin);
        check((await reportFile.Content.ReadAsByteArrayAsync()).SequenceEqual(payload),"Báo cáo: lưu và tải đúng byte gốc");
        await Send(HttpMethod.Delete,$"/api/storage/files/{id}",admin);
        var removed=await Send(HttpMethod.Get,$"/api/storage/files/{id}/download",admin);
        check(removed.StatusCode==HttpStatusCode.NotFound,"Kho: file đã xoá không tải lại được");
    }
}
