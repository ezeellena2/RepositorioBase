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
    public async Task Known_password_rules_are_complete_deterministic_parameterized_and_never_use_descriptions()
    {
        const string hostileDescription = "candidate PropertyValue=secret PIN=1234";
        var firstValidator = new Mock<IPasswordValidator<ApplicationUser>>();
        var secondValidator = new Mock<IPasswordValidator<ApplicationUser>>();
        var options = new IdentityOptions();
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 4;
        var manager = CreateManager([firstValidator.Object, secondValidator.Object], options);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        manager.Setup(candidate => candidate.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        firstValidator.Setup(candidate => candidate.ValidateAsync(manager.Object, user, "candidate"))
            .ReturnsAsync(IdentityResult.Failed(
                Error(nameof(IdentityErrorDescriber.PasswordRequiresUpper), hostileDescription),
                Error(nameof(IdentityErrorDescriber.PasswordTooShort), hostileDescription),
                Error(nameof(IdentityErrorDescriber.PasswordRequiresDigit), hostileDescription)));
        secondValidator.Setup(candidate => candidate.ValidateAsync(manager.Object, user, "candidate"))
            .ReturnsAsync(IdentityResult.Failed(
                Error(nameof(IdentityErrorDescriber.PasswordRequiresUniqueChars), hostileDescription),
                Error(nameof(IdentityErrorDescriber.PasswordRequiresNonAlphanumeric), hostileDescription),
                Error(nameof(IdentityErrorDescriber.PasswordRequiresLower), hostileDescription),
                Error(nameof(IdentityErrorDescriber.PasswordTooShort), "different provider prose")));

        var result = await new IdentityCredentialService(manager.Object)
            .ReplacePasswordAsync(user.Id, "candidate", CancellationToken.None);

        result.Errors["newPassword"].Select(detail => detail.Code).ShouldBe([
            ValidationErrorCodes.PasswordRequiresDigit,
            ValidationErrorCodes.PasswordRequiresLowercase,
            ValidationErrorCodes.PasswordRequiresSymbol,
            ValidationErrorCodes.PasswordRequiresUniqueCharacters,
            ValidationErrorCodes.PasswordRequiresUppercase,
            ValidationErrorCodes.PasswordTooShort,
        ]);
        result.Errors["newPassword"].Single(detail => detail.Code == ValidationErrorCodes.PasswordTooShort)
            .Params["min"].ShouldBe(12);
        result.Errors["newPassword"].Single(detail => detail.Code == ValidationErrorCodes.PasswordRequiresUniqueCharacters)
            .Params["min"].ShouldBe(4);
        var serialized = JsonSerializer.Serialize(result.Errors);
        serialized.ShouldNotContain(hostileDescription);
        serialized.ShouldNotContain("candidate");
        serialized.ShouldNotContain("1234");
        manager.Verify(candidate => candidate.UpdateAsync(It.IsAny<ApplicationUser>()), Times.Never);
    }

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
    public async Task A_malformed_provider_refusal_without_errors_uses_the_generic_password_fallback()
    {
        var validator = new Mock<IPasswordValidator<ApplicationUser>>();
        var manager = CreateManager([validator.Object]);
        var user = new ApplicationUser { Id = Guid.NewGuid() };
        manager.Setup(candidate => candidate.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        validator.Setup(candidate => candidate.ValidateAsync(manager.Object, user, "candidate"))
            .ReturnsAsync(IdentityResult.Failed());

        var result = await new IdentityCredentialService(manager.Object)
            .ReplacePasswordAsync(user.Id, "candidate", CancellationToken.None);

        result.Failure.ShouldBe(CredentialWriteFailure.PasswordPolicy);
        result.Errors["newPassword"].ShouldHaveSingleItem().Code.ShouldBe(ValidationErrorCodes.PasswordPolicy);
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
        IReadOnlyCollection<IPasswordValidator<ApplicationUser>> passwordValidators,
        IdentityOptions? options = null)
    {
        var hasher = new Mock<IPasswordHasher<ApplicationUser>>();
        hasher.Setup(candidate => candidate.HashPassword(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .Returns("safe-password-hash");

        return new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(),
            Options.Create(options ?? new IdentityOptions()),
            hasher.Object,
            Array.Empty<IUserValidator<ApplicationUser>>(),
            passwordValidators,
            Mock.Of<ILookupNormalizer>(),
            new IdentityErrorDescriber(),
            Mock.Of<IServiceProvider>(),
            Mock.Of<ILogger<UserManager<ApplicationUser>>>());
    }

    private static IdentityError Error(string code, string description) => new()
    {
        Code = code,
        Description = description
    };
}
