using System.Reflection;
using CleanArchitecture.Application.Common.Models;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Models;

public sealed class ResultContractShapeTests
{
    [Test]
    public void Result_construction_is_limited_to_success_and_typed_failure_factories()
    {
        typeof(Result).GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();
        typeof(Result<Guid>).GetConstructors(BindingFlags.Instance | BindingFlags.Public).ShouldBeEmpty();

        typeof(Result).GetMethod(nameof(Result.Success), BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull();
        typeof(Result).GetMethod(nameof(Result.Failure), BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull();
        typeof(Result<Guid>).GetMethod(nameof(Result<Guid>.Success), BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull();
        typeof(Result<Guid>).GetMethod(nameof(Result<Guid>.Failure), BindingFlags.Public | BindingFlags.Static).ShouldNotBeNull();
    }

    [Test]
    public void Result_exposes_no_generic_http_envelope_members()
    {
        var memberNames = typeof(Result).GetMembers(BindingFlags.Instance | BindingFlags.Public).Select(member => member.Name);

        memberNames.ShouldNotContain("Data");
        memberNames.ShouldNotContain("Errors");
        memberNames.ShouldNotContain("Success");
        memberNames.ShouldNotContain("StatusCode");
    }

    [Test]
    public void Application_error_has_only_expected_public_failure_categories()
    {
        Enum.GetNames<ApplicationErrorCategory>().ShouldBe([
            nameof(ApplicationErrorCategory.Validation),
            nameof(ApplicationErrorCategory.Authentication),
            nameof(ApplicationErrorCategory.Authorization),
            nameof(ApplicationErrorCategory.NotFound),
            nameof(ApplicationErrorCategory.Conflict),
            nameof(ApplicationErrorCategory.RateLimited),
            nameof(ApplicationErrorCategory.Unavailable)
        ]);
    }
}
