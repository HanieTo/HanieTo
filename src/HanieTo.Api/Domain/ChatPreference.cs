namespace HanieTo.Api.Domain;

// Per-chat bot settings, so a user's chosen language sticks between sessions.
// Keyed by the Telegram chat id.
public class ChatPreference
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ChatId { get; set; }
    public BotLanguage Language { get; set; } = BotLanguage.English;
}
