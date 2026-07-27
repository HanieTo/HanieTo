namespace HanieTo.Api.Publishing;

public record PublishOutcome(bool Success, string? ExternalPostId, string? ErrorMessage);
