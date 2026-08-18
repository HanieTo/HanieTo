using HanieTo.Api.Catalog;
using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

public record CreateProductDashboardRequest(
    string ItemType, string Category, string Subcategory, string Description,
    double Price, int Quantity, IReadOnlyList<string>? DigitalCodes);

public record UpdateProductPriceDashboardRequest(
    string Category, string Subcategory, string Description, double NewPrice);

// Reads go straight to the bot's Postgres catalog (see ShopCatalogDbContext -
// this is deliberately read-only). Writes are forwarded to the bot's own
// internal API (see ShopBotAdminClient) so the bot's persistence rules never
// get duplicated here.
[ApiController]
[Route("api/[controller]")]
public class ProductsController(ShopCatalogDbContext catalog, ShopBotAdminClient shopBot) : ControllerBase
{
    private static readonly string[] ValidItemTypes = ["DIGITAL", "PHYSICAL"];

    [HttpPost]
    public async Task<IActionResult> Create(CreateProductDashboardRequest request, CancellationToken ct)
    {
        var itemType = request.ItemType?.ToUpperInvariant();
        if (itemType is null || !ValidItemTypes.Contains(itemType))
        {
            return BadRequest($"ItemType must be one of: {string.Join(", ", ValidItemTypes)}.");
        }

        if (string.IsNullOrWhiteSpace(request.Category) || string.IsNullOrWhiteSpace(request.Subcategory) ||
            string.IsNullOrWhiteSpace(request.Description))
        {
            return BadRequest("Category, Subcategory, and Description are all required.");
        }

        try
        {
            var result = await shopBot.CreateProductAsync(
                new CreateProductRequest(
                    itemType, request.Category, request.Subcategory, request.Description,
                    request.Price, request.Quantity, request.DigitalCodes),
                ct);
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

    [HttpPatch("price")]
    public async Task<IActionResult> UpdatePrice(UpdateProductPriceDashboardRequest request, CancellationToken ct)
    {
        try
        {
            var result = await shopBot.UpdateProductPriceAsync(
                new UpdateProductPriceRequest(
                    request.Category, request.Subcategory, request.Description, request.NewPrice),
                ct);
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

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? category)
    {
        var itemsQuery =
            from item in catalog.Items
            join cat in catalog.Categories on item.CategoryId equals cat.Id
            join sub in catalog.Subcategories on item.SubcategoryId equals sub.Id
            select new { item, CategoryName = cat.Name, SubcategoryName = sub.Name };

        if (!string.IsNullOrWhiteSpace(category))
        {
            itemsQuery = itemsQuery.Where(x => x.CategoryName == category);
        }

        var products = await itemsQuery
            .GroupBy(x => new { x.item.Description, x.item.Price, x.CategoryName, x.SubcategoryName })
            .Select(g => new Product
            {
                Name = g.Key.Description,
                Category = g.Key.CategoryName,
                Subcategory = g.Key.SubcategoryName,
                Price = g.Key.Price,
                Stock = g.Count(x => !x.item.IsSold)
            })
            .ToListAsync();

        return Ok(products);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
    {
        var categories = await catalog.Categories.Select(c => c.Name).ToListAsync();
        return Ok(categories);
    }
}
