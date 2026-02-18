namespace Api.Domain.Events;

public record ItemCreated(
    Guid ItemId,
    string Name,
    string? Description,
    decimal Price,
    Guid CreatedByUserId,
    DateTime CreatedAt
);
