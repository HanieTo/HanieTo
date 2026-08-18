namespace HanieTo.Api.Publishing;

// Extra fields needed for marketplace-style channels (e.g. Divar) where "publishing"
// means creating a structured classified ad rather than a caption+photo social post.
public record ListingDetails(decimal? Price, string? Category, string? City);
