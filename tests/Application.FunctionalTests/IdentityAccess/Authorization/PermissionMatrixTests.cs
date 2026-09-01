using System.Net;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.TodoLists.Commands.CreateTodoList;
using CleanArchitecture.Domain.Entities;
using CleanArchitecture.Domain.IdentityAccess.Auditing;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using CleanArchitecture.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchitecture.Application.FunctionalTests.IdentityAccess.Authorization;

public sealed class PermissionMatrixTests : TestBase
{
    [Test]
    public async Task Anonymous_authorized_request_is_rejected_before_its_handler()
    {
        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            TestApp.SendAsync(new CreateTodoListCommand { Title = "must not be created" }));
    }

    [Test]
    public async Task Anonymous_endpoint_response_is_a_problem_details_401()
    {
        var response = await FunctionalTestSetup.HttpClient.GetAsync("/api/TodoLists");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("authentication_required");
        body.ShouldContain("traceId");

        var audit = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.EventType == "authorization.denied");
        audit.TenantId.ShouldBeNull();
        audit.ActorId.ShouldBeNull();
        audit.SessionId.ShouldBeNull();
        audit.OccurredAt.ShouldNotBe(default);
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "endpoint.authorization", ["outcome"] = "identity_missing_or_invalid" });
    }

    [Test]
    public async Task Authenticated_request_without_an_application_permission_is_denied_and_audited_without_tenant_authority()
    {
        var actorId = await TestApp.RunAsDefaultUserAsync();
        TestApp.SetApplicationPermissionGranted(false);

        await Should.ThrowAsync<ForbiddenAccessException>(() => TestApp.SendAsync(new CreateTodoListCommand { Title = "must not be created" }));

        var audit = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.EventType == "authorization.denied");
        audit.TenantId.ShouldBeNull();
        audit.ActorId.ShouldBe(actorId);
        audit.SessionId.ShouldBeNull("the application behavior has no session adapter before IA-007");
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "todos.write", ["outcome"] = "permission_denied" });
    }

    [Test]
    public async Task Middleware_forbid_persists_the_optional_actor_and_session_claims()
    {
        var actorId = await TestApp.RunAsDefaultUserAsync();
        var sessionId = TestApp.GetSessionId();
        TestApp.SetHttpAuthorizationGranted(false);

        var response = await FunctionalTestSetup.HttpClient.GetAsync("/api/TodoLists");

        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        var audit = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.EventType == "authorization.denied");
        audit.TenantId.ShouldBeNull();
        audit.ActorId.ShouldBe(actorId);
        audit.SessionId.ShouldBe(sessionId);
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "endpoint.authorization", ["outcome"] = "permission_denied" });
    }

    [Test]
    public async Task Denial_audit_uses_an_independent_context_and_survives_a_business_rollback()
    {
        var tenant = Tenant.CreateOrganization(TenantSlug.From($"denial-audit-{Guid.NewGuid():N}"));
        await TestApp.AddAsync(tenant);

        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var writer = scope.ServiceProvider.GetRequiredService<ISecurityDenialAuditWriter>();
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            context.TodoLists.Add(new TodoList { Title = "rolled-back mutation" });
            await context.SaveChangesAsync();
            await writer.WriteDeniedAsync(new SecurityDenialAudit("denial-correlation", null, tenant.Id, "tenant.read", "permission_denied"));
            await transaction.RollbackAsync();
        });

        (await TestApp.ListAsync<TodoList>()).ShouldBeEmpty();
        var events = await TestApp.ListAsync<AuditEvent>();
        var audit = events.Single(item => item.EventType == "authorization.denied");
        audit.CorrelationId.ShouldBe("denial-correlation");
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "tenant.read", ["outcome"] = "permission_denied" });
    }

    [Test]
    public async Task Canceled_caller_token_cannot_erase_an_isolated_denial_audit()
    {
        using var scope = FunctionalTestSetup.ScopeFactory.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<ISecurityDenialAuditWriter>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await writer.WriteDeniedAsync(new SecurityDenialAudit("server-owned-cancellation", null, null, "todos.read", "permission_denied"), cancellation.Token);

        var audit = (await TestApp.ListAsync<AuditEvent>()).Single(item => item.CorrelationId == "server-owned-cancellation");
        audit.Metadata.ShouldBe(new Dictionary<string, string> { ["code"] = "todos.read", ["outcome"] = "permission_denied" });
    }
}
