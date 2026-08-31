using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Data;

public sealed class DatabaseInitialisationTests
{
    [Test]
    public async Task InitialiseAsync_applies_all_available_migrations()
    {
        using var scope = TestServices.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await initialiser.InitialiseAsync();

        (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
    }

    [Test]
    public async Task InitialiseAsync_preserves_existing_todos_and_does_not_seed_identity_data()
    {
        using var scope = TestServices.CreateScope();
        var initialiser = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitialiser>();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await initialiser.InitialiseAsync();
        context.ChangeTracker.Clear();

        var sentinel = new TodoList { Title = "restart sentinel" };
        context.TodoLists.Add(sentinel);
        await context.SaveChangesAsync();

        await initialiser.InitialiseAsync();
        context.ChangeTracker.Clear();

        (await context.TodoLists.AnyAsync(todoList => todoList.Id == sentinel.Id)).ShouldBeTrue();
        (await userManager.Users.AnyAsync()).ShouldBeFalse();
    }
}
