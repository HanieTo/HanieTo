using System.Net.Http.Json;
using System.Text.Json;
using HanieTo.Api.Domain;

namespace HanieTo.Api.Publishing.Publishers;

// Real integration with the Rubika Bot API (botapi.rubika.ir/v3) - an Iranian
// social/messaging platform. Channel.ApiKey holds the bot token (from
// @BotFather on Rubika), Channel.ExternalId holds the target chat id. Unlike
// Telegram/Bale, sending a file is a 3-step process: request an upload slot,
// upload the bytes to the returned URL, then send the resulting file id. Falls
// back to a simulated success without credentials.
public class RubikaPublisher(IHttpClientFactory httpClientFactory) : IChannelPublisher
{
    public ChannelType SupportedType => ChannelType.Rubika;

    public async Task<PublishOutcome> PublishAsync(Content content, Channel channel, PublishMedia? media, ListingDetails? listing, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel.ApiKey) || string.IsNullOrWhiteSpace(channel.ExternalId))
        {
            await Task.Delay(150, cancellationToken);
            return new PublishOutcome(true, $"rubika_{Guid.NewGuid():N}", null);
        }

        var client = httpClientFactory.CreateClient();
        var baseUrl = $"https://botapi.rubika.ir/v3/{channel.ApiKey}";
        var text = $"{content.Title}\n\n{content.Body}";

        try
        {
            if (media is not null)
            {
                var fileId = await UploadFileAsync(client, baseUrl, media, cancellationToken);
                if (fileId is null)
                {
                    return new PublishOutcome(false, null, "Rubika file upload did not return a file_id.");
                }

                using var sendFileRequest = JsonContent.Create(new { chat_id = channel.ExternalId, file_id = fileId, text });
                using var sendFileResponse = await client.PostAsync($"{baseUrl}/sendFile", sendFileRequest, cancellationToken);
                return await ParseSendResultAsync(sendFileResponse, cancellationToken);
            }

            using var sendMessageRequest = JsonContent.Create(new { chat_id = channel.ExternalId, text });
            using var sendMessageResponse = await client.PostAsync($"{baseUrl}/sendMessage", sendMessageRequest, cancellationToken);
            return await ParseSendResultAsync(sendMessageResponse, cancellationToken);
        }
        catch (Exception ex)
        {
            return new PublishOutcome(false, null, ex.Message);
        }
    }

    private static async Task<string?> UploadFileAsync(HttpClient client, string baseUrl, PublishMedia media, CancellationToken cancellationToken)
    {
        using var requestSendFileRequest = JsonContent.Create(new { type = "Image" });
        using var requestSendFileResponse = await client.PostAsync($"{baseUrl}/requestSendFile", requestSendFileRequest, cancellationToken);
        var requestSendFileBody = await requestSendFileResponse.Content.ReadAsStringAsync(cancellationToken);
        using var requestSendFileJson = JsonDocument.Parse(requestSendFileBody);

        var uploadUrl = requestSendFileJson.RootElement.GetProperty("data").GetProperty("upload_url").GetString();
        if (string.IsNullOrWhiteSpace(uploadUrl))
        {
            return null;
        }

        using var uploadForm = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(media.Bytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(media.ContentType);
        uploadForm.Add(fileContent, "file", media.FileName);

        using var uploadResponse = await client.PostAsync(uploadUrl, uploadForm, cancellationToken);
        var uploadBody = await uploadResponse.Content.ReadAsStringAsync(cancellationToken);
        using var uploadJson = JsonDocument.Parse(uploadBody);

        return uploadJson.RootElement.GetProperty("data").GetProperty("file_id").GetString();
    }

    private static async Task<PublishOutcome> ParseSendResultAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new PublishOutcome(false, null, $"Rubika API returned HTTP {(int)response.StatusCode}: {body}");
        }

        using var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("message_id", out var messageId))
        {
            return new PublishOutcome(true, messageId.ToString(), null);
        }

        return new PublishOutcome(true, null, null);
    }
}
