internal static class AspireExtensions
{
    public static IResourceBuilder<T> WithAspNetCoreEnvironment<T>(this IResourceBuilder<T> builder) 
        where T : IResourceWithEnvironment
    {
        builder.WithEnvironment(context =>
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            context.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"] = environment ?? "Development";
            // The outbox worker is a generic host, which reads DOTNET_ENVIRONMENT and ignores the ASP.NET one.
            // Setting only the latter left it running as Production, where the readiness checks refuse a local
            // key ring and a local mail drop — so it crashed on start and delivered nothing.
            context.EnvironmentVariables["DOTNET_ENVIRONMENT"] = environment ?? "Development";
        });

        return builder;
    }
}