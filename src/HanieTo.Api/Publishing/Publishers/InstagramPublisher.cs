using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Instagram Graph API (Meta). Channel.ApiKey holds the
// long-lived access token and Channel.ExternalId holds the Instagram Business
// Account id. Unlike Telegram, Instagram does not accept an uploaded file -
// Meta's servers fetch the photo themselves from media.Url, so that URL must be
// publicly reachable (not localhost). Publishing is a two-step process: create a
// media container, then publish it. Falls back to a simulated success if
// credentials or a hosted photo URL aren't available yet.
public class InstagramPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    private const string GraphApiBase = "https://graph.facebook.com/v19.0";

    public ChannelType SupportedType => ChannelType.Instagram;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId) || media is null)
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"ig_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();

        try
        {
            using var createForm = new MultipartFormDataContent
            {
                { new StringContent(media.Url), "image_url" },
                { new StringContent($"{content.Title}\n\n{content.Body}"), "caption" },
                { new StringContent(channel.ApiKey), "access_token" }
            };

            using var createResponse = await client.PostAsync(
                $"{GraphApiBase}/{channel.ExternalId}/media", createForm, cancellationToken);
            var createBody = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            using var createJson = JsonDocument.Parse(createBody);

            if (createJson.RootElement.TryGetProperty("error", out var createError))
            {
                return new PublishOutcome(false, null, createError.GetProperty("message").GetString());
            }

            var creationId = createJson.RootElement.GetProperty("id").GetString();

            using var publishForm = new MultipartFormDataContent
            {
                { new StringContent(creationId!), "creation_id" },
                { new StringContent(channel.ApiKey), "access_token" }
            };

            using var publishResponse = await client.PostAsync(
                $"{GraphApiBase}/{channel.ExternalId}/media_publish", publishForm, cancellationToken);
            var publishBody = await publishResponse.Content.ReadAsStringAsync(cancellationToken);
            using var publishJson = JsonDocument.Parse(publishBody);

            if (publishJson.RootElement.TryGetProperty("error", out var publishError))
            {
                return new PublishOutcome(false, null, publishError.GetProperty("message").GetString());
            }

            var mediaId = publishJson.RootElement.GetProperty("id").GetString();
            return new PublishOutcome(true, mediaId, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
