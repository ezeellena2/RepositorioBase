using CleanArchitecture.Application.Common.Localization;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using FluentValidation;

namespace CleanArchitecture.Application.IdentityAccess.Context.SetPreferredLanguage;

[Authorize(Permissions.IdentityAccountManage, false)]
public sealed record SetPreferredLanguageCommand(string Language) : IRequest<Result>;

public sealed class SetPreferredLanguageCommandValidator : AbstractValidator<SetPreferredLanguageCommand>
{
    public SetPreferredLanguageCommandValidator()
    {
        RuleFor(command => command.Language)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithErrorCode("language_required")
                .WithMessage("Choose a language.")
            .Must(LocalizationRegistry.IsCanonicalSupported)
                .WithErrorCode("language_unsupported")
                .WithMessage("Choose a supported language.")
            .OverridePropertyName("language");
    }
}

public sealed class SetPreferredLanguageCommandHandler(
    ICurrentSession currentSession,
    IIdentityAccountService identities) : IRequestHandler<SetPreferredLanguageCommand, Result>
{
    public async Task<Result> Handle(SetPreferredLanguageCommand request, CancellationToken cancellationToken)
    {
        if (currentSession.IsInvalid || currentSession.IdentityId is null)
        {
            return Result.Failure(IdentityAccessErrors.InvalidSession());
        }

        return await identities.SetPreferredLanguageAsync(
            currentSession.IdentityId.Value,
            request.Language,
            cancellationToken)
            ? Result.Success()
            : Result.Failure(IdentityAccessErrors.InvalidSession());
    }
}
