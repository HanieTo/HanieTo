namespace HanieTo.Api.Domain;

// A read-only view of one catalog listing, grouped from the bot's Postgres
// "items" (one row per sellable unit, since AiogramShopBot models digital
// goods that way - see Data/ShopCatalog). This model itself is read straight
// from Postgres and never written here; edits go through ShopBotAdminClient,
// which calls back into the bot so its own persistence rules stay in one place.
public class Product
{
    public required string Name { get; set; }
    public string? Category { get; set; }
    public string? Subcategory { get; set; }
    public double Price { get; set; }
    public int Stock { get; set; }
}
