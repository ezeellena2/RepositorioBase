using CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess;

public sealed class ConfirmationTokenTests
{
    private const string CanonicalToken = "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=";

    [Test]
    public void Canonical_confirmation_token_shape_accepts_exactly_a_32_byte_base64_value()
    {
        ConfirmationToken.IsCanonical(CanonicalToken).ShouldBeTrue();
    }

    [TestCase("")]
    [TestCase("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI")]
    [TestCase("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI==")]
    [TestCase("MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI_")]
    public void Noncanonical_confirmation_token_shapes_are_rejected_before_persistence_lookup(string token)
    {
        ConfirmationToken.IsCanonical(token).ShouldBeFalse();
    }
}
