using CleanArchitecture.Web.Infrastructure.Identity;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class LoginRateLimitPartitionerTests
{
    private const string OpaqueKeyPattern = "^[0-9a-f]{64}$";

    [TestCase("  Owner@Example.TEST ", "owner@example.test")]
    [TestCase("owner@example.test", "owner@example.test")]
    [TestCase("\tOWNER@EXAMPLE.TEST\n", "owner@example.test")]
    public void Normalize_email_trims_and_lower_cases_exactly_like_the_session_command(string raw, string expected)
    {
        LoginRateLimitPartitioner.NormalizeEmail(raw).ShouldBe(expected);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void Normalize_email_returns_null_when_there_is_no_account_to_partition(string? raw)
    {
        LoginRateLimitPartitioner.NormalizeEmail(raw).ShouldBeNull();
    }

    [Test]
    public void Account_keys_are_equal_for_every_spelling_of_the_same_account_and_differ_across_accounts()
    {
        var canonical = LoginRateLimitPartitioner.AccountKey("owner@example.test");

        LoginRateLimitPartitioner.AccountKey("  Owner@Example.TEST ").ShouldBe(canonical);
        LoginRateLimitPartitioner.AccountKey("OWNER@EXAMPLE.TEST").ShouldBe(canonical);
        LoginRateLimitPartitioner.AccountKey("other@example.test").ShouldNotBe(canonical);
    }

    [Test]
    public void Keys_are_opaque_64_lowercase_hex_digests_that_never_contain_the_input()
    {
        var account = LoginRateLimitPartitioner.AccountKey("Owner@Example.test");
        var client = LoginRateLimitPartitioner.ClientKey("203.0.113.10");

        account.ShouldMatch(OpaqueKeyPattern);
        client.ShouldMatch(OpaqueKeyPattern);
        account.ShouldNotContain("owner", Case.Insensitive);
        account.ShouldNotContain("example", Case.Insensitive);
        client.ShouldNotContain("203.0.113.10");
        client.ShouldNotContain("113");
    }

    [Test]
    public void Account_and_client_partitions_use_separate_key_domains()
    {
        LoginRateLimitPartitioner.AccountKey("203.0.113.10").ShouldNotBe(LoginRateLimitPartitioner.ClientKey("203.0.113.10"));
    }

    [Test]
    public void Client_keys_differ_per_address_and_are_stable_for_one_address()
    {
        var first = LoginRateLimitPartitioner.ClientKey("203.0.113.10");

        LoginRateLimitPartitioner.ClientKey("203.0.113.10").ShouldBe(first);
        LoginRateLimitPartitioner.ClientKey("203.0.113.11").ShouldNotBe(first);
        LoginRateLimitPartitioner.ClientKey("2001:db8::10").ShouldNotBe(first);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void Keys_require_a_non_empty_input(string input)
    {
        Should.Throw<ArgumentException>(() => LoginRateLimitPartitioner.AccountKey(input));
        Should.Throw<ArgumentException>(() => LoginRateLimitPartitioner.ClientKey(input));
    }
}
