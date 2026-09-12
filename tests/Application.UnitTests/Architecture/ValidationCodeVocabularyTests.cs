using CleanArchitecture.Application.Common.Validation;
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
            .Select(type => (Type: type, Validator: (IValidator?)Activator.CreateInstance(type)))
            .Where(candidate => candidate.Validator is not null)
            .ToArray();

        validators.ShouldNotBeEmpty();
        var componentCount = 0;
        foreach (var (type, validator) in validators)
        {
            foreach (var member in validator!.CreateDescriptor().GetMembersWithValidators())
            {
                foreach (var (propertyValidator, options) in member)
                {
                    componentCount++;
                    options.ErrorCode.ShouldNotBeNullOrWhiteSpace(
                        $"{type.FullName}.{member.Key} ({propertyValidator.Name}) must declare its own WithErrorCode.");
                    ValidationErrorCodes.IsApproved(options.ErrorCode).ShouldBeTrue(
                        $"{type.FullName}.{member.Key} ({propertyValidator.Name}) uses unowned code '{options.ErrorCode}'.");
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
            ValidationErrorCodes.Invalid,
            ValidationErrorCodes.PasswordPolicy,
            ValidationErrorCodes.Required,
            ValidationErrorCodes.TooLong,
            ValidationErrorCodes.UnsupportedValue,
        });
        ValidationErrorCodes.Schema[ValidationErrorCodes.TooLong].ShouldBe(["max"]);
        foreach (var code in ValidationErrorCodes.Schema.Keys.Where(code => code != ValidationErrorCodes.TooLong))
            ValidationErrorCodes.Schema[code].ShouldBeEmpty();
    }

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
