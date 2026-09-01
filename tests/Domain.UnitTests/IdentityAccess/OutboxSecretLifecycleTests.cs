using CleanArchitecture.Domain.IdentityAccess.Outbox;
using NUnit.Framework;
using Shouldly;

namespace CleanArchitecture.Domain.UnitTests.IdentityAccess;

public sealed class OutboxSecretLifecycleTests
{
    [Test]
    public void Delivered_secret_can_be_consumed_without_erasing_delivery_evidence()
    {
        var deliveredAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var consumedAt = DateTimeOffset.UtcNow;
        var secret = OutboxSecret.Create(Guid.NewGuid(), "v1:hash", "ciphertext", consumedAt.AddHours(1));

        secret.MarkDelivered("provider_delivered", deliveredAt, "provider-receipt");
        secret.Consume("confirmation_consumed", consumedAt);

        secret.Status.ShouldBe(OutboxSecretStatus.Consumed);
        secret.Ciphertext.ShouldBeNull();
        secret.DeliveredAt.ShouldBe(deliveredAt);
        secret.DeliveryReason.ShouldBe("provider_delivered");
        secret.ProviderReceipt.ShouldBe("provider-receipt");
        secret.CompletedAt.ShouldBe(consumedAt);
        secret.TerminalReason.ShouldBe("confirmation_consumed");
    }

    [Test]
    public void Failed_secret_cannot_be_consumed()
    {
        var secret = OutboxSecret.Create(Guid.NewGuid(), "v1:hash", "ciphertext", DateTimeOffset.UtcNow.AddHours(1));

        secret.Terminate(OutboxSecretStatus.Failed, "delivery_failed", DateTimeOffset.UtcNow);

        Should.Throw<InvalidOperationException>(() => secret.Consume("confirmation_consumed", DateTimeOffset.UtcNow));
    }

    [Test]
    public void Pending_secret_cannot_record_a_provider_receipt_without_delivery_evidence()
    {
        var secret = OutboxSecret.Create(Guid.NewGuid(), "v1:hash", "ciphertext", DateTimeOffset.UtcNow.AddHours(1));

        Should.Throw<InvalidOperationException>(() => secret.Consume("confirmation_consumed", DateTimeOffset.UtcNow, "provider-receipt"));
    }
}
