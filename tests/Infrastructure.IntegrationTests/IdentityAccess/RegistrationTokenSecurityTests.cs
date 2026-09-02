using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.AspNetCore.Http;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class RegistrationTokenSecurityTests
{
    [Test]
    public void Secure_token_generator_creates_distinct_32_byte_opaque_tokens()
    {
        var generator = new SecureTokenGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Convert.FromBase64String(first).Length.ShouldBe(32);
        Convert.FromBase64String(second).Length.ShouldBe(32);
        first.ShouldNotBe(second);
    }

    [Test]
    public void Versioned_token_hasher_verifies_its_hash_without_retaining_raw_token()
    {
        var generator = new SecureTokenGenerator();
        var hasher = new VersionedTokenHasher();
        var rawToken = generator.Generate();

        var hash = hasher.Hash(rawToken);

        hash.ShouldStartWith("v1:");
        hash.ShouldNotContain(rawToken);
        hasher.Verify(rawToken, hash).ShouldBeTrue();
        hasher.Verify(generator.Generate(), hash).ShouldBeFalse();
    }

    [Test]
    public void Supplied_unvalidated_task8_authentication_cookie_fails_closed()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "__Host-ia-auth=untrusted";
        var accessor = new HttpContextAccessor { HttpContext = context };

        var session = new ValidatedOptionalSession(accessor);

        session.IsInvalid.ShouldBeTrue();
        session.IdentityId.ShouldBeNull();
        session.Email.ShouldBeNull();
    }
}
