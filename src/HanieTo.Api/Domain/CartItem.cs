namespace HanieTo.Api.Domain;

// A product a customer has added to their cart but not yet checked out. Keyed by
// the Telegram chat id so each customer has their own persistent cart.
public class CartItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ChatId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
