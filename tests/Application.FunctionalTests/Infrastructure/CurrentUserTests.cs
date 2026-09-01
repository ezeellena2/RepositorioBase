using System.Security.Claims;
using CleanArchitecture.Web.Services;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;

namespace CleanArchitecture.Application.FunctionalTests.Infrastructure;

public class CurrentUserTests
{
    [Test]
    public void IdReturnsGuidNameIdentifier()
    {
        var userId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())]));
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.That((object?)currentUser.Id, Is.EqualTo((object)userId));
    }

    [TestCase(null)]
    [TestCase("not-a-guid")]
    public void IdReturnsNullWhenNameIdentifierIsMissingOrInvalid(string? identifier)
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(identifier is null ? [] : [new Claim(ClaimTypes.NameIdentifier, identifier)]));
        var currentUser = new CurrentUser(new HttpContextAccessor { HttpContext = context });

        Assert.That(currentUser.Id, Is.Null);
    }
}
