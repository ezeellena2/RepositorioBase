using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

public sealed class ConcurrencyTests
{
    [Test]
    public async Task TodoItem_xmin_rejects_a_stale_update()
    {
        using var scope = TestServices.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var connectionString = source.Database.GetConnectionString();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connectionString).Options;

        var list = new TodoList { Title = "concurrency list" };
        source.TodoLists.Add(list);
        await source.SaveChangesAsync();

        var todo = new TodoItem { ListId = list.Id, Title = "original" };
        source.TodoItems.Add(todo);
        await source.SaveChangesAsync();

        await using var firstContext = new ApplicationDbContext(options);
        await using var staleContext = new ApplicationDbContext(options);
        var first = await firstContext.TodoItems.SingleAsync(item => item.Id == todo.Id);
        var stale = await staleContext.TodoItems.SingleAsync(item => item.Id == todo.Id);

        first.Title = "first update";
        await firstContext.SaveChangesAsync();

        stale.Title = "stale update";
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => staleContext.SaveChangesAsync());

        await using var verificationContext = new ApplicationDbContext(options);
        (await verificationContext.TodoItems.SingleAsync(item => item.Id == todo.Id)).Title.ShouldBe("first update");
    }
}
