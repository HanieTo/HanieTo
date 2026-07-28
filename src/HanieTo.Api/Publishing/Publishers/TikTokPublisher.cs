using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the TikTok Content Posting API (photo direct post).
// Channel.ApiKey holds a user OAuth2 access token with the video.publish scope
// (the token itself identifies the creator, so no separate ExternalId is
// needed). Pulls the photo from media.Url, so - like Instagram/Pinterest - that
// URL must be publicly reachable. privacy_level is hardcoded to SELF_ONLY: an
// app that hasn't passed TikTok's audit (a multi-week review, separate from
// basic developer signup) can only post privately regardless of what's
// requested here, and defaulting to PUBLIC_TO_EVERYONE would just fail outright
// for anyone who hasn't been through that audit. Bump this once audited. Falls
// back to a simulated success without credentials or a hosted photo.
public class TikTokPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.TikTok;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || media is null)
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"tiktok_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();

        try
        {
            var body = new
            {
                media_type = "PHOTO",
                post_mode = "DIRECT_POST",
                post_info = new
                {
                    title = content.Title,
                    description = content.Body,
                    privacy_level = "SELF_ONLY",
                    disable_comment = false,
                    auto_add_music = true
                },
                source_info = new
                {
                    source = "PULL_FROM_URL",
                    photo_images = new[] { media.Url },
                    photo_cover_index = 0
                }
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://open.tiktokapis.com/v2/post/publish/content/init/")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channel.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(responseBody);

            var errorCode = json.RootElement.GetProperty("error").GetProperty("code").GetString();
            if (errorCode != "ok")
            {
                var message = json.RootElement.GetProperty("error").GetProperty("message").GetString();
                return new PublishOutcome(false, null, $"{errorCode}: {message}");
            }

            var publishId = json.RootElement.GetProperty("data").GetProperty("publish_id").GetString();
            return new PublishOutcome(true, publishId, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }
}
