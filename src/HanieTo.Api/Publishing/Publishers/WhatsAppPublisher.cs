using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Stub only. WhatsApp's real API (the WhatsApp Business Cloud API) is provisioned
// through the same Meta Developer portal used for Instagram - which is currently
// geo-blocked for this account. Wire this up for real once that access issue is
// resolved; the shape would mirror InstagramPublisher (Channel.ApiKey as the
// access token, Channel.ExternalId as the phone number id).
public class WhatsAppPublisher : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.WhatsApp;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return new PublishOutcome(true, $"wa_{Guid.NewGuid():N}", null);
    }
}
