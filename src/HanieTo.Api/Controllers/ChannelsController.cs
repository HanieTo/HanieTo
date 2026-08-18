using System.Text.Json;
using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record CreateChannelRequest(
    ChannelType Type, string DisplayName, string? ApiKey, string? ApiSecret,
    string? AccessToken, string? AccessTokenSecret, string? ExternalId);

public record DiscoveredChatId(string ChatId, string? ChatTitle);

// "Health" here just means: does it have credentials set, and how did its
// last publish attempt go - not a live check against the platform's API.
public record ChannelHealthDto(
    Guid Id, ChannelType Type, string DisplayName, bool IsEnabled, string? ExternalId,
    bool HasCredentials, PublishAttemptStatus? LastPublishStatus, DateTime? LastPublishAtUtc);

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(AppDbContext db, IHttpClientFactory httpClientFactory) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateChannelRequest request)
    {
        var channel = new Channel
        {
            Type = request.Type,
            DisplayName = request.DisplayName,
            ApiKey = request.ApiKey,
            ApiSecret = request.ApiSecret,
            AccessToken = request.AccessToken,
            AccessTokenSecret = request.AccessTokenSecret,
            ExternalId = request.ExternalId
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, channel);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var channels = await (
            from channel in db.Channels
            select new ChannelHealthDto(
                channel.Id, channel.Type, channel.DisplayName, channel.IsEnabled, channel.ExternalId,
                channel.ApiKey != null || channel.AccessToken != null,
                db.PublishAttempts
                    .Where(a => a.ChannelId == channel.Id)
                    .OrderByDescending(a => a.AttemptedAtUtc)
                    .Select(a => (PublishAttemptStatus?)a.Status)
                    .FirstOrDefault(),
                db.PublishAttempts
                    .Where(a => a.ChannelId == channel.Id)
                    .OrderByDescending(a => a.AttemptedAtUtc)
                    .Select(a => (DateTime?)a.AttemptedAtUtc)
                    .FirstOrDefault())
        ).ToListAsync();

        return Ok(channels);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var channel = await db.Channels.FindAsync(id);
        return channel is null ? NotFound() : Ok(channel);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var channel = await db.Channels.FindAsync(id);
        if (channel is null)
        {
            return NotFound();
        }

        db.Channels.Remove(channel);
        await db.SaveChangesAsync();
        return NoContent();
    }

    // Telegram sends a `my_chat_member` update the moment a bot's role changes in
    // a chat (e.g. being made an admin) - and a `channel_post` update whenever
    // anything is posted. Reading those off the bot's own update feed lets us find
    // a private channel's numeric chat id automatically, without the token ever
    // leaving the database or the user needing to forward a message to a
    // third-party id-lookup bot.
    [HttpPost("{id:guid}/discover-telegram-chat-id")]
    public async Task<IActionResult> DiscoverTelegramChatId(Guid id, CancellationToken cancellationToken)
    {
        var channel = await db.Channels.FindAsync([id], cancellationToken);
        if (channel is null)
        {
            return NotFound();
        }

        if (channel.Type != ChannelType.Telegram || string.IsNullOrWhiteSpace(channel.ApiKey))
        {
            return BadRequest("This only works for a Telegram channel that already has an API key (bot token) set.");
        }

        var client = httpClientFactory.CreateClient();
        var response = await client.GetAsync($"https://api.telegram.org/bot{channel.ApiKey}/getUpdates?limit=100", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        using var json = JsonDocument.Parse(body);
        if (!json.RootElement.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
        {
            var description = json.RootElement.TryGetProperty("description", out var descProp)
                ? descProp.GetString()
                : "Telegram API call failed.";
            return BadRequest(description);
        }

        string? foundChatId = null;
        string? foundChatTitle = null;

        foreach (var update in json.RootElement.GetProperty("result").EnumerateArray())
        {
            if (update.TryGetProperty("my_chat_member", out var myChatMember) &&
                myChatMember.TryGetProperty("chat", out var chatFromMembership))
            {
                foundChatId = chatFromMembership.GetProperty("id").GetInt64().ToString();
                foundChatTitle = chatFromMembership.TryGetProperty("title", out var t1) ? t1.GetString() : null;
            }
            else if (update.TryGetProperty("channel_post", out var channelPost) &&
                     channelPost.TryGetProperty("chat", out var chatFromPost))
            {
                foundChatId = chatFromPost.GetProperty("id").GetInt64().ToString();
                foundChatTitle = chatFromPost.TryGetProperty("title", out var t2) ? t2.GetString() : null;
            }
        }

        if (foundChatId is null)
        {
            return NotFound("No recent activity found for this bot yet. In the channel, try removing and re-adding it as admin (or post any message), then try again.");
        }

        channel.ExternalId = foundChatId;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new DiscoveredChatId(foundChatId, foundChatTitle));
    }
}
