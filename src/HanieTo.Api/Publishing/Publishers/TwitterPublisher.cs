using System.Net.Http.Json;
using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Twitter/X API. Posting requires OAuth 1.0a user
// context (app-only bearer tokens can't post), so this needs 4 credentials:
// Channel.ApiKey/ApiSecret as the consumer key/secret, Channel.AccessToken/
// AccessTokenSecret as the user access token/secret (all generated once from the
// X Developer Portal for your own account - no interactive OAuth flow needed).
// Media still goes through the older v1.1 chunked upload endpoint (INIT/APPEND/
// FINALIZE) since v2 has no native upload endpoint yet; the resulting media_id is
// then attached to a v2 tweet. Falls back to a simulated success without
// credentials. Note: X now charges per post ($0.015, $0.20 if the text contains a
// URL) - this makes real API calls that cost money once credentials are set.
public class TwitterPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    private const string UploadBase = "https://upload.twitter.com/1.1/media/upload.json";
    private const string TweetsUrl = "https://api.x.com/2/tweets";

    public ChannelType SupportedType => ChannelType.Twitter;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ApiSecret) ||
            string.IsNullOrWhiteSpace(channel.AccessToken) || string.IsNullOrWhiteSpace(channel.AccessTokenSecret))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"tw_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();

        try
        {
            string? mediaId = media is not null
                ? await UploadMediaAsync(client, channel, media, cancellationToken)
                : null;

            var text = $"{content.Title}\n\n{content.Body}";
            var body = mediaId is not null
                ? new { text, media = new { media_ids = new[] { mediaId } } }
                : (object)new { text };

            using var jsonContent = JsonContent.Create(body);
            var authHeader = OAuth1Signer.BuildAuthorizationHeader(
                "POST", TweetsUrl, channel.ApiKey, channel.ApiSecret, channel.AccessToken, channel.AccessTokenSecret);

            using var request = new HttpRequestMessage(HttpMethod.Post, TweetsUrl) { Content = jsonContent };
            request.Headers.TryAddWithoutValidation("Authorization", authHeader);

            using var response = await client.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(responseBody);

            if (json.RootElement.TryGetProperty("errors", out var errors))
            {
                var message = errors.GetArrayLength() > 0 ? errors[0].GetProperty("message").GetString() : responseBody;
                return new PublishOutcome(false, null, message);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new PublishOutcome(false, null, $"X API returned HTTP {(int)response.StatusCode}: {responseBody}");
            }

            var tweetId = json.RootElement.GetProperty("data").GetProperty("id").GetString();
            return new PublishOutcome(true, tweetId, null);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }

    private static async Task<string?> UploadMediaAsync(HttpClient client, Channel channel, PublishMedia media, CancellationToken cancellationToken)
    {
        // INIT
        var initParams = new Dictionary<string, string>
        {
            ["command"] = "INIT",
            ["media_type"] = media.ContentType,
            ["total_bytes"] = media.Bytes.Length.ToString()
        };
        var mediaId = await PostFormSignedAsync(client, channel, initParams, cancellationToken,
            json => json.RootElement.GetProperty("media_id_string").GetString());

        if (mediaId is null)
        {
            return null;
        }

        // APPEND (only the text fields are part of the OAuth signature, not the binary part)
        var appendParams = new Dictionary<string, string>
        {
            ["command"] = "APPEND",
            ["media_id"] = mediaId,
            ["segment_index"] = "0"
        };
        var authHeader = OAuth1Signer.BuildAuthorizationHeader(
            "POST", UploadBase, channel.ApiKey!, channel.ApiSecret!, channel.AccessToken!, channel.AccessTokenSecret!, appendParams);

        using var appendForm = new MultipartFormDataContent
        {
            { new StringContent("APPEND"), "command" },
            { new StringContent(mediaId), "media_id" },
            { new StringContent("0"), "segment_index" }
        };
        var mediaContent = new ByteArrayContent(media.Bytes);
        mediaContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(media.ContentType);
        appendForm.Add(mediaContent, "media", media.FileName);

        using var appendRequest = new HttpRequestMessage(HttpMethod.Post, UploadBase) { Content = appendForm };
        appendRequest.Headers.TryAddWithoutValidation("Authorization", authHeader);
        await client.SendAsync(appendRequest, cancellationToken);

        // FINALIZE
        var finalizeParams = new Dictionary<string, string>
        {
            ["command"] = "FINALIZE",
            ["media_id"] = mediaId
        };
        await PostFormSignedAsync(client, channel, finalizeParams, cancellationToken, _ => mediaId);

        return mediaId;
    }

    private static async Task<string?> PostFormSignedAsync(
        HttpClient client, Channel channel, Dictionary<string, string> formParams, CancellationToken cancellationToken,
        Func<JsonDocument, string?> extract)
    {
        var authHeader = OAuth1Signer.BuildAuthorizationHeader(
            "POST", UploadBase, channel.ApiKey!, channel.ApiSecret!, channel.AccessToken!, channel.AccessTokenSecret!, formParams);

        using var request = new HttpRequestMessage(HttpMethod.Post, UploadBase)
        {
            Content = new FormUrlEncodedContent(formParams)
        };
        request.Headers.TryAddWithoutValidation("Authorization", authHeader);

        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var json = JsonDocument.Parse(body);
        return extract(json);
    }
}
