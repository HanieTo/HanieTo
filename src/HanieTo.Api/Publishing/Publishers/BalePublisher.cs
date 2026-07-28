using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Bale Bot API (tapi.bale.ai) - Bale is an Iranian
// messenger whose bot API is a Telegram Bot API-compatible clone, same shape as
// TelegramPublisher. Channel.ApiKey holds the bot token, Channel.ExternalId holds
// the target chat id. Falls back to a simulated success without credentials.
public class BalePublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Bale;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"bale_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();
        var baseUrl = $"https://tapi.bale.ai/bot{channel.ApiKey}";

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
                    : $"Bale API returned HTTP {(int)response.StatusCode}";
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
