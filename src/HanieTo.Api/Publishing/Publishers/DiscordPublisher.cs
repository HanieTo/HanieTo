using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with a Discord incoming webhook - no OAuth, no app review,
// just a URL from a channel's Integrations > Webhooks settings. Channel.ApiKey
// holds the full webhook URL (simplest option - Discord webhook URLs already
// embed both the id and token). Falls back to a simulated success without one.
public class DiscordPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Discord;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"discord_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();
        var webhookUrl = channel.ApiKey.Contains("wait=") ? channel.ApiKey : $"{channel.ApiKey}?wait=true";
        var text = $"**{content.Title}**\n{content.Body}";

        try
        {
            HttpResponseMessage response;

            if (media is not null)
            {
                using var form = new MultipartFormDataContent
                {
                    { new StringContent(text), "content" }
                };
                var fileContent = new ByteArrayContent(media.Bytes);
                fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(media.ContentType);
                form.Add(fileContent, "files[0]", media.FileName);

                response = await client.PostAsync(webhookUrl, form, cancellationToken);
            }
            else
            {
                using var jsonContent = System.Net.Http.Json.JsonContent.Create(new { content = text });
                response = await client.PostAsync(webhookUrl, jsonContent, cancellationToken);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    return new PublishOutcome(false, null, $"Discord webhook returned HTTP {(int)response.StatusCode}: {body}");
                }

                using var json = System.Text.Json.JsonDocument.Parse(body);
                var messageId = json.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                return new PublishOutcome(true, messageId, null);
            }
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
