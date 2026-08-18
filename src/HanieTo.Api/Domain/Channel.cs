using System.Text.Json.Serialization;

namespace HanieTo.Api.Domain;

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ChannelType Type { get; set; }
    public required string DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;

    // Credentials for the real platform API (e.g. Telegram bot token, Twitter API
    // key/secret, Instagram/Meta access token). JsonIgnore only affects
    // serialization (API responses) - EF Core still persists these fine, and
    // publishers still read them directly off the tracked entity. Without this,
    // GET /api/channels and every PublishAttempt.Channel echo secrets back to
    // the browser in plaintext.
    [JsonIgnore]
    public string? ApiKey { get; set; }
    [JsonIgnore]
    public string? ApiSecret { get; set; }

    // A second credential pair, needed for platforms whose auth model requires 4
    // values rather than 2 - e.g. Twitter/X's OAuth 1.0a (ApiKey/ApiSecret as the
    // consumer key/secret, these as the access token/secret).
    [JsonIgnore]
    public string? AccessToken { get; set; }
    [JsonIgnore]
    public string? AccessTokenSecret { get; set; }

    // Where on the platform to post (e.g. a Telegram chat id, LinkedIn organization
    // URN, Pinterest board id). Separate from the credential fields because those
    // identify *who is posting*, this identifies *where*.
    public string? ExternalId { get; set; }
}
