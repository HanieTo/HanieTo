using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

public class TelegramPublisher : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Telegram;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
        return new PublishOutcome(true, $"tg_{Guid.NewGuid():N}", null);
    }
}
