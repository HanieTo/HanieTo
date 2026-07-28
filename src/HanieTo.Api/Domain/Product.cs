namespace HanieTo.Api.Domain;

public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int Stock { get; set; }
    public string? Category { get; set; }
    public string? PhotoUrl { get; set; }
    public bool IsActive { get; set; } = true;
}
