using System.Reflection;
using CleanArchitecture.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Api;

/// <summary>
/// The web layer's half of the identity architecture. It lives here rather than beside the other architecture
/// tests because only this project can see the web assembly at all — which is itself the rule being kept.
/// </summary>
public sealed class EndpointShapeTests
{
    private static readonly Assembly WebAssembly = typeof(Web.Endpoints.Identity).Assembly;

    /// <summary>
    /// An endpoint that reaches the database directly is a second place where authorization, tenant scoping and
    /// validation have to be remembered — and the one place the request pipeline cannot enforce them.
    /// </summary>
    [Test]
    public void No_endpoint_touches_the_database()
    {
        // Anchored, because a namespace that stopped matching would leave this passing over nothing.
        var endpoints = EndpointTypes().ToArray();
        endpoints.ShouldContain(typeof(Web.Endpoints.Identity));
        endpoints.Length.ShouldBeGreaterThan(3);

        foreach (var endpoint in endpoints)
        {
            foreach (var method in endpoint.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                foreach (var parameter in method.GetParameters())
                {
                    IsDatabaseAccess(parameter.ParameterType).ShouldBeFalse(
                        $"{endpoint.Name}.{method.Name} takes {parameter.ParameterType.Name}; endpoints reach the database through a request, not directly.");
                }
            }

            foreach (var field in endpoint.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                IsDatabaseAccess(field.FieldType).ShouldBeFalse(
                    $"{endpoint.Name} holds {field.FieldType.Name}; endpoints reach the database through a request, not directly.");
            }
        }
    }

    private static IEnumerable<Type> EndpointTypes() =>
        WebAssembly.GetTypes().Where(type =>
            type.Namespace is { } name &&
            (name == "CleanArchitecture.Web.Endpoints" || name.StartsWith("CleanArchitecture.Web.Endpoints.", StringComparison.Ordinal) ||
             name == "CleanArchitecture.Web.IdentityEndpoints"));

    private static bool IsDatabaseAccess(Type type) =>
        typeof(DbContext).IsAssignableFrom(type) ||
        typeof(IApplicationDbContext).IsAssignableFrom(type) ||
        (type.IsGenericType && type.GetGenericArguments().Any(IsDatabaseAccess));
}
