using HanieTo.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record LowStockProductDto(string Name, string? Category, string? Subcategory, int Stock);
public record FailedPublishDto(Guid ContentId, string ContentTitle, string ChannelName, string? ErrorMessage, DateTime AttemptedAtUtc);
public record OverviewDto(
    int TodayOrderCount,
    decimal TodayRevenue,
    IReadOnlyList<LowStockProductDto> LowStockProducts,
    IReadOnlyList<FailedPublishDto> RecentFailedPublishes);

// Home tab: the "is anything on fire" summary - today's orders/revenue, what's
// about to run out of stock, and what failed to publish, so those don't have
// to be discovered by clicking through the other tabs.
[ApiController]
[Route("api/[controller]")]
public class OverviewController(ShopCatalogDbContext catalog, AppDbContext db) : ControllerBase
{
    private const int LowStockThreshold = 3;
    private const int RecentFailuresLimit = 10;

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var todayUtc = DateTime.UtcNow.Date;
        var tomorrowUtc = todayUtc.AddDays(1);

        var todaysBuys = await catalog.Buys
            .Where(b => b.Status != "REFUNDED" && b.BuyDatetime >= todayUtc && b.BuyDatetime < tomorrowUtc)
            .ToListAsync();

        var lowStock = await (
                from item in catalog.Items
                join cat in catalog.Categories on item.CategoryId equals cat.Id
                join sub in catalog.Subcategories on item.SubcategoryId equals sub.Id
                select new { item, CategoryName = cat.Name, SubcategoryName = sub.Name })
            .GroupBy(x => new { x.item.Description, x.CategoryName, x.SubcategoryName })
            .Select(g => new LowStockProductDto(
                g.Key.Description,
                g.Key.CategoryName,
                g.Key.SubcategoryName,
                g.Count(x => !x.item.IsSold)))
            .Where(p => p.Stock <= LowStockThreshold)
            .OrderBy(p => p.Stock)
            .ToListAsync();

        var recentFailures = await db.PublishAttempts
            .Where(a => a.Status == Domain.PublishAttemptStatus.Failed)
            .OrderByDescending(a => a.AttemptedAtUtc)
            .Take(RecentFailuresLimit)
            .Join(db.Contents, a => a.ContentId, c => c.Id, (a, c) => new FailedPublishDto(
                c.Id, c.Title, a.Channel != null ? a.Channel.DisplayName : "unknown", a.ErrorMessage, a.AttemptedAtUtc))
            .ToListAsync();

        return Ok(new OverviewDto(
            todaysBuys.Count,
            todaysBuys.Sum(b => b.TotalPrice),
            lowStock,
            recentFailures));
    }
}
