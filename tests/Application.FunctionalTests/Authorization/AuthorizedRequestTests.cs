using CleanArchitecture.Application.TodoLists.Commands.CreateTodoList;

namespace CleanArchitecture.Application.FunctionalTests.Authorization;

public class AuthorizedRequestTests : TestBase
{
    [Test]
    public async Task ShouldDenyAnonymousAuthorizedCommand()
    {
        await Should.ThrowAsync<UnauthorizedAccessException>(
            () => TestApp.SendAsync(new CreateTodoListCommand { Title = "Tasks" }));
    }
}
