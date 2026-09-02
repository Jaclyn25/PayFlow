using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace PayFlow.Api.Swagger;

public class IdempotencyHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.RelativePath != null &&
            context.ApiDescription.RelativePath.Contains("transfers", StringComparison.OrdinalIgnoreCase) &&
            context.ApiDescription.HttpMethod?.Equals("POST", StringComparison.OrdinalIgnoreCase) == true)
        {
            operation.Parameters ??= new List<IOpenApiParameter>();

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Unique UUID idempotency key to prevent double transfers (e.g. 7d672c21-8f3b-412e-9d2a-123456789abc)",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Format = "uuid"
                }
            });
        }
    }
}
