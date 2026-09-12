using System.Text.Json.Serialization;
using CleanArchitecture.Application.Common.Validation;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchitecture.Web.Infrastructure;

/// <summary>Public RFC 9457 error payload shared by all Web boundaries.</summary>
public sealed class ApiProblemDetails : ProblemDetails
{
    public required string Code { get; init; }

    public required string TraceId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, ValidationErrorDetail[]>? Errors { get; init; }
}
