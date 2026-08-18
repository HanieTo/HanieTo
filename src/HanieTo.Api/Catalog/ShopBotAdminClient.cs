using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HanieTo.Api.Catalog;

// Thrown when ShopBotInternalApi:ApiKey isn't configured yet - distinct from
// ShopBotAdminApiException so controllers can turn it into a clear "not set
// up" response instead of a generic failure.
public class ShopBotAdminClientNotConfiguredException()
    : Exception("Missing ShopBotInternalApi:ApiKey (set via 'dotnet user-secrets set ShopBotInternalApi:ApiKey \"...\"' - " +
                "must match the bot's INTERNAL_API_KEY). Writing products/orders from the dashboard isn't available until this is set.");

// The bot's internal API returned an error (validation failure, not found,
// etc.) - carries its HTTP status and {"detail": "..."} message through so
// the dashboard sees the same reason the bot gave.
public class ShopBotAdminApiException(HttpStatusCode statusCode, string detail)
    : Exception(detail)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public record CreateProductRequest(
    string ItemType, string Category, string Subcategory, string Description,
    double Price, int Quantity, IReadOnlyList<string>? DigitalCodes);

public record CreateProductResult(int Added);

public record UpdateProductPriceRequest(string Category, string Subcategory, string Description, double NewPrice);

public record UpdateProductPriceResult(int Updated);

public record OrderActionResult(int Id, string Status, string? Message);

// Calls the bot's internal write API (shopbot/internal_api/catalog_admin.py)
// instead of writing to the catalog Postgres database directly - see
// ShopCatalogDbContext for why this API only ever reads that database. The
// bot's pydantic models use snake_case field names (e.g. item_type,
// new_price), so every request/response on this client goes through the
// same snake_case JsonSerializerOptions rather than .NET's default PascalCase.
public class ShopBotAdminClient(HttpClient http, IConfiguration configuration)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private string ApiKey => configuration["ShopBotInternalApi:ApiKey"]
        ?? throw new ShopBotAdminClientNotConfiguredException();

    private HttpRequestMessage NewRequest(HttpMethod method, string path) =>
        new(method, path) { Headers = { { "X-Internal-Api-Key", ApiKey } } };

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string detail;
        try
        {
            var body = await response.Content.ReadFromJsonAsync<ErrorBody>(JsonOptions, ct);
            detail = body?.Detail ?? $"Shop bot internal API returned {(int)response.StatusCode}.";
        }
        catch
        {
            detail = $"Shop bot internal API returned {(int)response.StatusCode}.";
        }

        throw new ShopBotAdminApiException(response.StatusCode, detail);
    }

    public async Task<CreateProductResult> CreateProductAsync(CreateProductRequest request, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Post, "/internal/products");
        req.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<CreateProductResult>(JsonOptions, ct))!;
    }

    public async Task<UpdateProductPriceResult> UpdateProductPriceAsync(UpdateProductPriceRequest request, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Patch, "/internal/products/price");
        req.Content = JsonContent.Create(request, options: JsonOptions);
        using var response = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<UpdateProductPriceResult>(JsonOptions, ct))!;
    }

    public async Task<OrderActionResult> UpdateOrderStatusAsync(int buyId, string status, string? trackNumber, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Post, $"/internal/buys/{buyId}/status");
        req.Content = JsonContent.Create(new UpdateOrderStatusRequest(status, trackNumber), options: JsonOptions);
        using var response = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<OrderActionResult>(JsonOptions, ct))!;
    }

    public async Task<OrderActionResult> RefundOrderAsync(int buyId, CancellationToken ct)
    {
        using var req = NewRequest(HttpMethod.Post, $"/internal/buys/{buyId}/refund");
        using var response = await http.SendAsync(req, ct);
        await EnsureSuccessAsync(response, ct);
        return (await response.Content.ReadFromJsonAsync<OrderActionResult>(JsonOptions, ct))!;
    }

    private record UpdateOrderStatusRequest(string Status, string? TrackNumber);

    private record ErrorBody(string? Detail);
}
