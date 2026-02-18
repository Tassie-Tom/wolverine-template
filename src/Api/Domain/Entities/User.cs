namespace Api.Domain.Entities;

public class User : BaseEntity
{
    private User() { }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }

    public static User Create(string email, string firstName, string lastName)
    {
        return Create(Guid.NewGuid(), email, firstName, lastName);
    }

    public static User Create(Guid id, string email, string firstName, string lastName)
    {
        var user = new User
        {
            Id = id,
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            CreatedAt = DateTime.UtcNow
        };

        return user;
    }
}
