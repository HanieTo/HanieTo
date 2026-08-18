using HanieTo.Api.Catalog;
using HanieTo.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record OrderDto(int Id, string Buyer, decimal TotalPrice, string Status, DateTime? BuyDatetime);

public record UpdateOrderStatusDashboardRequest(string Status, string? TrackNumber);

// Reads go straight to the bot's Postgres orders table (see ShopCatalogDbContext
// - deliberately read-only). Status changes and refunds are forwarded to the
// bot's own internal API (see ShopBotAdminClient) so refund bookkeeping (giving
// stock back, adjusting the buyer's spend total, notifying admins) only lives
// in one place - the bot's existing BuyService.refund.
[ApiController]
[Route("api/[controller]")]
public class OrdersController(ShopCatalogDbContext catalog, ShopBotAdminClient shopBot) : ControllerBase
{
    private static readonly string[] ValidStatuses = ["PAID", "SHIPPED", "DELIVERED", "COMPLETED"];

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orders = await (
            from buy in catalog.Buys
            join user in catalog.Users on buy.BuyerId equals user.Id into buyerJoin
            from buyer in buyerJoin.DefaultIfEmpty()
            orderby buy.BuyDatetime descending
            select new OrderDto(
                buy.Id,
                buyer != null ? (buyer.TelegramUsername ?? buyer.TelegramId.ToString()) : "unknown",
                buy.TotalPrice,
                buy.Status,
                buy.BuyDatetime)
        ).ToListAsync();

        return Ok(orders);
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateOrderStatusDashboardRequest request, CancellationToken ct)
    {
        var status = request.Status?.ToUpperInvariant();
        if (status is null || !ValidStatuses.Contains(status))
        {
            return BadRequest($"Status must be one of: {string.Join(", ", ValidStatuses)}. Use /refund to refund an order.");
        }

        try
        {
            var result = await shopBot.UpdateOrderStatusAsync(id, status, request.TrackNumber, ct);
            return Ok(result);
        }
        catch (ShopBotAdminClientNotConfiguredException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (ShopBotAdminApiException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }

    [HttpPost("{id:int}/refund")]
    public async Task<IActionResult> Refund(int id, CancellationToken ct)
    {
        try
        {
            var result = await shopBot.RefundOrderAsync(id, ct);
            return Ok(result);
        }
        catch (ShopBotAdminClientNotConfiguredException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, ex.Message);
        }
        catch (ShopBotAdminApiException ex)
        {
            return StatusCode((int)ex.StatusCode, ex.Message);
        }
    }
}
