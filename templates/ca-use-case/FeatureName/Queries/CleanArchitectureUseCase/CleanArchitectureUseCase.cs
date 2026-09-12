using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Validation;

namespace CleanArchitecture.Application.FeatureName.Queries.CleanArchitectureUseCase;

//#if (hasReturnType)
public record CleanArchitectureUseCaseQuery : IRequest<TReturnType>
//#else
public record CleanArchitectureUseCaseQuery : IRequest
//#endif
{
    public string SomeInput { get; init; } = string.Empty;
}

public class CleanArchitectureUseCaseQueryValidator : AbstractValidator<CleanArchitectureUseCaseQuery>
{
    public CleanArchitectureUseCaseQueryValidator()
    {
        RuleFor(query => query.SomeInput)
            .NotEmpty().WithMessage("Some input is required.")
            .WithErrorCode(ValidationErrorCodes.Required);
    }
}

//#if (hasReturnType)
public class CleanArchitectureUseCaseQueryHandler : IRequestHandler<CleanArchitectureUseCaseQuery, TReturnType>
//#else
public class CleanArchitectureUseCaseQueryHandler : IRequestHandler<CleanArchitectureUseCaseQuery>
//#endif
{
    private readonly IApplicationDbContext _context;

    public CleanArchitectureUseCaseQueryHandler(IApplicationDbContext context)
    {
        _context = context;
    }

//#if (hasReturnType)
    public async Task<TReturnType> Handle(CleanArchitectureUseCaseQuery request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
//#else
    public async Task Handle(CleanArchitectureUseCaseQuery request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
//#endif
}
