using System.Net;
using System.Text;
using System.Text.Json;
using HanieTo.Api.Catalog;
using Microsoft.Extensions.Configuration;

namespace HanieTo.Api.Tests;

public class RecordingHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        };
    }
}

public class ShopBotAdminClientTests
{
    private static IConfiguration ConfigWithApiKey(string? apiKey = "test-key") =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(apiKey is null
                ? []
                : new Dictionary<string, string?> { ["ShopBotInternalApi:ApiKey"] = apiKey })
            .Build();

    private static ShopBotAdminClient BuildClient(RecordingHandler handler, IConfiguration? configuration = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://shopbot.local") };
        return new ShopBotAdminClient(httpClient, configuration ?? ConfigWithApiKey());
    }

    [Fact]
    public async Task CreateProductAsync_SendsExpectedRequest_AndParsesResponse()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"added": 3}""");
        var client = BuildClient(handler);

        var result = await client.CreateProductAsync(
            new CreateProductRequest("PHYSICAL", "Shoes", "Sneakers", "Cool sneakers", 49.99, 3, null),
            CancellationToken.None);

        Assert.Equal(3, result.Added);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/internal/products", handler.LastRequest.RequestUri!.AbsolutePath);
        Assert.Equal("test-key", handler.LastRequest.Headers.GetValues("X-Internal-Api-Key").Single());

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("PHYSICAL", body.RootElement.GetProperty("item_type").GetString());
        Assert.Equal("Cool sneakers", body.RootElement.GetProperty("description").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task CreateProductAsync_WithoutApiKeyConfigured_ThrowsBeforeSendingRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var client = BuildClient(handler, ConfigWithApiKey(null));

        await Assert.ThrowsAsync<ShopBotAdminClientNotConfiguredException>(() =>
            client.CreateProductAsync(new CreateProductRequest("PHYSICAL", "c", "s", "d", 1, 1, null), CancellationToken.None));

        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task CreateProductAsync_OnErrorResponse_ThrowsWithStatusAndDetail()
    {
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, """{"detail": "price must be positive"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ShopBotAdminApiException>(() =>
            client.CreateProductAsync(new CreateProductRequest("PHYSICAL", "c", "s", "d", 1, 1, null), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Equal("price must be positive", ex.Message);
    }

    [Fact]
    public async Task UpdateOrderStatusAsync_SendsStatusAndTrackNumber()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, """{"id": 42, "status": "SHIPPED", "message": null}""");
        var client = BuildClient(handler);

        var result = await client.UpdateOrderStatusAsync(42, "SHIPPED", "TRACK123", CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.Equal("SHIPPED", result.Status);
        Assert.Equal("/internal/buys/42/status", handler.LastRequest!.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        Assert.Equal("SHIPPED", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("TRACK123", body.RootElement.GetProperty("track_number").GetString());
    }

    [Fact]
    public async Task RefundOrderAsync_OnNotFound_ThrowsWithStatusAndDetail()
    {
        var handler = new RecordingHandler(HttpStatusCode.NotFound, """{"detail": "Order not found, or already refunded"}""");
        var client = BuildClient(handler);

        var ex = await Assert.ThrowsAsync<ShopBotAdminApiException>(() =>
            client.RefundOrderAsync(7, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal("Order not found, or already refunded", ex.Message);
        Assert.Equal("/internal/buys/7/refund", handler.LastRequest!.RequestUri!.AbsolutePath);
    }
}
