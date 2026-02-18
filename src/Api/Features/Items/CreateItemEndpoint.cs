using Api.Data;
using Api.Domain.Entities;
using Api.Domain.Events;
using Api.Extensions;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Wolverine.Http;

namespace Api.Features.Items;

public record CreateItemRequest(
    string Name,
    string? Description,
    decimal Price
)
{
    public class Validator : AbstractValidator<CreateItemRequest>
    {
        public Validator()
        {
            RuleFor(x => x.Name)
                .NotEmpty()
                .WithMessage("Name is required")
                .MaximumLength(200)
                .WithMessage("Name must not exceed 200 characters");

            RuleFor(x => x.Description)
                .MaximumLength(2000)
                .WithMessage("Description must not exceed 2000 characters")
                .When(x => !string.IsNullOrEmpty(x.Description));

            RuleFor(x => x.Price)
                .GreaterThanOrEqualTo(0)
                .WithMessage("Price cannot be negative");
        }
    }
}

public record ItemResponse(
    Guid Id,
    string Name,
    string? Description,
    decimal Price,
    Guid CreatedByUserId,
    DateTime CreatedAt
);

public static class CreateItemEndpoint
{
    public static async Task<User?> LoadAsync(
        HttpContext context,
        AppDbContext db)
    {
        var userId = context.User.GetUserId();
        if (userId == null) return null;

        return await db.Users.FindAsync(userId.Value);
    }

    [Authorize]
    [Tags("Items")]
    [WolverinePost("/api/v1/items")]
    public static (Created<ItemResponse>, ItemCreated) Handle(
        CreateItemRequest request,
        User owner,
        AppDbContext db,
        ILogger logger)
    {
        var item = Item.Create(
            request.Name,
            request.Description,
            request.Price,
            owner.Id
        );

        db.Items.Add(item);

        var response = new ItemResponse(
            item.Id,
            item.Name,
            item.Description,
            item.Price,
            item.CreatedByUserId,
            item.CreatedAt
        );

        var domainEvent = (ItemCreated)item.DomainEvents.First();

        logger.LogInformation("Item {ItemId} created: \"{Name}\" by user {UserId}",
            item.Id, item.Name, owner.Id);

        return (
            TypedResults.Created($"/api/v1/items/{item.Id}", response),
            domainEvent
        );
    }
}
