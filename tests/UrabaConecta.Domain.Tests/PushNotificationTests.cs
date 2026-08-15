using UrabaConecta.Domain;
using UrabaConecta.Infrastructure;

namespace UrabaConecta.Domain.Tests;

public sealed class PushNotificationTests
{
    private static WebPushSubscription NewSubscription(PushAudience audience = PushAudience.Owner)
        => new(Guid.NewGuid(), Guid.NewGuid(), audience, Guid.NewGuid().ToString("N"), new string('a', 64),
            "https://push.example/subscription", "p256dh", "auth",
            audience == PushAudience.Owner ? Guid.NewGuid() : null,
            audience == PushAudience.Owner ? null : Guid.NewGuid(),
            audience == PushAudience.Owner ? null : "protected-link", DateTimeOffset.UtcNow);

    [Fact]
    public void Web_push_options_remove_bom_and_whitespace_from_environment_values()
    {
        var options = new WebPushOptions
        {
            Subject = " \uFEFFmailto:demo@urabaconecta.test\r\n",
            PublicKey = "\uFEFFpublic-key\r\n",
            PrivateKey = " \uFEFFprivate-key "
        };

        Assert.True(options.IsConfigured);
        Assert.Equal("mailto:demo@urabaconecta.test", options.NormalizedSubject);
        Assert.Equal("public-key", options.NormalizedPublicKey);
        Assert.Equal("private-key", options.NormalizedPrivateKey);
    }

    [Fact]
    public void Owner_subscription_requires_a_user_and_client_subscription_requires_an_entity()
    {
        Assert.Throws<DomainException>(() => new WebPushSubscription(Guid.NewGuid(), Guid.NewGuid(),
            PushAudience.Owner, "scope", "hash", "https://push.example/a", "key", "auth",
            null, null, null, DateTimeOffset.UtcNow));
        Assert.Throws<DomainException>(() => new WebPushSubscription(Guid.NewGuid(), Guid.NewGuid(),
            PushAudience.QueueTicket, "scope", "hash", "https://push.example/a", "key", "auth",
            null, null, "protected", DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Refresh_reactivates_and_three_transient_failures_deactivate()
    {
        var subscription = NewSubscription();
        var now = DateTimeOffset.UtcNow;
        subscription.MarkFailed(now, false);
        subscription.MarkFailed(now.AddMinutes(1), false);
        Assert.True(subscription.IsActive);
        subscription.MarkFailed(now.AddMinutes(2), false);
        Assert.False(subscription.IsActive);

        subscription.Refresh("https://push.example/new", "new-key", "new-auth", Guid.NewGuid(), null,
            now.AddMinutes(3));
        Assert.True(subscription.IsActive);
        Assert.Equal(0, subscription.FailureCount);
    }

    [Fact]
    public void Gone_endpoint_is_deactivated_immediately()
    {
        var subscription = NewSubscription(PushAudience.PickupOrder);
        subscription.MarkFailed(DateTimeOffset.UtcNow, true);
        Assert.False(subscription.IsActive);
    }

    [Fact]
    public void Promotion_requires_relative_deep_link_and_bounded_validity()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("INVALID_PROMOTION", Assert.Throws<DomainException>(() => new BusinessPromotion(
            Guid.NewGuid(), Guid.NewGuid(), "Oferta", null, "Ver", "https://outside.example",
            now, now.AddDays(1), true, now)).Code);
        Assert.Equal("INVALID_PROMOTION", Assert.Throws<DomainException>(() => new BusinessPromotion(
            Guid.NewGuid(), Guid.NewGuid(), "Oferta", null, "Ver", "//outside.example",
            now, now.AddDays(1), true, now)).Code);
        Assert.Equal("INVALID_PROMOTION", Assert.Throws<DomainException>(() => new BusinessPromotion(
            Guid.NewGuid(), Guid.NewGuid(), "Oferta", null, "Ver", "/negocio",
            now, now.AddDays(32), true, now)).Code);
    }
}
