using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing;

public class ChannelPublisherResolver(IEnumerable<IChannelPublisher> publishers)
{
    private readonly Dictionary<ChannelType, IChannelPublisher> _publishersByType =
        publishers.ToDictionary(p => p.SupportedType);

    public IChannelPublisher Resolve(ChannelType type)
    {
        if (!_publishersByType.TryGetValue(type, out var publisher))
        {
            throw new NotSupportedException($"No publisher is registered for channel type '{type}'.");
        }

        return publisher;
    }
}
