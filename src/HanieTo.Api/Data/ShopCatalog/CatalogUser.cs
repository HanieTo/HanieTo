namespace HanieTo.Api.Data.ShopCatalog;

// Mirrors the bot's "users" table. Read-only - see CatalogItem.
public class CatalogUser
{
    public int Id { get; set; }
    public string? TelegramUsername { get; set; }
    public long TelegramId { get; set; }
}
