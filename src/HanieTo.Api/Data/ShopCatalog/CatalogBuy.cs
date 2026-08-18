namespace HanieTo.Api.Data.ShopCatalog;

// Mirrors the bot's "buys" table. Read-only - see CatalogItem.
public class CatalogBuy
{
    public int Id { get; set; }
    public int? BuyerId { get; set; }
    public decimal TotalPrice { get; set; }
    public DateTime? BuyDatetime { get; set; }
    public string Status { get; set; } = "";
    public decimal Discount { get; set; }
    public string? ShippingAddress { get; set; }
    public string? TrackNumber { get; set; }
}
