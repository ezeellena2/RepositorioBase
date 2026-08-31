namespace CleanArchitecture.Infrastructure.Data;

public static class DatabaseMigrationExecutionPolicy
{
    private const string OpenApiDocumentGeneratorServerType =
        "Microsoft.Extensions.ApiDescription.Tool.Commands.GetDocumentCommandWorker+NoopServer";

    public static bool ShouldMigrate(string? serverTypeName) =>
        !IsOpenApiDocumentGeneration(serverTypeName);

    public static bool IsOpenApiDocumentGeneration(string? serverTypeName) =>
        string.Equals(serverTypeName, OpenApiDocumentGeneratorServerType, StringComparison.Ordinal);
}
