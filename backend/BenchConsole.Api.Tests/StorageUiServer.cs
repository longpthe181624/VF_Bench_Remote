using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BenchConsole.Api.Tests;

// Chạy FE trên trình duyệt với API thật và database / kho tạm của bộ kiểm thử.
public static class StorageUiServer
{
    public static async Task<int> Run()
    {
        using var factory = new MayChuThu();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });
        // Only the temporary test server gets this idle device; no real MQTT or hardware.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Api.Data.AppDbContext>();
            var project = new Core.Models.DuAn { Ma = "SIMULATION", Ten = "Simulation UI" };
            db.Benches.Add(new Core.Models.Bench { Code = "SIMULATION-UI", Ten = "Simulation bench",
                State = Core.Models.BenchState.Idle, DuAns = [new Core.Models.ThietBiDuAn { DuAn = project }] });
            using var stream = new MemoryStream();
            using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create, true))
            using (var entry = zip.CreateEntry("simulation.tc").Open()) entry.Write(System.Text.Encoding.UTF8.GetBytes("simulation only"));
            stream.Position = 0;
            db.Benches.Add(new Core.Models.Bench { Code = "SIMULATION-STANDALONE", Ten = "Standalone simulation", State = Core.Models.BenchState.Idle });
            var saved = await scope.ServiceProvider.GetRequiredService<Api.Services.KhoGoiTestCase>().LuuAsync(stream, default);
            db.GoiTestCases.Add(new Core.Models.GoiTestCase { Ten = "Simulation-UI", TenFileGoc = "simulation.zip",
                Sha256 = saved.Sha256, KichThuoc = saved.KichThuoc, SoTestCase = 1 });
            await db.SaveChangesAsync();
        }
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = Api.Services.KiemTraTep.TranYeuCau);
        var app = builder.Build();
        app.Urls.Add("http://127.0.0.1:5077");
        app.Run(async context =>
        {
            if (context.Request.Path == "/__test/stop" && context.Request.Method == "POST")
            {
                await context.Response.WriteAsync("Stopping temporary test server");
                app.Lifetime.StopApplication();
                return;
            }
            using var req = new HttpRequestMessage(new HttpMethod(context.Request.Method), context.Request.Path + context.Request.QueryString);
            if (context.Request.ContentLength > 0) req.Content = new StreamContent(context.Request.Body);
            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;
                if (!req.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                    req.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
            using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
            context.Response.StatusCode = (int)res.StatusCode;
            foreach (var header in res.Headers.Concat(res.Content.Headers))
                if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    context.Response.Headers[header.Key] = header.Value.ToArray();
            await res.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
        Console.WriteLine("Storage UI test server: http://127.0.0.1:5077 (temporary database and files)");
        await app.RunAsync();
        return 0;
    }
}
