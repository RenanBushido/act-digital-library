namespace Library.Api.Features.Loans;

public sealed class LoanMetrics : IDisposable
{
    public const string MeterName = "Library.Loans";

    public const string ReasonUnavailable = "unavailable";
    public const string ReasonBookInactive = "book_inactive";
    public const string ReasonBookNotFound = "book_not_found";
    public const string ReasonUserNotFound = "user_not_found";

    public const string OutcomeCreated = "created";
    public const string OutcomeReplayed = "replayed";
    public const string OutcomeRejected = "rejected";

    private readonly Meter _meter;

    // Exposto só para o `MeterListener` dos testes de integração filtrar pela instância exata
    // deste host, em vez de por nome (dois hosts de teste concorrentes teriam Meters homônimos).
    public Meter Meter => _meter;

    private readonly Counter<long> _created;
    private readonly Counter<long> _rejected;
    private readonly Counter<long> _idempotentReplays;
    private readonly Histogram<double> _createDuration;

    public LoanMetrics()
    {
        _meter = new Meter(MeterName);
        _created = _meter.CreateCounter<long>("library.loans.created");
        _rejected = _meter.CreateCounter<long>("library.loans.rejected");
        _idempotentReplays = _meter.CreateCounter<long>("library.loans.idempotent_replays");
        _createDuration = _meter.CreateHistogram<double>("library.loans.create.duration", unit: "ms");
    }

    public void RecordCreated() => _created.Add(1);

    public void RecordRejected(string reason) => _rejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordIdempotentReplay() => _idempotentReplays.Add(1);

    public void RecordCreateDuration(double milliseconds, string outcome) =>
        _createDuration.Record(milliseconds, new KeyValuePair<string, object?>("outcome", outcome));

    public void Dispose() => _meter.Dispose();
}
