using CleanArchitecture.Application.Common.Models;
using Microsoft.AspNetCore.Identity;

using CleanArchitecture.Application.IdentityAccess.Common;

namespace CleanArchitecture.Infrastructure.Identity;

public static class IdentityResultExtensions
{
    public static Result ToApplicationResult(this IdentityResult result, ApplicationError failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return result.Succeeded
            ? Result.Success()
            : Result.Failure(failure);
    }
}
