using HanieTo.Api.Domain;
using HanieTo.Api.Publishing;

namespace HanieTo.Api.Tests;

public class FakePublisher(ChannelType type) : IChannelPublisher
{
    public ChannelType SupportedType { get; } = type;

    public Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, CancellationToken cancellationToken)
        => Task.FromResult(new PublishOutcome(true, "fake_id", null));
}

public class ChannelPublisherResolverTests
{
    [Fact]
    public void Resolve_ReturnsPublisher_ForRegisteredType()
    {
        var resolver = new ChannelPublisherResolver([new FakePublisher(ChannelType.Instagram)]);

        var publisher = resolver.Resolve(ChannelType.Instagram);

        Assert.Equal(ChannelType.Instagram, publisher.SupportedType);
    }

    [Fact]
    public void Resolve_Throws_ForUnregisteredType()
    {
        var resolver = new ChannelPublisherResolver([new FakePublisher(ChannelType.Instagram)]);

        Assert.Throws<NotSupportedException>(() => resolver.Resolve(ChannelType.Twitter));
    }
}
