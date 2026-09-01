using CleanArchitecture.Application.TodoItems.Commands.CreateTodoItem;
using CleanArchitecture.Application.TodoItems.Commands.DeleteTodoItem;
using CleanArchitecture.Application.TodoItems.Commands.UpdateTodoItem;
using CleanArchitecture.Application.TodoItems.Commands.UpdateTodoItemDetail;
using CleanArchitecture.Application.Common.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace CleanArchitecture.Web.Endpoints;

public class TodoItems : IEndpointGroup
{
    public static void Map(RouteGroupBuilder groupBuilder)
    {
        groupBuilder.RequireAuthorization();

        groupBuilder.MapPost(CreateTodoItem)
            .WithCreatedLocation<int>()
            .WithApiProblemDetails(
                ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.InternalServerError);
        groupBuilder.MapPut(UpdateTodoItem, "{id}")
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.RouteBodyIdMismatch, ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError);
        groupBuilder.MapPatch(UpdateTodoItemDetail, "UpdateDetail/{id}")
            .Produces(StatusCodes.Status204NoContent)
            .WithApiProblemDetails(
                ApiProblemMetadata.ValidationFailed, ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.RouteBodyIdMismatch, ApiProblemMetadata.AuthenticationRequired,
                ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.TodoItemConcurrencyConflict,
                ApiProblemMetadata.InternalServerError);
        groupBuilder.MapDelete(DeleteTodoItem, "{id}").WithApiProblemDetails(
            ApiProblemMetadata.InvalidRequest, ApiProblemMetadata.AuthenticationRequired, ApiProblemMetadata.PermissionDenied, ApiProblemMetadata.NotFound, ApiProblemMetadata.InternalServerError);
    }

    [EndpointSummary("Create a new Todo Item")]
    [EndpointDescription("Creates a new todo item using the provided details and returns the ID of the created item.")]
    public static async Task<Created<int>> CreateTodoItem(ISender sender, CreateTodoItemCommand command)
    {
        var id = await sender.Send(command);

        return TypedResults.Created($"/{nameof(TodoItems)}/{id}", id);
    }

    [EndpointSummary("Update a Todo Item")]
    [EndpointDescription("Updates the specified todo item. The ID in the URL must match the ID in the payload.")]
    public static async Task<IResult> UpdateTodoItem(ISender sender, ApiProblemDetailsMapper problemDetailsMapper, int id, UpdateTodoItemCommand command)
    {
        if (id != command.Id)
            return problemDetailsMapper.ToHttpResult(new ApplicationError(
                "route_body_id_mismatch",
                ApplicationErrorCategory.Validation,
                validationErrors: new Dictionary<string, string[]> { ["id"] = ["The route identifier must match the payload identifier."] }));

        await sender.Send(command);

        return TypedResults.NoContent();
    }

    [EndpointSummary("Update Todo Item Details")]
    [EndpointDescription("Updates the detail fields of a specific todo item. The ID in the URL must match the ID in the payload.")]
    public static async Task<IResult> UpdateTodoItemDetail(ISender sender, ApiProblemDetailsMapper problemDetailsMapper, HttpContext httpContext, int id, UpdateTodoItemDetailCommand command)
    {
        if (id != command.Id)
        {
            return problemDetailsMapper.ToHttpResult(new ApplicationError(
                "route_body_id_mismatch",
                ApplicationErrorCategory.Validation,
                validationErrors: new Dictionary<string, string[]> { ["id"] = ["The route identifier must match the payload identifier."] }));
        }

        var result = await sender.Send(command);
        return result.ToHttpResult(httpContext, problemDetailsMapper);
    }

    [EndpointSummary("Delete a Todo Item")]
    [EndpointDescription("Deletes the todo item with the specified ID.")]
    public static async Task<NoContent> DeleteTodoItem(ISender sender, int id)
    {
        await sender.Send(new DeleteTodoItemCommand(id));

        return TypedResults.NoContent();
    }
}
