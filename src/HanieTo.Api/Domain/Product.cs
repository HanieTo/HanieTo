namespace HanieTo.Api.Domain;

// A read-only view of one catalog listing, grouped from the bot's Postgres
// "items" (one row per sellable unit, since AiogramShopBot models digital
// goods that way - see Data/ShopCatalog). The bot's own admin panel is the
// only place this catalog is edited; this API only reads it.
public class Product
{
    public required string Name { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public double Price { get; set; }
    public int Stock { get; set; }
}
