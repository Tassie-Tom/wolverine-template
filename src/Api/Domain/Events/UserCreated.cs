namespace Api.Domain.Events;

public record UserCreated(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    DateTime CreatedAt
);
