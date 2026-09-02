using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class IdentityOptionsTests
{
    [Test]
    public void Identity_options_configure_the_exact_password_sign_in_and_lockout_policy()
    {
        using var scope = TestServices.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Password.RequiredLength.ShouldBe(12);
        options.Password.RequireUppercase.ShouldBeTrue();
        options.Password.RequireLowercase.ShouldBeTrue();
        options.Password.RequireDigit.ShouldBeTrue();
        options.Password.RequireNonAlphanumeric.ShouldBeTrue();
        options.Password.RequiredUniqueChars.ShouldBe(4);
        options.SignIn.RequireConfirmedEmail.ShouldBeTrue();
        options.Lockout.AllowedForNewUsers.ShouldBeTrue();
        options.Lockout.MaxFailedAccessAttempts.ShouldBe(5);
        options.Lockout.DefaultLockoutTimeSpan.ShouldBe(TimeSpan.FromMinutes(15));
    }

    [TestCase("Abcdefgh1!x", "PasswordTooShort", TestName = "Eleven_characters_are_rejected")]
    [TestCase("abcdefgh123!", "PasswordRequiresUpper", TestName = "Missing_uppercase_is_rejected")]
    [TestCase("ABCDEFGH123!", "PasswordRequiresLower", TestName = "Missing_lowercase_is_rejected")]
    [TestCase("Abcdefghijk!", "PasswordRequiresDigit", TestName = "Missing_digit_is_rejected")]
    [TestCase("Abcdefghijk1", "PasswordRequiresNonAlphanumeric", TestName = "Missing_symbol_is_rejected")]
    [TestCase("Aa!Aa!Aa!Aa!", "PasswordRequiresUniqueChars", TestName = "Fewer_than_four_unique_characters_are_rejected")]
    public async Task User_manager_rejects_passwords_that_violate_each_policy_rule(string password, string expectedErrorCode)
    {
        using var scope = TestServices.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"policy-{Guid.NewGuid():N}@example.test";

        var result = await userManager.CreateAsync(new ApplicationUser { UserName = email, Email = email }, password);

        result.Succeeded.ShouldBeFalse();
        result.Errors.Select(error => error.Code).ShouldContain(expectedErrorCode);
        (await userManager.FindByEmailAsync(email)).ShouldBeNull();
    }

    [Test]
    public async Task User_manager_accepts_a_policy_conformant_password_and_enables_lockout_for_the_new_user()
    {
        using var scope = TestServices.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var email = $"policy-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser { UserName = email, Email = email };

        try
        {
            var result = await userManager.CreateAsync(user, "Testing1234!");

            result.Succeeded.ShouldBeTrue(string.Join(",", result.Errors.Select(error => error.Code)));
            var persisted = await userManager.FindByEmailAsync(email);
            persisted.ShouldNotBeNull();
            persisted.LockoutEnabled.ShouldBeTrue();
            persisted.AccessFailedCount.ShouldBe(0);
            persisted.LockoutEnd.ShouldBeNull();
        }
        finally
        {
            // The shared integration database is not reset between fixtures; leave no identity behind.
            await userManager.DeleteAsync(user);
        }
    }
}
