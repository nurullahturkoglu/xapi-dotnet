using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Xapi.Testing;

// Records the measurements of the instruments that `include` selects. Linked into the test projects that check
// metrics; select by meter instance or scope, so tests running in parallel do not see each other's measurements.
internal sealed class MetricRecorder : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentQueue<RecordedMeasurement> _measurements = new();

    public MetricRecorder(Func<Instrument, bool> include)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (include(instrument))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _listener.Start();
    }

    public IReadOnlyList<RecordedMeasurement> All => [.. _measurements];

    public List<RecordedMeasurement> Of(string instrument) => [.. _measurements.Where(m => m.Instrument == instrument)];

    // Sums the measurements of a counter by their tags, written as RecordedMeasurement.TagText
    public Dictionary<string, double> Sums(string instrument) => Of(instrument)
        .GroupBy(m => m.TagText, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Sum(m => m.Value), StringComparer.Ordinal);

    // Runs the callbacks of the observable instruments and returns what they reported
    public List<RecordedMeasurement> Observe(string instrument)
    {
        var before = _measurements.Count;
        _listener.RecordObservableInstruments();
        return [.. _measurements.Skip(before).Where(m => m.Instrument == instrument)];
    }

    public void Dispose() => _listener.Dispose();

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            copy[tag.Key] = tag.Value;
        }

        _measurements.Enqueue(new RecordedMeasurement(instrument.Meter.Name, instrument.Name, instrument.Unit, value, copy));
    }
}

internal sealed record RecordedMeasurement(string Meter, string Instrument, string? Unit, double Value, IReadOnlyDictionary<string, object?> Tags)
{
    // "name=value" pairs sorted by name, for comparing a whole tag set in one assertion
    public string TagText => string.Join(", ", Tags.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => $"{t.Key}={t.Value}"));

    public object? this[string tag] => Tags.TryGetValue(tag, out var value) ? value : null;
}
