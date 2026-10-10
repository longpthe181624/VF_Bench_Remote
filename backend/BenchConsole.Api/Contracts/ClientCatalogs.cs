using System.ComponentModel.DataAnnotations;

namespace BenchConsole.Api.Contracts;

public record ClientCatalogRequest(string? Ma, string? Ten);

public record ClientCatalogDto(int Id, string Ma, string Ten, bool Created);
