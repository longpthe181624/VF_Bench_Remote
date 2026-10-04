using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BenchConsole.Api.Services;

public static class FileDownload
{
    public static PhysicalFileResult Create(string path, string name, string sha256) => new(path, "application/octet-stream")
    {
        FileDownloadName = name,
        EnableRangeProcessing = true,
        EntityTag = new EntityTagHeaderValue($"\"{sha256}\""),
    };
}
