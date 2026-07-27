namespace HanieTo.Api.Domain;

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ChannelType Type { get; set; }
    public required string DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;

    // Credentials for the real platform API (e.g. Telegram bot token, Twitter bearer
    // token, Instagram/Meta access token).
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }

    // Where on the platform to post (e.g. a Telegram chat id or "@channelusername").
    // Separate from ApiKey/ApiSecret because those identify *who is posting*, this
    // identifies *where*.
    public string? ExternalId { get; set; }
}
