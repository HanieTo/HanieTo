using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the LinkedIn Posts API. Channel.ApiKey holds an OAuth2
// access token with w_organization_social (company page) or w_member_social
// (personal) scope. Channel.ExternalId holds the author URN, e.g.
// "urn:li:organization:12345" or "urn:li:person:abc123". Posting an image is a
// 2-step process: register + upload the image via the Images API to get an
// image URN, then reference it in the post. Falls back to a simulated success
// without credentials.
public class LinkedInPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    // LinkedIn requires a YYYYMM version header; bump this periodically.
    private const string ApiVersion = "202607";

    public ChannelType SupportedType => ChannelType.LinkedIn;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"li_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();

        try
        {
            string? imageUrn = media is not null
                ? await UploadImageAsync(client, channel, media, cancellationToken)
                : null;

            object body = imageUrn is not null
                ? new
                {
                    author = channel.ExternalId,
                    commentary = $"{content.Title}\n\n{content.Body}",
                    visibility = "PUBLIC",
                    distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<object>(), thirdPartyDistributionChannels = Array.Empty<object>() },
                    content = new { media = new { id = imageUrn } },
                    lifecycleState = "PUBLISHED",
                    isReshareDisabledByAuthor = false
                }
                : new
                {
                    author = channel.ExternalId,
                    commentary = $"{content.Title}\n\n{content.Body}",
                    visibility = "PUBLIC",
                    distribution = new { feedDistribution = "MAIN_FEED", targetEntities = Array.Empty<object>(), thirdPartyDistributionChannels = Array.Empty<object>() },
                    lifecycleState = "PUBLISHED",
                    isReshareDisabledByAuthor = false
                };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/posts")
            {
                Content = JsonContent.Create(body)
            };
            AddCommonHeaders(request, channel.ApiKey);

            using var response = await client.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new PublishOutcome(false, null, $"LinkedIn API returned HTTP {(int)response.StatusCode}: {errorBody}");
            }

            var postId = response.Headers.TryGetValues("x-restli-id", out var values) ? values.FirstOrDefault() : null;
            return new PublishOutcome(true, postId, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }

    private static async Task<string?> UploadImageAsync(HttpClient client, Channel channel, PublishMedia media, CancellationToken cancellationToken)
    {
        using var initRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.linkedin.com/rest/images?action=initializeUpload")
        {
            Content = JsonContent.Create(new { initializeUploadRequest = new { owner = channel.ExternalId } })
        };
        AddCommonHeaders(initRequest, channel.ApiKey!);

        using var initResponse = await client.SendAsync(initRequest, cancellationToken);
        var initBody = await initResponse.Content.ReadAsStringAsync(cancellationToken);
        using var initJson = JsonDocument.Parse(initBody);

        var value = initJson.RootElement.GetProperty("value");
        var uploadUrl = value.GetProperty("uploadUrl").GetString();
        var imageUrn = value.GetProperty("image").GetString();

        if (string.IsNullOrWhiteSpace(uploadUrl))
        {
            return null;
        }

        using var uploadRequest = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
        {
            Content = new ByteArrayContent(media.Bytes)
        };
        uploadRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", channel.ApiKey);
        await client.SendAsync(uploadRequest, cancellationToken);

        return imageUrn;
    }

    private static void AddCommonHeaders(HttpRequestMessage request, string accessToken)
    {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("X-Restli-Protocol-Version", "2.0.0");
        request.Headers.TryAddWithoutValidation("Linkedin-Version", ApiVersion);
    }
}
