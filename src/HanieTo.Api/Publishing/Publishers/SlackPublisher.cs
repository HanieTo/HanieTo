using System.Net.Http.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with a Slack incoming webhook. Channel.ApiKey holds the full
// webhook URL (from a Slack App's Incoming Webhooks feature). Incoming webhooks
// can't accept a raw file upload, only a hosted image URL - so like Instagram,
// this needs media.Url to be publicly reachable (not localhost) for a photo to
// actually show up. Falls back to a simulated success without a webhook URL.
public class SlackPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Slack;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"slack_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();

        object payload = media is not null
            ? new
            {
                text = content.Title,
                blocks = new object[]
                {
                    new { type = "section", text = new { type = "mrkdwn", text = $"*{content.Title}*\n{content.Body}" } },
                    new { type = "image", image_url = media.Url, alt_text = content.Title }
                }
            }
            : new { text = $"*{content.Title}*\n{content.Body}" };

        try
        {
            using var jsonContent = JsonContent.Create(payload);
            using var response = await client.PostAsync(channel.ApiKey, jsonContent, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode || body != "ok")
            {
                return new PublishOutcome(false, null, $"Slack webhook returned HTTP {(int)response.StatusCode}: {body}");
            }

            return new PublishOutcome(true, null, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
