using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Pinterest API v5. Channel.ApiKey holds an OAuth2
// access token, Channel.ExternalId holds the target board id. A Pin is
// fundamentally an image, so this needs media.Url to be publicly reachable
// (Pinterest fetches it, same constraint as Instagram) - falls back to a
// simulated success without credentials or a hosted photo.
public class PinterestPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Pinterest;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId) || media is null)
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"pin_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();

        try
        {
            var body = new
            {
                board_id = channel.ExternalId,
                title = content.Title,
                description = content.Body,
                media_source = new { source_type = "image_url", url = media.Url }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.pinterest.com/v5/pins")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channel.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PublishOutcome(false, null, $"Pinterest API returned HTTP {(int)response.StatusCode}: {responseBody}");
            }

            using var json = JsonDocument.Parse(responseBody);
            var pinId = json.RootElement.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            return new PublishOutcome(true, pinId, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
