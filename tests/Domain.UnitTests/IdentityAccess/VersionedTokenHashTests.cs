using CleanArchitecture.Domain.IdentityAccess.Security;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class VersionedTokenHashTests
{
    /// <summary>
    /// The counterexample that shape validation could never catch: a generated token is 32 random bytes in
    /// Base64 and a digest is 32 bytes in Base64, so "v1:" + token is indistinguishable from a hash by
    /// inspection. There is no entry point that accepts a precomputed value from a caller holding a token, so
    /// handing the token over hashes it instead of storing it.
    /// </summary>
    [Test]
    public void Hashing_a_token_never_yields_the_token_behind_a_version_tag()
    {
        const string rawToken = "Zm9vYmFyLXJhdy10b2tlbi0zMi1ieXRlcy1sb25nISE=";

        var hash = VersionedTokenHash.Of(rawToken);

        hash.Value.ShouldNotBe($"v1:{rawToken}", "passing the token where the hash belongs must hash it, not store it.");
        hash.Value.ShouldNotContain(rawToken);
        hash.Matches(rawToken).ShouldBeTrue();
    }

    [Test]
    public void Hashing_is_deterministic_and_separates_distinct_tokens()
    {
        VersionedTokenHash.Of("one").ShouldBe(VersionedTokenHash.Of("one"));
        VersionedTokenHash.Of("one").ShouldNotBe(VersionedTokenHash.Of("two"));
        VersionedTokenHash.Of("one").Matches("two").ShouldBeFalse();
    }

    [Test]
    public void A_hash_carries_its_version_and_a_digest_of_the_declared_size()
    {
        var value = VersionedTokenHash.Of("token").Value;

        value.ShouldStartWith("v1:");
        value["v1:".Length..].Length.ShouldBe(44, "Base64 of a 32-byte digest is 44 characters.");
        Convert.FromBase64String(value["v1:".Length..]).Length.ShouldBe(32);
    }

    [TestCase("", TestName = "an empty value")]
    [TestCase("   ", TestName = "a blank value")]
    [TestCase("Zm9vYmFyLXJhdy10b2tlbg==", TestName = "a value with no version tag")]
    [TestCase("v1:", TestName = "a version tag with no digest")]
    [TestCase("v1:AAAA", TestName = "a digest of the wrong size")]
    [TestCase("v2:crGz8gJN0Y2BXmF1x6jFbj1Qh4seDVvGgCoIBAFwCQs=", TestName = "a version this project cannot produce")]
    [TestCase("v1:raw-secret-that-is-not-base64-at-all-here!!", TestName = "a digest that is not Base64")]
    public void A_persisted_value_that_this_type_never_produced_is_refused(string persisted) =>
        Should.Throw<ArgumentException>(() => VersionedTokenHash.FromPersistedValue(persisted));

    [Test]
    public void A_persisted_value_round_trips_to_the_hash_that_produced_it()
    {
        var original = VersionedTokenHash.Of("round-trip-token");

        VersionedTokenHash.FromPersistedValue(original.Value).ShouldBe(original);
    }

    [Test]
    public void Hashing_requires_a_token()
    {
        Should.Throw<ArgumentException>(() => VersionedTokenHash.Of(""));
        Should.Throw<ArgumentException>(() => VersionedTokenHash.Of("   "));
        VersionedTokenHash.Of("token").Matches("").ShouldBeFalse();
    }
}
