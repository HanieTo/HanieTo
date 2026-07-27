namespace HanieTo.Api.Domain;

public class Content
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public required string Body { get; set; }
    public ContentStatus Status { get; set; } = ContentStatus.Draft;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<PublishAttempt> PublishAttempts { get; set; } = [];
}
