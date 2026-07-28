namespace HanieTo.Api.Domain;

public class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // The Telegram chat id of the buyer - how "My Orders" looks orders back up.
    public required string BuyerChatId { get; set; }

    public OrderStatus Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = [];

    public decimal Total => Items.Sum(i => i.UnitPrice * i.Quantity);
}
