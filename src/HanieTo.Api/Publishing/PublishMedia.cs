namespace HanieTo.Api.Publishing;

// Url is a publicly-reachable link to the photo, used by platforms whose API fetches
// the image itself (e.g. Instagram/Meta) rather than accepting an uploaded file.
public record PublishMedia(byte[] Bytes, string FileName, string ContentType, string Url);
