namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

public abstract class TestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await TestApp.ResetState();
    }
}

public abstract class AuthorizedTestBase : TestBase
{
    [SetUp]
    public async Task SetUpAuthorizedUser()
    {
        await TestApp.RunAsDefaultUserAsync();
    }
}
