namespace HanieTo.Api.Domain;

public class Channel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ChannelType Type { get; set; }
    public required string DisplayName { get; set; }
    public bool IsEnabled { get; set; } = true;

    // Credentials for the real platform API (e.g. Telegram bot token, Twitter bearer
    // token, Instagram/Meta access token). Optional for now since the publishers are
    // stubs, but the field exists so a real key can be plugged in per channel later
    // without a schema change.
    public string? ApiKey { get; set; }
    public string? ApiSecret { get; set; }
}
