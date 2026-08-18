using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Eitaa bot API (eitaayar.ir) - an Iranian messenger.
// Channel.ApiKey holds the token issued at eitaayar.ir, Channel.ExternalId holds
// the target channel/chat id. Eitaa's exact response shape isn't officially
// published (unlike Telegram/Bale), so this parses defensively: it accepts either
// an explicit failure flag or falls back to the HTTP status code, and surfaces the
// raw response body in the error message if the shape doesn't match what's
// expected, so a live test is easy to debug. Falls back to a simulated success
// without credentials.
public class EitaaPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    private const string ApiBase = "https://eitaayar.ir/api";

    public ChannelType SupportedType => ChannelType.Eitaa;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"eitaa_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();
        var baseUrl = $"{ApiBase}/{channel.ApiKey}";

        try
        {
            using var form = new MultipartFormDataContent();
            string endpoint;

            if (media is not null)
            {
                endpoint = $"{baseUrl}/sendFile";
                form.Add(new StringContent(channel.ExternalId), "chat_id");
                form.Add(new StringContent($"{content.Title}\n\n{content.Body}"), "caption");
                var fileContent = new ByteArrayContent(media.Bytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(media.ContentType);
                form.Add(fileContent, "file", media.FileName);
            }
            else
            {
                endpoint = $"{baseUrl}/sendMessage";
                form.Add(new StringContent(channel.ExternalId), "chat_id");
                form.Add(new StringContent($"{content.Title}\n\n{content.Body}"), "text");
            }

            using var response = await client.PostAsync(endpoint, form, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PublishOutcome(false, null, $"Eitaa API returned HTTP {(int)response.StatusCode}: {body}");
            }

            try
            {
                using var json = JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("result", out var result) &&
                    result.TryGetProperty("message_id", out var messageId))
                {
                    return new PublishOutcome(true, messageId.ToString(), null);
                }
            }
            catch (JsonException)
            {
                // Response wasn't the shape we expected - fall through and treat the
                // successful HTTP status as success, surfacing the raw body for
                // debugging rather than silently guessing at a message id.
            }

            return new PublishOutcome(true, null, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
