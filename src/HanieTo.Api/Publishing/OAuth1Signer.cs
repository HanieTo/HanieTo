using System.Security.Cryptography;
using System.Text;

namespace HanieTo.Api.Publishing;

// Hand-rolled OAuth 1.0a request signing (RFC 5849), needed for the Twitter/X v1.1
// media upload endpoints and as an option for the v2 endpoints. There's no built-in
// .NET support for this, and pulling in a full third-party X SDK felt like more
// dependency surface than a single well-defined signing algorithm warrants.
public static class OAuth1Signer
{
    // extraParams: any x-www-form-urlencoded body or query string parameters that
    // must be included in the signature base string per the OAuth1.0a spec. Do NOT
    // pass multipart binary fields here - only plain text form fields are signed.
    public static string BuildAuthorizationHeader(
        string httpMethod,
        string url,
        string consumerKey,
        string consumerSecret,
        string accessToken,
        string accessTokenSecret,
        IDictionary<string, string>? extraParams = null)
    {
        var oauthParams = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = consumerKey,
            ["oauth_nonce"] = Guid.NewGuid().ToString("N"),
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["oauth_token"] = accessToken,
            ["oauth_version"] = "1.0"
        };

        var allParams = new SortedDictionary<string, string>(oauthParams, StringComparer.Ordinal);
        if (extraParams is not null)
        {
            foreach (var (key, value) in extraParams)
            {
                allParams[key] = value;
            }
        }

        var parameterString = string.Join("&",
            allParams.Select(p => $"{PercentEncode(p.Key)}={PercentEncode(p.Value)}"));

        var signatureBase = string.Join("&",
            httpMethod.ToUpperInvariant(),
            PercentEncode(url),
            PercentEncode(parameterString));

        var signingKey = $"{PercentEncode(consumerSecret)}&{PercentEncode(accessTokenSecret)}";

        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(signingKey));
        var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureBase)));

        var headerParams = new SortedDictionary<string, string>(oauthParams, StringComparer.Ordinal)
        {
            ["oauth_signature"] = signature
        };

        var header = "OAuth " + string.Join(", ",
            headerParams.Select(p => $"{PercentEncode(p.Key)}=\"{PercentEncode(p.Value)}\""));

        return header;
    }

    // RFC 3986 percent-encoding, which is stricter than Uri.EscapeDataString about
    // a handful of characters OAuth1.0a requires encoded (!*'()) - those must be
    // encoded too, unlike the .NET default.
    public static string PercentEncode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            var c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                c is '-' or '.' or '_' or '~')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%').Append(b.ToString("X2"));
            }
        }
        return sb.ToString();
    }
}
