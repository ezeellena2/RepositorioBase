using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Common;
using CleanArchitecture.Domain.Enums;

namespace CleanArchitecture.Application.TodoItems.Commands.UpdateTodoItemDetail;

[Authorize("todos.write", false)]
public record UpdateTodoItemDetailCommand : IRequest<Result>
{
    public int Id { get; init; }

    public int ListId { get; init; }

    public PriorityLevel Priority { get; init; }

    public string? Note { get; init; }
}

public class UpdateTodoItemDetailCommandHandler(IApplicationDbContext context) : IRequestHandler<UpdateTodoItemDetailCommand, Result>
{
    public async Task<Result> Handle(UpdateTodoItemDetailCommand request, CancellationToken cancellationToken)
    {
        var entity = await context.TodoItems.FindAsync([request.Id], cancellationToken);
        Guard.Against.NotFound(request.Id, entity);

        entity.ListId = request.ListId;
        entity.Priority = request.Priority;
        entity.Note = request.Note;

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(IdentityAccessErrors.TodoItemConcurrencyConflict());
        }
    }
}
