using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

public class TwitterPublisher : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Twitter;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return new PublishOutcome(true, $"tw_{Guid.NewGuid():N}", null);
    }
}
