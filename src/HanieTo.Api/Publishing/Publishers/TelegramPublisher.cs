using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Telegram Bot API. Channel.ApiKey holds the bot token
// (from @BotFather) and Channel.ExternalId holds the target chat id or "@channel"
// handle the bot has been added to. If either is missing, falls back to a
// simulated success so channels created without real credentials still work.
public class TelegramPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Telegram;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"tg_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();
        var baseUrl = $"https://api.telegram.org/bot{channel.ApiKey}";

        try
        {
            using var form = new MultipartFormDataContent();
            string endpoint;

            if (media is not null)
            {
                endpoint = $"{baseUrl}/sendPhoto";
                form.Add(new StringContent(channel.ExternalId), "chat_id");
                form.Add(new StringContent($"{content.Title}\n\n{content.Body}"), "caption");
                var photoContent = new ByteArrayContent(media.Bytes);
                photoContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(media.ContentType);
                form.Add(photoContent, "photo", media.FileName);
            }
            else
            {
                endpoint = $"{baseUrl}/sendMessage";
                form.Add(new StringContent(channel.ExternalId), "chat_id");
                form.Add(new StringContent($"{content.Title}\n\n{content.Body}"), "text");
            }

            using var response = await client.PostAsync(endpoint, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);

            var ok = json.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (!ok)
            {
                var description = json.RootElement.TryGetProperty("description", out var descProp)
                    ? descProp.GetString()
                    : $"Telegram API returned HTTP {(int)response.StatusCode}";
                return new PublishOutcome(false, null, description);
            }

            var messageId = json.RootElement.GetProperty("result").GetProperty("message_id").GetInt64();
            return new PublishOutcome(true, messageId.ToString(), null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
