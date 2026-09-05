using System.Reflection;
using CleanArchitecture.Application.Common.Security;
using FluentValidation;
using MediatR;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

/// <summary>
/// The onboarding half of the Platform slice, checked by shape. Both requests are reached by someone who has no
/// account yet — or has one and is not signed in — so both are public by declaration, and both carry a token,
/// which is what makes them sensitive (IA-REQ-041).
/// </summary>
public sealed class PlatformInvitationApplicationShapeTests
{
    private const string Namespace = "CleanArchitecture.Application.IdentityAccess.Platform.Invitations";

    private static readonly Assembly ApplicationAssembly = typeof(AuthorizeAttribute).Assembly;

    private static Type Require(string name) =>
        ApplicationAssembly.GetType($"{Namespace}.{name}").ShouldNotBeNull(name);

    [TestCase("RegisterPlatformInviteeCommand")]
    [TestCase("RegisterPlatformInviteeCommandValidator")]
    [TestCase("ConfirmPlatformInviteeCommand")]
    [TestCase("ConfirmPlatformInviteeCommandValidator")]
    public void The_onboarding_request_exists(string name) => Require(name);

    /// <summary>
    /// Public by declaration rather than by omission: the recipient of a Platform invitation has no session, and
    /// may have no account at all, so requiring one would make the invitation impossible to answer.
    /// </summary>
    [TestCase("RegisterPlatformInviteeCommand")]
    [TestCase("ConfirmPlatformInviteeCommand")]
    public void The_onboarding_request_is_explicitly_public(string name)
    {
        var request = Require(name);

        typeof(IPublicRequest).IsAssignableFrom(request).ShouldBeTrue($"{name} must declare itself public.");
        request.GetCustomAttributes<AuthorizeAttribute>(false).ShouldBeEmpty($"{name} cannot also be authorized.");
        typeof(IBaseRequest).IsAssignableFrom(request).ShouldBeTrue($"{name} must be a request.");
    }

    /// <summary>Both carry a token, so neither may be logged in full (IA-REQ-029).</summary>
    [TestCase("RegisterPlatformInviteeCommand")]
    [TestCase("ConfirmPlatformInviteeCommand")]
    public void The_onboarding_request_is_sensitive(string name) =>
        typeof(ISensitiveRequest).IsAssignableFrom(Require(name)).ShouldBeTrue($"{name} carries a token.");

    /// <summary>
    /// Registration carries the invitation token and a password, because a recipient with no account has to be
    /// able to create one with a password they chose — the system never generates a default (IA-REQ-041).
    /// </summary>
    [Test]
    public void Registration_carries_the_invitation_token_and_a_submitted_password()
    {
        var request = Require("RegisterPlatformInviteeCommand");

        request.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["Password", "Token"]);
    }

    /// <summary>Confirmation consumes both tokens: the invitation's, and the one the confirmation email carried.</summary>
    [Test]
    public void Confirmation_carries_both_tokens_and_no_credential()
    {
        var request = Require("ConfirmPlatformInviteeCommand");

        request.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
            .ShouldBe(["ConfirmationToken", "Token"]);
    }

    /// <summary>
    /// A validator may only judge the request's shape. Judging its state — whether the token resolves, whether the
    /// address has an account — would answer differently for a real token than for an unknown one, which is the
    /// oracle the neutral flow exists to prevent.
    /// </summary>
    [TestCase("RegisterPlatformInviteeCommandValidator")]
    [TestCase("ConfirmPlatformInviteeCommandValidator")]
    public void The_validator_reads_no_state(string name)
    {
        var validator = Require(name);

        validator.BaseType!.GetGenericTypeDefinition().ShouldBe(typeof(AbstractValidator<>));
        validator.GetConstructors().ShouldHaveSingleItem().GetParameters()
            .ShouldBeEmpty($"{name} must not take a dependency it could read state through.");
    }

    /// <summary>
    /// Neither request may name a tenant, a membership, a role or an identity. Onboarding gets a recipient as far
    /// as being confirmable and no further: membership comes only after the MFA gates (IA-REQ-041).
    /// </summary>
    [TestCase("RegisterPlatformInviteeCommand")]
    [TestCase("ConfirmPlatformInviteeCommand")]
    public void The_onboarding_request_cannot_name_an_authority_to_grant(string name)
    {
        foreach (var property in Require(name).GetProperties())
        {
            foreach (var forbidden in new[] { "tenant", "membership", "role", "permission", "identity", "email", "owner" })
            {
                property.Name.Contains(forbidden, StringComparison.OrdinalIgnoreCase).ShouldBeFalse(
                    $"{name}.{property.Name} would let a caller choose {forbidden}.");
            }
        }
    }
}
