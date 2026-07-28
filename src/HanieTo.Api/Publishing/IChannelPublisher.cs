using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing;

public interface IChannelPublisher
{
    ChannelType SupportedType { get; }
    Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken);
}
