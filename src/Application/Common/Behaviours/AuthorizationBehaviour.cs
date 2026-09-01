using System.Reflection;
using CleanArchitecture.Application.Common.Exceptions;
using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.IdentityAccess.Authorization;
using CleanArchitecture.Domain.IdentityAccess.Tenants;

namespace CleanArchitecture.Application.Common.Behaviours;

/// <summary>Enforces the Application request contract before a handler runs.</summary>
public sealed class AuthorizationBehaviour<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IUser _user;
    private readonly ICurrentTenant _currentTenant;
    private readonly IPermissionEvaluator _permissionEvaluator;
    private readonly ISecurityDenialAuditWriter _denialAuditWriter;

    public AuthorizationBehaviour(IUser user, ICurrentTenant currentTenant, IPermissionEvaluator permissionEvaluator, ISecurityDenialAuditWriter denialAuditWriter)
    {
        _user = user;
        _currentTenant = currentTenant;
        _permissionEvaluator = permissionEvaluator;
        _denialAuditWriter = denialAuditWriter;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var requestType = request.GetType();
        var authorizeAttributes = requestType.GetCustomAttributes<AuthorizeAttribute>(false).ToArray();
        var isPublicRequest = request is IPublicRequest;

        if (isPublicRequest && authorizeAttributes.Length > 0)
        {
            throw new AuthorizationMetadataMissingException(requestType, "a request cannot be both public and authorized");
        }

        if (!isPublicRequest && authorizeAttributes.Length != 1)
        {
            throw new AuthorizationMetadataMissingException(requestType, "exactly one authorization declaration is required");
        }

        if (isPublicRequest)
        {
            return await next();
        }

        var authorization = authorizeAttributes.Single();
        if (string.IsNullOrWhiteSpace(authorization.Permission))
        {
            throw new AuthorizationMetadataMissingException(requestType, "a nonblank permission is required");
        }

        if (!_user.Id.HasValue || _user.Id.Value == Guid.Empty)
        {
            await WriteDenialAsync(null, authorization.Permission, "identity_missing_or_invalid", cancellationToken);
            throw new UnauthorizedAccessException();
        }

        if (!authorization.RequiresTenant)
        {
            if (!await _permissionEvaluator.HasPermissionAsync(_user.Id.Value, authorization.Permission, cancellationToken))
            {
                await WriteDenialAsync(null, authorization.Permission, "permission_denied", cancellationToken);
                throw new ForbiddenAccessException();
            }

            return await next();
        }

        if (authorization.RequiresTenant)
        {
            var tenantId = _currentTenant.TenantId;
            if (tenantId is null || tenantId.Value.IsEmpty)
            {
                await WriteDenialAsync(null, authorization.Permission, "tenant_context_missing", cancellationToken);
                throw new ForbiddenAccessException();
            }

            if (!await _permissionEvaluator.HasPermissionAsync(_user.Id.Value, tenantId.Value, authorization.Permission, cancellationToken))
            {
                await WriteDenialAsync(tenantId, authorization.Permission, "permission_denied", cancellationToken);
                throw new ForbiddenAccessException();
            }
        }

        return await next();
    }

    private Task WriteDenialAsync(TenantId? tenantId, string permissionCode, string outcome, CancellationToken cancellationToken) =>
        _denialAuditWriter.WriteDeniedAsync(
            new SecurityDenialAudit(System.Diagnostics.Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString("N"), _user.Id, tenantId, permissionCode, outcome),
            cancellationToken);
}
