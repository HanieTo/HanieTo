namespace HanieTo.Api.Data.ShopCatalog;

// Mirrors the bot's "items" table (AiogramShopBot, in shopbot/models/item.py).
// The bot owns this schema via its own Alembic migrations - this API only
// reads it, never migrates or writes to it. Deliberately excludes
// `private_data`: that's the per-unit fulfillment secret (e.g. a license
// key), revealed to the buyer after purchase, and must never be exposed here.
public class CatalogItem
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public int SubcategoryId { get; set; }
    public double Price { get; set; }
    public bool IsSold { get; set; }
    public string Description { get; set; } = "";
}
