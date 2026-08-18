namespace HanieTo.Api.Data.ShopCatalog;

// Mirrors the bot's "categories" table. Read-only - see CatalogItem.
public class CatalogCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
