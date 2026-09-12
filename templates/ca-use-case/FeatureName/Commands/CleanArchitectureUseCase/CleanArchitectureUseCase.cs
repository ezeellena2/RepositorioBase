using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Validation;

namespace CleanArchitecture.Application.FeatureName.Commands.CleanArchitectureUseCase;

//#if (hasReturnType)
public record CleanArchitectureUseCaseCommand : IRequest<TReturnType>
//#else
public record CleanArchitectureUseCaseCommand : IRequest
//#endif
{
    public string SomeInput { get; init; } = string.Empty;
}

public class CleanArchitectureUseCaseCommandValidator : AbstractValidator<CleanArchitectureUseCaseCommand>
{
    public CleanArchitectureUseCaseCommandValidator()
    {
        RuleFor(command => command.SomeInput)
            .NotEmpty().WithMessage("Some input is required.")
            .WithErrorCode(ValidationErrorCodes.Required);
    }
}

//#if (hasReturnType)
public class CleanArchitectureUseCaseCommandHandler : IRequestHandler<CleanArchitectureUseCaseCommand, TReturnType>
//#else
public class CleanArchitectureUseCaseCommandHandler : IRequestHandler<CleanArchitectureUseCaseCommand>
//#endif
{
    private readonly IApplicationDbContext _context;

    public CleanArchitectureUseCaseCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

//#if (hasReturnType)
    public async Task<TReturnType> Handle(CleanArchitectureUseCaseCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
//#else
    public async Task Handle(CleanArchitectureUseCaseCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
//#endif
}
