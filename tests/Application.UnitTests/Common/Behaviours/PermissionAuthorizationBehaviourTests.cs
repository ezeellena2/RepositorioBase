using CleanArchitecture.Application.Common.Behaviours;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;
using MediatR;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.Common.Behaviours;

public sealed class PermissionAuthorizationBehaviourTests
{
    [Test]
    public async Task Unmarked_request_is_rejected_before_the_handler()
    {
        var behaviour = CreateBehaviour<UnmarkedRequest>(null, null, false, out _);
        var handled = false;

        await Should.ThrowAsync<AuthorizationMetadataMissingException>(() =>
            behaviour.Handle(new UnmarkedRequest(), _ =>
            {
                handled = true;
                return Task.FromResult(Unit.Value);
            }, CancellationToken.None));

        handled.ShouldBeFalse();
    }

    [Test]
    public async Task Public_request_bypasses_identity_and_permission_checks()
    {
        var behaviour = CreateBehaviour<PublicRequest>(null, null, false, out var evaluator);

        var result = await behaviour.Handle(new PublicRequest(), _ => Task.FromResult(Unit.Value), CancellationToken.None);

        result.ShouldBe(Unit.Value);
        evaluator.VerifyNoOtherCalls();
    }

    [Test]
    public async Task Request_with_public_and_authorize_markers_is_rejected_before_the_handler()
    {
        var behaviour = CreateBehaviour<DualMarkedRequest>(Guid.NewGuid(), null, true, out _);
        var handled = false;

        await Should.ThrowAsync<AuthorizationMetadataMissingException>(() =>
            behaviour.Handle(new DualMarkedRequest(), _ =>
            {
                handled = true;
                return Task.FromResult(Unit.Value);
            }, CancellationToken.None));

        handled.ShouldBeFalse();
    }

    [Test]
    public async Task Non_tenant_authorized_request_denies_a_missing_explicit_permission()
    {
        var userId = Guid.NewGuid();
        var behaviour = CreateBehaviour<NonTenantRequest>(userId, null, false, out var evaluator, out var audit);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            behaviour.Handle(new NonTenantRequest(), _ => Task.FromResult(Unit.Value), CancellationToken.None));

        evaluator.Verify(x => x.HasPermissionAsync(userId, "identity.context.read", It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.WriteDeniedAsync(It.Is<SecurityDenialAudit>(entry =>
            entry.TenantId == null && entry.PermissionCode == "identity.context.read" && entry.Outcome == "permission_denied"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Authorized_request_requires_a_valid_guid_identity()
    {
        var behaviour = CreateBehaviour<TenantRequest>(null, TenantId.New(), false, out _);

        await Should.ThrowAsync<UnauthorizedAccessException>(() =>
            behaviour.Handle(new TenantRequest(), _ => Task.FromResult(Unit.Value), CancellationToken.None));
    }

    [Test]
    public async Task Tenant_scoped_request_denies_a_missing_permission_and_records_the_denial()
    {
        var userId = Guid.NewGuid();
        var tenantId = TenantId.New();
        var behaviour = CreateBehaviour<TenantRequest>(userId, tenantId, false, out var evaluator, out var audit);

        await Should.ThrowAsync<ForbiddenAccessException>(() =>
            behaviour.Handle(new TenantRequest(), _ => Task.FromResult(Unit.Value), CancellationToken.None));

        evaluator.Verify(x => x.HasPermissionAsync(userId, tenantId, "tenant.read", It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(x => x.WriteDeniedAsync(It.Is<SecurityDenialAudit>(entry => entry.TenantId == tenantId && entry.PermissionCode == "tenant.read" && entry.Outcome == "permission_denied"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Tenant_scoped_request_uses_the_single_validated_tenant_context()
    {
        var userId = Guid.NewGuid();
        var tenantId = TenantId.New();
        var behaviour = CreateBehaviour<TenantRequest>(userId, tenantId, true, out var evaluator);

        await behaviour.Handle(new TenantRequest(), _ => Task.FromResult(Unit.Value), CancellationToken.None);

        evaluator.Verify(x => x.HasPermissionAsync(userId, tenantId, "tenant.read", It.IsAny<CancellationToken>()), Times.Once);
    }

    private static AuthorizationBehaviour<TRequest, Unit> CreateBehaviour<TRequest>(Guid? userId, TenantId? tenantId, bool granted, out Mock<IPermissionEvaluator> evaluator)
        where TRequest : notnull
    {
        return CreateBehaviour<TRequest>(userId, tenantId, granted, out evaluator, out _);
    }

    private static AuthorizationBehaviour<TRequest, Unit> CreateBehaviour<TRequest>(Guid? userId, TenantId? tenantId, bool granted, out Mock<IPermissionEvaluator> evaluator, out Mock<ISecurityDenialAuditWriter> audit)
        where TRequest : notnull
    {
        var user = new Mock<IUser>();
        user.SetupGet(candidate => candidate.Id).Returns(userId);
        evaluator = new Mock<IPermissionEvaluator>();
        evaluator.Setup(candidate => candidate.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(granted);
        evaluator.Setup(candidate => candidate.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<TenantId>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(granted);
        audit = new Mock<ISecurityDenialAuditWriter>();
        audit.Setup(candidate => candidate.WriteDeniedAsync(It.IsAny<SecurityDenialAudit>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        return new AuthorizationBehaviour<TRequest, Unit>(user.Object, new TestCurrentTenant(tenantId), evaluator.Object, audit.Object);
    }

    private sealed class TestCurrentTenant(TenantId? tenantId) : ICurrentTenant
    {
        public TenantId? TenantId { get; } = tenantId;
    }

    private sealed record UnmarkedRequest : IRequest<Unit>;

    private sealed record PublicRequest : IRequest<Unit>, IPublicRequest;

    [Authorize("tenant.read", true)]
    private sealed record TenantRequest : IRequest<Unit>;

    [Authorize("identity.context.read", false)]
    private sealed record NonTenantRequest : IRequest<Unit>;

    [Authorize("identity.context.read", false)]
    private sealed record DualMarkedRequest : IRequest<Unit>, IPublicRequest;
}
