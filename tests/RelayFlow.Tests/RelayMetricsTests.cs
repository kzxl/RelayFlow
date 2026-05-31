using System.Collections.Generic;
using System.Diagnostics.Metrics;
using RelayFlow.Diagnostics;
using Xunit;

namespace RelayFlow.Tests
{
    /// <summary>
    /// Verifies RelayFlow emits metrics through the standard <see cref="Meter"/> API so they are
    /// collectable by OpenTelemetry / dotnet-counters.
    /// </summary>
    public class RelayMetricsTests
    {
        [Fact]
        public void RecordForwarded_EmitsRequestCount_AndDuration()
        {
            long requestCount = 0;
            bool sawDuration = false;

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == RelayMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((inst, value, tags, state) =>
            {
                if (inst.Name == "relayflow.requests") requestCount += value;
            });
            listener.SetMeasurementEventCallback<double>((inst, value, tags, state) =>
            {
                if (inst.Name == "relayflow.duration") sawDuration = true;
            });
            listener.Start();

            // Create the metrics AFTER the listener so InstrumentPublished fires for it.
            using var metrics = new RelayMetrics();
            metrics.RecordForwarded("https://internal:5000", 200, 12.5);

            listener.RecordObservableInstruments();

            Assert.Equal(1, requestCount);
            Assert.True(sawDuration);
        }

        [Fact]
        public void RecordRejected_EmitsRejectedCount()
        {
            long rejected = 0;

            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == RelayMetrics.MeterName)
                    l.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((inst, value, tags, state) =>
            {
                if (inst.Name == "relayflow.rejected") rejected += value;
            });
            listener.Start();

            using var metrics = new RelayMetrics();
            metrics.RecordRejected("ssrf_denied");

            Assert.Equal(1, rejected);
        }
    }
}
