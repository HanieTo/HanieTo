using HanieTo.Api.Data;
using HanieTo.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HanieTo.Api.Controllers;

// Read-only view of the bot's product catalog (see ShopCatalogDbContext).
// Products are managed in the bot's own admin panel, not here.
[ApiController]
[Route("api/[controller]")]
public class ProductsController(ShopCatalogDbContext catalog) : ControllerBase
{
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
