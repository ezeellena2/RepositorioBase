namespace CleanArchitecture.Web.AcceptanceTests;

internal static class AcceptanceTestCredentials
{
    private const string EmailEnvironmentVariable = "CLEANARCHITECTURE_ACCEPTANCE_TEST_EMAIL";
    private const string PasswordEnvironmentVariable = "CLEANARCHITECTURE_ACCEPTANCE_TEST_PASSWORD";

    public static async Task SignInAsync(LoginPage loginPage)
    {
        await loginPage.SetEmail(GetRequired(EmailEnvironmentVariable));
        await loginPage.SetPassword(GetRequired(PasswordEnvironmentVariable));
        await loginPage.ClickLogin();
    }

    private static string GetRequired(string environmentVariable)
    {
        var value = Environment.GetEnvironmentVariable(environmentVariable);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Set {EmailEnvironmentVariable} and {PasswordEnvironmentVariable} for acceptance tests. No seeded default account is available.");
        }

        return value;
    }
}
