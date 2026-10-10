using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BenchConsole.Api.Services.Files;

public static class FileDownload
{
    public static PhysicalFileResult Create(StoredFile file) => file.ContentType is null
        ? Create(file.Path, file.Name, file.Sha256)
        : new PhysicalFileResult(file.Path, file.ContentType)
        {
            FileDownloadName = file.Name,
            EnableRangeProcessing = true,
        };

    public static PhysicalFileResult Create(string path, string name, string sha256) => new(path, "application/octet-stream")
    {
        FileDownloadName = name,
        EnableRangeProcessing = true,
        EntityTag = new EntityTagHeaderValue($"\"{sha256}\""),
    };
}
