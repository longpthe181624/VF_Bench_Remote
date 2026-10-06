using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.AspNetCore.Authorization;

namespace BenchConsole.Api.Auth;

public sealed class ClientApiKeyOpenApi : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<AllowClientApiKeyAttribute>().Any()) return;
        var keyOnly = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<AuthorizeAttribute>()
            .Any(x => x.AuthenticationSchemes == ClientKeyAccess.Scheme);
        operation.Security = keyOnly ? [Requirement("ClientApiKey")] : [Requirement("Bearer"), Requirement("ClientApiKey")];
    }
    private static OpenApiSecurityRequirement Requirement(string scheme) => new()
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = scheme } }] = [],
    };
}
