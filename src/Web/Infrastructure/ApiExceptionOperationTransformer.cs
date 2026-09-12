using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>Adds the stable public problem-code metadata declared by each endpoint.</summary>
internal sealed class ApiExceptionOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Responses ??= new OpenApiResponses();
        var contracts = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ApiProblemContractMetadata>()
            .SelectMany(metadata => metadata.Contracts)
            .DistinctBy(contract => (contract.StatusCode, contract.Code))
            .GroupBy(contract => contract.StatusCode);

        foreach (var statusContracts in contracts)
        {
            var status = statusContracts.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(status, out var response) || response is not OpenApiResponse concreteResponse)
            {
                continue;
            }

            concreteResponse.Extensions ??= new Dictionary<string, IOpenApiExtension>();
            concreteResponse.Extensions["x-problem-codes"] = new JsonNodeExtension(
                new JsonArray(statusContracts.Select(contract => JsonValue.Create(contract.Code)).ToArray()));

            if (statusContracts.Any(contract => contract.RequiresRetryAfter))
            {
                concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
                concreteResponse.Headers.TryAdd("Retry-After", new OpenApiHeader
                {
                    Description = "Seconds until the caller may retry.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32" }
                });
            }

        }

        var successContracts = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<ApiSuccessContractMetadata>()
            .Where(contract => contract.RequiresLocationHeader);

        foreach (var successContract in successContracts)
        {
            var status = successContract.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!operation.Responses.TryGetValue(status, out var response) || response is not OpenApiResponse concreteResponse)
            {
                continue;
            }

            concreteResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concreteResponse.Headers.TryAdd("Location", new OpenApiHeader
            {
                Description = "URI of the created resource.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" }
            });
        }

        return Task.CompletedTask;
    }
}
