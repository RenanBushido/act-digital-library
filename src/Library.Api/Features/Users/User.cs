namespace Library.Api.Features.Users;

public sealed class User
{
    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public Email Email { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    private User(Guid id, string name, Email email, DateTimeOffset createdAtUtc, DateTimeOffset updatedAtUtc)
    {
        Id = id;
        Name = name;
        Email = email;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public static User Create(string name, string email, TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Nome não pode ser vazio.");
        }

        var normalizedEmail = Email.Create(email);
        var now = timeProvider.GetUtcNow();

        return new User(Guid.NewGuid(), name.Trim(), normalizedEmail, now, now);
    }
}
