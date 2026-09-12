using CleanArchitecture.Application.Common.Validation;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using FluentValidation;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Architecture;

public sealed class ValidationCodeVocabularyTests
{
    [Test]
    public void Every_current_FluentValidation_rule_declares_an_approved_owned_code()
    {
        var validators = typeof(ValidationErrorCodes).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IValidator).IsAssignableFrom(type))
            .Select(type => (Type: type, Validator: CreateValidator(type)))
            .ToArray();

        validators.ShouldNotBeEmpty();
        var componentCount = 0;
        foreach (var (type, validator) in validators)
        {
            foreach (var member in validator.CreateDescriptor().GetMembersWithValidators())
            {
                foreach (var (propertyValidator, options) in member)
                {
                    componentCount++;
                    options.ErrorCode.ShouldNotBeNullOrWhiteSpace(
                        $"{type.FullName}.{member.Key} ({propertyValidator.Name}) must declare its own WithErrorCode.");
                    ValidationErrorCodes.IsApproved(options.ErrorCode).ShouldBeTrue(
                        $"{type.FullName}.{member.Key} ({propertyValidator.Name}) uses unowned code '{options.ErrorCode}'.");
                    options.ErrorCode.ShouldNotBe(
                        ValidationErrorCodes.Invalid,
                        $"{type.FullName}.{member.Key} ({propertyValidator.Name}) is a known rule and must say what can be corrected.");
                }
            }
        }

        componentCount.ShouldBeGreaterThan(0);
    }

    [TestCase("Commands")]
    [TestCase("Queries")]
    public void Runnable_use_case_templates_teach_the_approved_validation_code_syntax(string kind)
    {
        var template = GetRepositoryPath(
            "templates", "ca-use-case", "FeatureName", kind, "CleanArchitectureUseCase", "CleanArchitectureUseCase.cs");
        var source = File.ReadAllText(template);
        var subject = kind == "Commands" ? "command" : "query";
        var validationCodeLine = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains(
                ".WithErrorCode(ValidationErrorCodes.Required);",
                StringComparison.Ordinal));
        var validationMessageLine = source.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Single(line => line.Contains(
                ".WithMessage(\"Some input is required.\")",
                StringComparison.Ordinal));

        source.ShouldContain("using CleanArchitecture.Application.Common.Validation;");
        source.ShouldContain($"RuleFor({subject} => {subject}.SomeInput)");
        validationMessageLine.TrimStart().ShouldNotStartWith("//");
        validationCodeLine.TrimStart().ShouldNotStartWith("//");
    }

    [Test]
    public void Server_owned_schema_has_an_exact_parameter_contract_for_every_code()
    {
        ValidationErrorCodes.Schema.Keys.Order(StringComparer.Ordinal).ShouldBe(new[]
        {
            ValidationErrorCodes.CuitCharacters,
            ValidationErrorCodes.CuitCheckDigit,
            ValidationErrorCodes.CuitLength,
            ValidationErrorCodes.DniCharacters,
            ValidationErrorCodes.DniLength,
            ValidationErrorCodes.EmailFormat,
            ValidationErrorCodes.Invalid,
            ValidationErrorCodes.PasswordPolicy,
            ValidationErrorCodes.PasswordRequiresDigit,
            ValidationErrorCodes.PasswordRequiresLowercase,
            ValidationErrorCodes.PasswordRequiresSymbol,
            ValidationErrorCodes.PasswordRequiresUniqueCharacters,
            ValidationErrorCodes.PasswordRequiresUppercase,
            ValidationErrorCodes.PasswordTooShort,
            ValidationErrorCodes.ProfileVersionFormat,
            ValidationErrorCodes.Required,
            ValidationErrorCodes.RoleIdsInvalid,
            ValidationErrorCodes.TooLong,
            ValidationErrorCodes.UnsupportedValue,
        });
        ValidationErrorCodes.Schema[ValidationErrorCodes.TooLong].ShouldBe(["max"]);
        ValidationErrorCodes.Schema[ValidationErrorCodes.PasswordTooShort].ShouldBe(["min"]);
        ValidationErrorCodes.Schema[ValidationErrorCodes.PasswordRequiresUniqueCharacters].ShouldBe(["min"]);
        foreach (var code in ValidationErrorCodes.Schema.Keys.Where(code =>
                     code is not ValidationErrorCodes.TooLong
                         and not ValidationErrorCodes.PasswordTooShort
                         and not ValidationErrorCodes.PasswordRequiresUniqueCharacters))
            ValidationErrorCodes.Schema[code].ShouldBeEmpty();
    }

    private static IValidator CreateValidator(Type type)
    {
        var constructor = type.GetConstructors().Single();
        var arguments = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType == typeof(IValidatedOptionalSession)
                ? AnonymousSession
                : throw new InvalidOperationException(
                    $"{type.FullName} has an unsupported validator dependency {parameter.ParameterType.FullName}."))
            .ToArray();

        return (IValidator)(constructor.Invoke(arguments)
            ?? throw new InvalidOperationException($"Could not construct validator {type.FullName}."));
    }

    private static readonly IValidatedOptionalSession AnonymousSession = new SessionStub(null, null);

    private sealed record SessionStub(Guid? IdentityId, string? Email, bool IsInvalid = false)
        : IValidatedOptionalSession;

    private static string GetRepositoryPath(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file {Path.Combine(parts)}.");
    }
}
