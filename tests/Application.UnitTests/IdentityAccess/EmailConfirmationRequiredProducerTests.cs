using CleanArchitecture.Application.Common.Interfaces;
using CleanArchitecture.Application.Common.Models;
using CleanArchitecture.Application.IdentityAccess.Credentials;
using CleanArchitecture.Application.IdentityAccess.Credentials.Reauthenticate;
using CleanArchitecture.Application.IdentityAccess.ExternalLogins;
using CleanArchitecture.Application.IdentityAccess.Organizations;
using CleanArchitecture.Application.IdentityAccess.Sessions;
using CleanArchitecture.Domain.IdentityAccess.Identities;
using CleanArchitecture.Domain.IdentityAccess.Sessions;
using Moq;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Application.UnitTests.IdentityAccess;

public sealed class EmailConfirmationRequiredProducerTests
{
    private static readonly Guid IdentityId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly UserSessionId SessionId = UserSessionId.From(
        Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Test]
    public async Task Reauthentication_refuses_a_non_active_identity_before_validating_or_issuing_a_proof()
    {
        var transaction = new Mock<IApplicationTransaction>(MockBehavior.Strict);
        var identities = PendingIdentity();
        var proofs = new Mock<IRecentIdentityProofStore>(MockBehavior.Strict);
        var handler = new ReauthenticateCommandHandler(
            transaction.Object,
            identities.Object,
            proofs.Object,
            LiveSession().Object);

        var result = await handler.Handle(
            new ReauthenticateCommand(ProofActions.PasswordChange, "password-not-inspected"),
            CancellationToken.None);

        AssertEmailConfirmationRequired(result.Error);
        identities.Verify(service => service.FindByIdAsync(IdentityId, CancellationToken.None), Times.Once);
        identities.VerifyNoOtherCalls();
        transaction.VerifyNoOtherCalls();
        proofs.VerifyNoOtherCalls();
    }

    [Test]
    public async Task External_link_start_refuses_a_non_active_identity_before_reading_links_or_spending_a_proof()
    {
        var transaction = new Mock<IApplicationTransaction>(MockBehavior.Strict);
        var context = new Mock<IApplicationDbContext>(MockBehavior.Strict);
        var handoffs = new Mock<IExternalHandoffContext>(MockBehavior.Strict);
        handoffs.Setup(candidate => candidate.IsConfigured("Google")).Returns(true);
        var identities = PendingIdentity();
        var proofs = new Mock<IRecentIdentityProofStore>(MockBehavior.Strict);
        var external = new Mock<IExternalIdentityService>(MockBehavior.Strict);
        var handler = new StartExternalLinkCommandHandler(
            transaction.Object,
            context.Object,
            handoffs.Object,
            identities.Object,
            proofs.Object,
            external.Object,
            LiveSession().Object,
            TimeProvider.System);

        var result = await handler.Handle(new StartExternalLinkCommand("Google", true), CancellationToken.None);

        AssertEmailConfirmationRequired(result.Error);
        handoffs.Verify(candidate => candidate.IsConfigured("Google"), Times.Once);
        handoffs.VerifyNoOtherCalls();
        identities.Verify(service => service.FindByIdAsync(IdentityId, CancellationToken.None), Times.Once);
        identities.VerifyNoOtherCalls();
        transaction.VerifyNoOtherCalls();
        context.VerifyNoOtherCalls();
        proofs.VerifyNoOtherCalls();
        external.VerifyNoOtherCalls();
    }

    private static Mock<IIdentityAccountService> PendingIdentity()
    {
        var identities = new Mock<IIdentityAccountService>(MockBehavior.Strict);
        identities.Setup(service => service.FindByIdAsync(IdentityId, CancellationToken.None))
            .ReturnsAsync(new IdentityAccount(IdentityId, "person@example.test", IdentityAccountStatus.PendingConfirmation));
        return identities;
    }

    private static Mock<ICurrentSession> LiveSession()
    {
        var session = new Mock<ICurrentSession>(MockBehavior.Strict);
        session.SetupGet(candidate => candidate.IsInvalid).Returns(false);
        session.SetupGet(candidate => candidate.IdentityId).Returns(IdentityId);
        session.SetupGet(candidate => candidate.SessionId).Returns(SessionId);
        return session;
    }

    private static void AssertEmailConfirmationRequired(ApplicationError? error)
    {
        error.ShouldNotBeNull();
        error.Code.ShouldBe("email_confirmation_required");
        error.Category.ShouldBe(ApplicationErrorCategory.Authorization);
    }
}
