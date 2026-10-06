using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace BenchConsole.Api.Auth;

public sealed class ClientApiKeyOpenApi : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<AllowClientApiKeyAttribute>().Any()) return;
        operation.Security = [Requirement("Bearer"), Requirement("ClientApiKey")];
    }
    private static OpenApiSecurityRequirement Requirement(string scheme) => new()
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } }] = [],
    };
}
