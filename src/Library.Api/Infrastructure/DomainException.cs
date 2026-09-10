namespace Library.Api.Infrastructure;

public sealed class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
