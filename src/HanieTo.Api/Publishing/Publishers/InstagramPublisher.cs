using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Placeholder for the real Meta Graph API integration. No real credentials exist
// in this environment yet, so this simulates a successful publish instead of making
// an untestable external call. Swap this class out once a Channel.ApiKey (Meta
// access token) is wired up for real.
public class InstagramPublisher : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Instagram;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return new PublishOutcome(true, $"ig_{Guid.NewGuid():N}", null);
    }
}
