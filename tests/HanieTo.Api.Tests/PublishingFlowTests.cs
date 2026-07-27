using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using HanieTo.Api.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace HanieTo.Api.Tests;

public class PublishingFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"hanieto-test-{Guid.NewGuid():N}.db");
    private readonly HttpClient _client;

    public PublishingFlowTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_dbPath}"
                });
            });
        }).CreateClient();
    }

    [Fact]
    public async Task PublishContent_ToMultipleChannels_RecordsAttemptsAndMarksPublished()
    {
        var instagram = await CreateChannelAsync(ChannelType.Instagram, "My Instagram");
        var twitter = await CreateChannelAsync(ChannelType.Twitter, "My Twitter");

        var contentResponse = await _client.PostAsJsonAsync("/api/content", new { Title = "Hello", Body = "World" });
        contentResponse.EnsureSuccessStatusCode();
        var content = await contentResponse.Content.ReadFromJsonAsync<Content>(JsonOptions);
        Assert.NotNull(content);

        using var publishForm = new MultipartFormDataContent
        {
            { new StringContent(instagram.Id.ToString()), "ChannelIds" },
            { new StringContent(twitter.Id.ToString()), "ChannelIds" }
        };
        var publishResponse = await _client.PostAsync($"/api/content/{content!.Id}/publish", publishForm);
        publishResponse.EnsureSuccessStatusCode();

        var getResponse = await _client.GetAsync($"/api/content/{content.Id}");
        getResponse.EnsureSuccessStatusCode();
        var updated = await getResponse.Content.ReadFromJsonAsync<Content>(JsonOptions);

        Assert.NotNull(updated);
        Assert.Equal(ContentStatus.Published, updated!.Status);
        Assert.Equal(2, updated.PublishAttempts.Count);
        Assert.All(updated.PublishAttempts, a => Assert.Equal(PublishAttemptStatus.Succeeded, a.Status));
    }

    private async Task<Channel> CreateChannelAsync(ChannelType type, string displayName)
    {
        var response = await _client.PostAsJsonAsync("/api/channels", new { Type = type, DisplayName = displayName });
        response.EnsureSuccessStatusCode();
        var channel = await response.Content.ReadFromJsonAsync<Channel>(JsonOptions);
        Assert.NotNull(channel);
        return channel!;
    }

    public void Dispose()
    {
        _client.Dispose();
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        File.Delete(_dbPath + "-shm");
        File.Delete(_dbPath + "-wal");
        GC.SuppressFinalize(this);
    }
}
