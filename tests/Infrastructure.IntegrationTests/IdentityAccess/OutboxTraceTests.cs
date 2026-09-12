using System.Diagnostics;
using CleanArchitecture.Domain.IdentityAccess.Outbox;
using CleanArchitecture.Infrastructure.Data;
using CleanArchitecture.Infrastructure.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Infrastructure.IntegrationTests.IdentityAccess;

[NonParallelizable]
public sealed class OutboxTraceTests
{
    private const string KnownTraceId = "0123456789abcdef0123456789abcdef";
    private readonly List<Guid> _messageIds = [];

    [TearDown]
    public async Task Remove_test_messages()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await context.OutboxMessages.Where(message => _messageIds.Contains(message.Id)).ExecuteDeleteAsync();
        _messageIds.Clear();
    }

    [Test]
    public async Task Added_messages_capture_only_the_ambient_w3c_trace_and_the_mapping_is_nullable()
    {
        using var scope = TestServices.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var traced = OutboxMessage.Create("trace.test", "{}", DateTimeOffset.UtcNow);
        _messageIds.Add(traced.Id);

        using (var activity = new Activity("enqueue")
            .SetIdFormat(ActivityIdFormat.W3C)
            .SetParentId($"00-{KnownTraceId}-0123456789abcdef-01")
            .Start())
        {
            context.OutboxMessages.Add(traced);
            await context.SaveChangesAsync();
        }

        var previous = Activity.Current;
        Activity.Current = null;
        try
        {
            var untraced = OutboxMessage.Create("trace.none", "{}", DateTimeOffset.UtcNow);
            _messageIds.Add(untraced.Id);
            context.OutboxMessages.Add(untraced);
            await context.SaveChangesAsync();
        }
        finally
        {
            Activity.Current = previous;
        }

        context.ChangeTracker.Clear();
        (await context.OutboxMessages.SingleAsync(message => message.Id == traced.Id)).TraceId.ShouldBe(KnownTraceId);
        (await context.OutboxMessages.SingleAsync(message => message.Id != traced.Id && _messageIds.Contains(message.Id))).TraceId.ShouldBeNull();

        var property = context.Model.FindEntityType(typeof(OutboxMessage))!
            .FindProperty(nameof(OutboxMessage.TraceId))!;
        property.IsNullable.ShouldBeTrue();
        property.GetMaxLength().ShouldBe(32);
    }
}
