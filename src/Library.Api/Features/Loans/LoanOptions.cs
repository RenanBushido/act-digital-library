namespace Library.Api.Features.Loans;

public sealed class LoanOptions
{
    public const string SectionName = "Loans";

    public int DueDays { get; set; } = 14;
}
