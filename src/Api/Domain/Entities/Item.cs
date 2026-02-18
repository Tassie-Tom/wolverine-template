using Api.Domain.Events;

namespace Api.Domain.Entities;

public class Item : BaseEntity
{
    private Item() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Navigation properties
    public User CreatedBy { get; private set; } = null!;

    public static Item Create(string name, string? description, decimal price, Guid createdByUserId)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Price = price,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow
        };

        item.RaiseDomainEvent(new ItemCreated(
            item.Id,
            item.Name,
            item.Description,
            item.Price,
            item.CreatedByUserId,
            item.CreatedAt
        ));

        return item;
    }

    public void Update(string name, string? description, decimal price)
    {
        if (price < 0)
            throw new ArgumentException("Price cannot be negative", nameof(price));

        Name = name;
        Description = description;
        Price = price;
    }
}
