using CleanArchitecture.Application.Common.Security;
using CleanArchitecture.Application.Common.Models;

namespace CleanArchitecture.Application.IdentityAccess.Organizations.ConfirmEmail;

public sealed record ConfirmEmailCommand(string Token) : IRequest<Result>, IPublicRequest, ISensitiveRequest;
