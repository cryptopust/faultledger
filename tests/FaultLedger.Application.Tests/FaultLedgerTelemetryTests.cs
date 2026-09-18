using System.Diagnostics;
using System.Diagnostics.Metrics;
using FaultLedger.Application.Diagnostics;

namespace FaultLedger.Application.Tests;

public sealed class FaultLedgerTelemetryTests
{
    [Fact]
    public void Metrics_AreNamedAndRecordableWithoutAnExporter()
    {
        using var listener = new MeterListener();
        long observed = 0;
        listener.InstrumentPublished = (instrument, meterListener) =>
        {
            if (instrument.Meter.Name == FaultLedgerTelemetry.MeterName)
            {
                meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) =>
        {
            if (instrument.Name == "faultledger_unknown_outcomes_total")
            {
                observed += measurement;
            }
        });
        listener.Start();

        FaultLedgerTelemetry.UnknownOutcomes.Add(1);

        Assert.Equal(1, observed);
    }

    [Fact]
    public void CustomActivity_UsesStableNameAndSafeAttributes()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == FaultLedgerTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using Activity? activity = FaultLedgerTelemetry.ActivitySource.StartActivity("faultledger.outbox.dispatch");
        activity?.SetTag("faultledger.outbox.id", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));

        Assert.NotNull(activity);
        Assert.Equal("faultledger.outbox.dispatch", activity.OperationName);
        Assert.DoesNotContain(activity.Tags, tag => tag.Key.Contains("secret", StringComparison.OrdinalIgnoreCase));
    }
}
