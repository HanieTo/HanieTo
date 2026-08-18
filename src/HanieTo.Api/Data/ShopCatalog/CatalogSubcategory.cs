namespace HanieTo.Api.Data.ShopCatalog;

// Mirrors the bot's "subcategories" table. Read-only - see CatalogItem.
public class CatalogSubcategory
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}
