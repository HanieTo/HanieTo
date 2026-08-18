using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Stub only. Divar's public "Kenar" API is OAuth2-based and, from what's publicly
// documented, is built around managing/enriching listings a business already has
// on Divar (add-ons, chat, search) - not clearly a "create a brand-new listing"
// endpoint. Real integration needs confirming that capability exists (and getting
// app slug / API key / OAuth secret from Divar's developer panel) before this can
// do more than simulate success. ListingDetails (price/category/city) is already
// threaded through the publish flow so wiring a real call later only means
// filling in this class.
public class DivarPublisher : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Divar;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return new PublishOutcome(true, $"divar_{Guid.NewGuid():N}", null);
    }
}
