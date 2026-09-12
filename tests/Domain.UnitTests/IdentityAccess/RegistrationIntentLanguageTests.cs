using CleanArchitecture.Domain.IdentityAccess.Organizations;
using CleanArchitecture.Domain.IdentityAccess.People;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class RegistrationIntentLanguageTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void Organization_open_captures_and_preserves_its_language_snapshot()
    {
        var intent = PendingRegistrationIntent.Open(
            Guid.NewGuid(),
            "ana@example.test",
            "Acme S.A.",
            NormalizedCuit.From("30-71234567-1"),
            "password-hash",
            "es",
            Now,
            Now.AddDays(1));

        intent.Complete(PendingRegistrationIntentOutcome.Created, Now.AddHours(1));

        intent.Language.ShouldBe("es");
    }

    [Test]
    public void Organization_notice_captures_its_language_snapshot()
    {
        var intent = PendingRegistrationIntent.Notify(
            Guid.NewGuid(),
            "ana@example.test",
            "Acme S.A.",
            NormalizedCuit.From("30-71234567-1"),
            "es",
            Now,
            Now.AddDays(1));

        intent.Language.ShouldBe("es");
    }

    [Test]
    public void Personal_open_captures_and_preserves_its_language_snapshot()
    {
        var intent = PendingPersonalIntent.Open(
            Guid.NewGuid(),
            "ana@example.test",
            "Ana Example",
            "Ana",
            "sealed-document",
            "password-hash",
            "es",
            Now,
            Now.AddDays(1));

        intent.Complete(PendingRegistrationIntentOutcome.Created, Now.AddHours(1));

        intent.Language.ShouldBe("es");
    }

    [Test]
    public void Personal_notice_captures_its_language_snapshot()
    {
        var intent = PendingPersonalIntent.Notify(
            Guid.NewGuid(),
            "ana@example.test",
            "Ana Example",
            "Ana",
            "es",
            Now,
            Now.AddDays(1));

        intent.Language.ShouldBe("es");
    }
}
