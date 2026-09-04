using System.Reflection;
using CleanArchitecture.Domain.IdentityAccess.Outbox;

namespace CleanArchitecture.Infrastructure.IntegrationTests.Architecture;

/// <summary>
/// The shape Task 11 needs before any of its behaviour can be written. Every assertion here is a runtime lookup
/// rather than a direct reference, so a missing type fails the test instead of breaking the build: a compile
/// error is not a RED anyone can run.
/// <para>
/// Task 7 gave <see cref="OutboxMessage"/> its retry columns and no way to write them — <c>AttemptCount</c>,
/// <c>NextAttemptAt</c> and <c>FailureCode</c> exist with no mutator, and there is no lease and no terminal
/// state at all. A dispatcher cannot claim, retry or give up against that surface, so the aggregate is part of
/// this shape and not an afterthought of it.
/// </para>
/// </summary>
public sealed class OutboxInfrastructureShapeTests
{
    private static readonly Assembly ApplicationAssembly = typeof(CleanArchitecture.Application.Common.Interfaces.IApplicationDbContext).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(CleanArchitecture.Infrastructure.Data.ApplicationDbContext).Assembly;

    [TestCase("CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader")]
    [TestCase("CleanArchitecture.Application.Common.Interfaces.IIdentityEmailSender")]
    public void RequiredApplicationPortExists(string fullyQualifiedName)
    {
        ApplicationAssembly.GetType(fullyQualifiedName).ShouldNotBeNull();
    }

    [TestCase("CleanArchitecture.Infrastructure.Outbox.OutboxSecretReader")]
    [TestCase("CleanArchitecture.Infrastructure.Outbox.OutboxDispatcher")]
    [TestCase("CleanArchitecture.Infrastructure.Outbox.InvitationEmailDeliveryHandler")]
    [TestCase("CleanArchitecture.Infrastructure.Outbox.EmailConfirmationDeliveryHandler")]
    [TestCase("CleanArchitecture.Infrastructure.Email.IdentityEmailAdapter")]
    public void RequiredInfrastructureTypeExists(string fullyQualifiedName)
    {
        InfrastructureAssembly.GetType(fullyQualifiedName).ShouldNotBeNull();
    }

    /// <summary>
    /// The token leaves the envelope only in memory, so the reader hands back a decrypted value and nothing
    /// persists it. Its one member is named for that: it reads, it does not expose a store.
    /// </summary>
    [Test]
    public void The_secret_reader_decrypts_one_envelope_at_a_time()
    {
        var reader = ApplicationAssembly.GetType("CleanArchitecture.Application.Common.Interfaces.IOutboxSecretReader");

        reader.ShouldNotBeNull();
        reader!.GetMethod("ReadAsync").ShouldNotBeNull("the dispatcher decrypts one leased envelope, never a page of them");
    }

    /// <summary>
    /// The adapter is asked to send with an idempotency key and answers with evidence, because a retry has to be
    /// able to ask whether the previous attempt actually landed (IA-REQ-018).
    /// </summary>
    [Test]
    public void The_email_sender_takes_an_idempotency_key_and_returns_delivery_evidence()
    {
        var sender = ApplicationAssembly.GetType("CleanArchitecture.Application.Common.Interfaces.IIdentityEmailSender");

        sender.ShouldNotBeNull();
        var send = sender!.GetMethod("SendAsync").ShouldNotBeNull();
        send.GetParameters().Select(parameter => parameter.Name).ShouldContain("idempotencyKey");
        send.ReturnType.ShouldNotBe(typeof(Task), "a send that answers nothing cannot be reconciled before a retry");
    }

    /// <summary>
    /// A dispatcher that read the system clock could not be tested for backoff at all, so time is injected the
    /// same way every other timed behaviour in this project injects it.
    /// </summary>
    [Test]
    public void The_dispatcher_takes_its_clock_as_a_dependency()
    {
        var dispatcher = InfrastructureAssembly.GetType("CleanArchitecture.Infrastructure.Outbox.OutboxDispatcher");

        dispatcher.ShouldNotBeNull();
        dispatcher!.GetConstructors().SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ShouldContain(typeof(TimeProvider));
    }

    /// <summary>
    /// The hosted service only loops; the unit that does one pass is separately invocable. Without that seam a
    /// test cannot drive exactly one iteration, and the functional suite would race a background poller.
    /// </summary>
    [Test]
    public void One_dispatch_pass_is_invocable_without_a_hosted_service()
    {
        var dispatcher = InfrastructureAssembly.GetType("CleanArchitecture.Infrastructure.Outbox.OutboxDispatcher");

        dispatcher.ShouldNotBeNull();
        dispatcher!.GetMethod("DispatchDueAsync").ShouldNotBeNull("a test drives one pass; the worker merely repeats it");
    }

    /// <summary>
    /// A handler declares which message type it answers for, so dispatch is a lookup rather than a chain of
    /// string comparisons copied into every call site.
    /// </summary>
    [TestCase("CleanArchitecture.Infrastructure.Outbox.InvitationEmailDeliveryHandler")]
    [TestCase("CleanArchitecture.Infrastructure.Outbox.EmailConfirmationDeliveryHandler")]
    public void A_delivery_handler_declares_the_message_type_it_answers_for(string fullyQualifiedName)
    {
        var handler = InfrastructureAssembly.GetType(fullyQualifiedName);

        handler.ShouldNotBeNull();
        handler!.GetProperty("MessageType").ShouldNotBeNull();
        handler.GetMethod("HandleAsync").ShouldNotBeNull();
    }

    /// <summary>
    /// The retry and terminal transitions Task 7 left unreachable. The columns exist; nothing can write them, so
    /// a dispatcher cannot claim a message, record a transient failure, or stop retrying a hopeless one.
    /// </summary>
    [TestCase("Claim")]
    [TestCase("ReleaseLease")]
    [TestCase("Fail")]
    [TestCase("MarkDelivered")]
    [TestCase("Abandon")]
    public void OutboxMessageExposesItsDispatchTransition(string memberName)
    {
        typeof(OutboxMessage).GetMethod(memberName).ShouldNotBeNull();
    }

    [TestCase("Status")]
    [TestCase("LeaseExpiresAt")]
    [TestCase("LeaseOwner")]
    [TestCase("DeliveredAt")]
    public void OutboxMessageExposesItsDispatchState(string memberName)
    {
        typeof(OutboxMessage).GetProperty(memberName).ShouldNotBeNull();
    }

    /// <summary>Claiming is a compare-and-swap, so the row carries the token the swap compares (IA-REQ-028).</summary>
    [Test]
    public void An_outbox_message_carries_a_concurrency_token_for_its_claim()
    {
        typeof(OutboxMessage).GetProperty("Generation").ShouldNotBeNull("a lease is claimed by swapping a generation, never by a bare read-then-write");
    }

    [Test]
    public void OutboxMessageStateHasNoValueWithoutATransition()
    {
        var status = typeof(OutboxMessage).Assembly.GetType("CleanArchitecture.Domain.IdentityAccess.Outbox.OutboxMessageStatus");

        status.ShouldNotBeNull();
        Enum.GetNames(status!).ShouldBe(["Pending", "Delivered", "Abandoned"], ignoreOrder: true);
    }
}
