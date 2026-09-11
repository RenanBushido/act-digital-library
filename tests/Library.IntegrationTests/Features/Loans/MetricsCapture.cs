using System.Diagnostics.Metrics;

namespace Library.IntegrationTests.Features.Loans;

// Captura as medições publicadas por um `Meter` específico durante a janela de vida do objeto,
// filtrando pela instância exata (não pelo nome) para não misturar medições de outro host de
// teste que registre um `Meter` homônimo.
internal sealed class MetricsCapture : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly List<(string InstrumentName, double Value, IReadOnlyDictionary<string, object?> Tags)> _measurements = [];
    private readonly object _lock = new();

    public MetricsCapture(Meter meter)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter == meter)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags));

        _listener.Start();
    }

    public IReadOnlyList<(string InstrumentName, double Value, IReadOnlyDictionary<string, object?> Tags)> Measurements
    {
        get
        {
            lock (_lock)
            {
                return [.. _measurements];
            }
        }
    }

    public int CountOf(string instrumentName, Func<IReadOnlyDictionary<string, object?>, bool>? tagPredicate = null) =>
        Measurements.Count(m => m.InstrumentName == instrumentName && (tagPredicate is null || tagPredicate(m.Tags)));

    public double SumOf(string instrumentName) => Measurements.Where(m => m.InstrumentName == instrumentName).Sum(m => m.Value);

    private void Record(string instrumentName, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var tagDictionary = new Dictionary<string, object?>();
        foreach (var tag in tags)
        {
            tagDictionary[tag.Key] = tag.Value;
        }

        lock (_lock)
        {
            _measurements.Add((instrumentName, value, tagDictionary));
        }
    }

    public void Dispose() => _listener.Dispose();
}
