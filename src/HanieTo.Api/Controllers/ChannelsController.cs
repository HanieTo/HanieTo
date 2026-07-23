using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record CreateChannelRequest(ChannelType Type, string DisplayName, string? ApiKey, string? ApiSecret);

[ApiController]
[Route("api/[controller]")]
public class ChannelsController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateChannelRequest request)
    {
        var channel = new Channel
        {
            Type = request.Type,
            DisplayName = request.DisplayName,
            ApiKey = request.ApiKey,
            ApiSecret = request.ApiSecret
        };

        db.Channels.Add(channel);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = channel.Id }, channel);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var channels = await db.Channels.ToListAsync();
        return Ok(channels);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var channel = await db.Channels.FindAsync(id);
        return channel is null ? NotFound() : Ok(channel);
    }
}
