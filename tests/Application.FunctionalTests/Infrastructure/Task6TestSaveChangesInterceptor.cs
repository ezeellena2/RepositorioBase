using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

/// <summary>Creates a real competing PostgreSQL update only when a Task 6 test requests it.</summary>
public sealed class Task6TestSaveChangesInterceptor : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (TestApp.ConsumeForcedUnexpectedFailure())
        {
            throw new InvalidOperationException("provider password=must-not-reach-the-client");
        }

        var context = eventData.Context;
        var staleItem = context?.ChangeTracker.Entries<TodoItem>()
            .Select(entry => entry.Entity)
            .FirstOrDefault(entry => entry.Id > 0 && entry.StateIsModified(context));

        if (staleItem is not null && TestApp.ConsumeForcedTodoItemConcurrencyConflict())
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(context!.Database.GetConnectionString())
                .Options;
            await using var competingContext = new ApplicationDbContext(options);
            var competingItem = await competingContext.TodoItems.SingleAsync(item => item.Id == staleItem.Id, cancellationToken);
            competingItem.Note = "concurrent database update";
            await competingContext.SaveChangesAsync(cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}

internal static class Task6TodoItemEntryExtensions
{
    internal static bool StateIsModified(this TodoItem entity, DbContext context) =>
        context.Entry(entity).State == EntityState.Modified;
}
