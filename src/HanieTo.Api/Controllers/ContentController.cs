using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using HanieTo.Api.Publishing;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record CreateContentRequest(string Title, string Body);
public record PublishAttemptResult(Guid ChannelId, string ChannelName, bool Success, string? ExternalPostId, string? ErrorMessage);

public class PublishContentForm
{
    public List<Guid> ChannelIds { get; set; } = [];
    public IFormFile? Photo { get; set; }

    // Only meaningful for marketplace-style channels (e.g. Divar).
    public decimal? Price { get; set; }
    public string? Category { get; set; }
    public string? City { get; set; }
}

[ApiController]
[Route("api/[controller]")]
public class ContentController(AppDbContext db, ChannelPublisherResolver resolver, IWebHostEnvironment env) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(CreateContentRequest request)
    {
        var content = new Content { Title = request.Title, Body = request.Body };
        db.Contents.Add(content);
        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetById), new { id = content.Id }, content);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var content = await db.Contents
            .Include(c => c.PublishAttempts)
            .ThenInclude(pa => pa.Channel)
            .FirstOrDefaultAsync(c => c.Id == id);

        return content is null ? NotFound() : Ok(content);
    }

    [HttpPost("{id:guid}/publish")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Publish(Guid id, [FromForm] PublishContentForm form)
    {
        var content = await db.Contents.FirstOrDefaultAsync(c => c.Id == id);
        if (content is null)
        {
            return NotFound();
        }

        var channels = await db.Channels
            .Where(c => form.ChannelIds.Contains(c.Id))
            .ToListAsync();

        if (channels.Count != form.ChannelIds.Count)
        {
            return BadRequest("One or more channel ids do not exist.");
        }

        PublishMedia? media = null;
        if (form.Photo is not null)
        {
            using var ms = new MemoryStream();
            await form.Photo.CopyToAsync(ms, HttpContext.RequestAborted);
            var bytes = ms.ToArray();

            var uploadsDir = Path.Combine(env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploadsDir);
            var storedFileName = $"{Guid.NewGuid():N}{Path.GetExtension(form.Photo.FileName)}";
            await System.IO.File.WriteAllBytesAsync(Path.Combine(uploadsDir, storedFileName), bytes, HttpContext.RequestAborted);

            var url = $"{Request.Scheme}://{Request.Host}/uploads/{storedFileName}";
            media = new PublishMedia(bytes, form.Photo.FileName, form.Photo.ContentType, url);
        }

        ListingDetails? listing = null;
        if (form.Price is not null || form.Category is not null || form.City is not null)
        {
            listing = new ListingDetails(form.Price, form.Category, form.City);
        }

        content.Status = ContentStatus.Publishing;

        var publishTasks = channels.Select(async channel =>
        {
            var publisher = resolver.Resolve(channel.Type);
            var outcome = await publisher.PublishAsync(content, channel, media, listing, HttpContext.RequestAborted);

            var attempt = new PublishAttempt
            {
                ContentId = content.Id,
                ChannelId = channel.Id,
                Status = outcome.Success ? PublishAttemptStatus.Succeeded : PublishAttemptStatus.Failed,
                ExternalPostId = outcome.ExternalPostId,
                ErrorMessage = outcome.ErrorMessage
            };

            return (channel, attempt, outcome);
        });

        var results = await Task.WhenAll(publishTasks);

        foreach (var (_, attempt, _) in results)
        {
            db.PublishAttempts.Add(attempt);
        }

        content.Status = results.Any(r => r.outcome.Success) ? ContentStatus.Published : ContentStatus.Failed;

        await db.SaveChangesAsync();

        var response = results.Select(r => new PublishAttemptResult(
            r.channel.Id, r.channel.DisplayName, r.outcome.Success, r.outcome.ExternalPostId, r.outcome.ErrorMessage));

        return Ok(response);
    }
}
