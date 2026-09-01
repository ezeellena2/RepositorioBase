using CleanArchitecture.Application.TodoLists.Commands.CreateTodoList;
using CleanArchitecture.Application.TodoLists.Commands.DeleteTodoList;
using CleanArchitecture.Application.TodoLists.Commands.UpdateTodoList;
using CleanArchitecture.Application.TodoLists.Queries.GetTodos;
using CleanArchitecture.Application.Common.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Web.Endpoints;

public class TodoLists : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        groupBuilder.MapGet(GetTodoLists).WithApiProblemDetails(
            ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);
        groupBuilder.MapPost(CreateTodoList)
            .WithCreatedLocation<int>()
            .WithApiProblemDetails(
                ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);
        groupBuilder.MapPut(UpdateTodoList, "{id}")
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.RouteBodyIdMismatch, ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError);
        groupBuilder.MapDelete(DeleteTodoList, "{id}").WithApiProblemDetails(
            ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError);
    }

    [EndpointSummary("Get all Todo Lists")]
    [EndpointDescription("Retrieves all todo lists along with their items.")]
    public static async Task<Ok<TodosVm>> GetTodoLists(ISender sender)
    {
        var vm = await sender.Send(new GetTodosQuery());

        return TypedResults.Ok(vm);
    }

    [EndpointSummary("Create a new Todo List")]
    [EndpointDescription("Creates a new todo list using the provided details and returns the ID of the created list.")]
    public static async Task<Created<int>> CreateTodoList(ISender sender, CreateTodoListCommand command)
    {
        var id = await sender.Send(command);

        return TypedResults.Created($"/{nameof(TodoLists)}/{id}", id);
    }

    [EndpointSummary("Update a Todo List")]
    [EndpointDescription("Updates the specified todo list. The ID in the URL must match the ID in the payload.")]
    public static async Task<IResult> UpdateTodoList(ISender sender, ApiProblemDetailsMapper problemDetailsMapper, int id, UpdateTodoListCommand command)
    {
        if (id != command.Id)
        {
            return problemDetailsMapper.ToHttpResult(new ApplicationError(
                "route_body_id_mismatch",
                ApplicationErrorCategory.Validation,
                validationErrors: new Dictionary<string, string[]> { ["id"] = ["The route identifier must match the payload identifier."] }));
        }

        await sender.Send(command);

        return TypedResults.NoContent();
    }

    [EndpointSummary("Delete a Todo List")]
    [EndpointDescription("Deletes the todo list with the specified ID.")]
    public static async Task<NoContent> DeleteTodoList(ISender sender, int id)
    {
        await sender.Send(new DeleteTodoListCommand(id));

        return TypedResults.NoContent();
    }
}
