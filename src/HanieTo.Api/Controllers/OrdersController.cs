using HanieTo.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record OrderDto(int Id, string Buyer, decimal TotalPrice, string Status, DateTime? BuyDatetime);

// Read-only view of the bot's orders (see ShopCatalogDbContext). Orders are
// created and managed through the bot itself, not here.
[ApiController]
[Route("api/[controller]")]
public class OrdersController(ShopCatalogDbContext catalog) : ControllerBase
{
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
}
