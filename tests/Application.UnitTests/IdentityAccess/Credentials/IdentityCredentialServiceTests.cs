using System.Text.Json;
using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Infrastructure.Identity;
using CleanArchitecture.Infrastructure.IdentityAccess;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess.Credentials;

public sealed class IdentityCredentialServiceTests
{
    [Test]
    public async Task A_password_validator_refusal_is_the_only_password_policy_outcome_and_exposes_no_provider_prose()
    {
        const string hostileDescription = "PropertyValue=secret PIN=1234";
        var validator = new Mock<IPasswordValidator<ApplicationUser>>();
        var manager = CreateManager([validator.Object]);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        manager.Setup(candidate => candidate.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        validator.Setup(candidate => candidate.ValidateAsync(manager.Object, user, "candidate"))
            .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ProviderRule", Description = hostileDescription }));

        var result = await new IdentityCredentialService(manager.Object)
            .ReplacePasswordAsync(user.Id, "candidate", CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBe(CredentialWriteFailure.PasswordPolicy);
        result.Errors["newPassword"].ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
        JsonSerializer.Serialize(result.Errors).ShouldNotContain(hostileDescription);
        manager.Verify(candidate => candidate.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

    [Test]
    public async Task A_concurrency_update_uses_the_established_conflict_outcome()
    {
        var manager = CreateManager([]);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        manager.Setup(candidate => candidate.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        manager.Setup(candidate => candidate.UpdateAsync(user))
            .ReturnsAsync(IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure()));

        var result = await new IdentityCredentialService(manager.Object)
            .ReplacePasswordAsync(user.Id, "candidate", CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.Failure.ShouldBe(CredentialWriteFailure.Concurrency);
        result.Errors.ShouldBeEmpty();
        result.ToApplicationError().Code.ShouldBe("identity_concurrency_conflict");
    }

    [Test]
    public async Task A_missing_identity_is_a_concurrency_outcome_not_a_policy_refusal()
    {
        var manager = CreateManager([]);
        manager.Setup(candidate => candidate.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var result = await new IdentityCredentialService(manager.Object)
            .ReplacePasswordAsync(Guid.NewGuid(), "candidate", CancellationToken.None);

        result.Failure.ShouldBe(CredentialWriteFailure.Concurrency);
        result.Errors.ShouldBeEmpty();
    }

    [Test]
    public async Task An_unexpected_update_failure_throws_only_the_safe_exception_path()
    {
        const string hostileDescription = "Npgsql password=secret PropertyValue=1234";
        var manager = CreateManager([]);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        manager.Setup(candidate => candidate.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        manager.Setup(candidate => candidate.UpdateAsync(user)).ReturnsAsync(IdentityResult.Failed(
            new IdentityError { Code = "ProviderDatabaseFailure", Description = hostileDescription }));

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            new IdentityCredentialService(manager.Object)
                .ReplacePasswordAsync(user.Id, "candidate", CancellationToken.None));

        exception.Message.ShouldBe("Identity did not persist the password update.");
        exception.ToString().ShouldNotContain(hostileDescription);
    }

    private static Mock<UserManager<ApplicationUser>> CreateManager(
        IReadOnlyCollection<IPasswordValidator<ApplicationUser>> passwordValidators)
    {
        var hasher = new Mock<IPasswordHasher<ApplicationUser>>();
        hasher.Setup(candidate => candidate.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Returns("safe-password-hash");

        return new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(new IdentityOptions()),
            hasher.Object,
            Array.Empty<IUserValidator<ApplicationUser>>(),
            passwordValidators,
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());
    }
}
