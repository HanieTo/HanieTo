namespace HanieTo.Api.Domain;

public class PublishAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ContentId { get; set; }
    public Guid ChannelId { get; set; }
    public Channel? Channel { get; set; }
    public PublishAttemptStatus Status { get; set; }
    public string? ExternalPostId { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
}
