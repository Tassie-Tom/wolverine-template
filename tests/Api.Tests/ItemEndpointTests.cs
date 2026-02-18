using Api.Data;
using Api.Domain.Entities;
using Api.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Api.Tests;

public class ItemEndpointTests
{
    private AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void Item_Create_SetsPropertiesCorrectly()
    {
        var userId = Guid.NewGuid();
        var item = Item.Create("Test Item", "A description", 9.99m, userId);

        Assert.Equal("Test Item", item.Name);
        Assert.Equal("A description", item.Description);
        Assert.Equal(9.99m, item.Price);
        Assert.Equal(userId, item.CreatedByUserId);
        Assert.NotEqual(Guid.Empty, item.Id);
        Assert.True(item.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Item_Create_WithNegativePrice_Throws()
    {
        var userId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() =>
            Item.Create("Test Item", null, -1m, userId));
    }

    [Fact]
    public void Item_Create_RaisesDomainEvent()
    {
        var userId = Guid.NewGuid();
        var item = Item.Create("Test Item", "Description", 5.00m, userId);

        Assert.Single(item.DomainEvents);
        var domainEvent = Assert.IsType<ItemCreated>(item.DomainEvents.First());
        Assert.Equal(item.Id, domainEvent.ItemId);
        Assert.Equal("Test Item", domainEvent.Name);
        Assert.Equal(5.00m, domainEvent.Price);
        Assert.Equal(userId, domainEvent.CreatedByUserId);
    }

    [Fact]
    public void Item_Update_ChangesProperties()
    {
        var item = Item.Create("Original", "Desc", 10m, Guid.NewGuid());

        item.Update("Updated", "New desc", 20m);

        Assert.Equal("Updated", item.Name);
        Assert.Equal("New desc", item.Description);
        Assert.Equal(20m, item.Price);
    }

    [Fact]
    public void Item_Update_WithNegativePrice_Throws()
    {
        var item = Item.Create("Test", null, 10m, Guid.NewGuid());

        Assert.Throws<ArgumentException>(() =>
            item.Update("Test", null, -5m));
    }

    [Fact]
    public async Task AppDbContext_CanSaveAndRetrieveItem()
    {
        using var db = CreateInMemoryDb();

        var user = User.Create(Guid.NewGuid(), "test@example.com", "Test", "User");
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var item = Item.Create("DB Test Item", "Stored in DB", 15.50m, user.Id);
        db.Items.Add(item);
        await db.SaveChangesAsync();

        var retrieved = await db.Items.FindAsync(item.Id);

        Assert.NotNull(retrieved);
        Assert.Equal("DB Test Item", retrieved.Name);
        Assert.Equal(15.50m, retrieved.Price);
        Assert.Equal(user.Id, retrieved.CreatedByUserId);
    }

    [Fact]
    public void User_Create_WithExplicitId_SetsIdCorrectly()
    {
        var id = Guid.NewGuid();
        var user = User.Create(id, "test@example.com", "Test", "User");

        Assert.Equal(id, user.Id);
        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("Test", user.FirstName);
        Assert.Equal("User", user.LastName);
    }
}
