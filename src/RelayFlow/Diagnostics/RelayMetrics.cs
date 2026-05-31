using System;
using System.Diagnostics.Metrics;

namespace RelayFlow.Diagnostics
{
    /// <summary>
    /// Emits RelayFlow metrics via <see cref="System.Diagnostics.Metrics"/>, so they can be
    /// collected by OpenTelemetry or <c>dotnet-counters</c> without any RelayFlow-specific
    /// dependency. The meter name is <see cref="MeterName"/>.
    /// </summary>
    public sealed class RelayMetrics : IDisposable
    {
        /// <summary>The meter name to subscribe to (e.g. in OpenTelemetry).</summary>
        public const string MeterName = "RelayFlow";

        private readonly Meter _meter;
        private readonly Counter<long> _requests;
        private readonly Counter<long> _rejected;
        private readonly Histogram<double> _duration;

        /// <summary>Creates the metrics instruments. Registered as a singleton.</summary>
        public RelayMetrics()
        {
            _meter = new Meter(MeterName, "0.2.0");

            _requests = _meter.CreateCounter<long>(
                "relayflow.requests",
                unit: "{request}",
                description: "Number of relayed requests by outcome.");

            _rejected = _meter.CreateCounter<long>(
                "relayflow.rejected",
                unit: "{request}",
                description: "Requests rejected before forwarding (SSRF deny, open circuit).");

            _duration = _meter.CreateHistogram<double>(
                "relayflow.duration",
                unit: "ms",
                description: "Duration of relayed requests in milliseconds.");
        }

        /// <summary>Records a completed forward with its outcome and duration.</summary>
        public void RecordForwarded(string destinationAuthority, int statusCode, double elapsedMs)
        {
            var dest = new System.Collections.Generic.KeyValuePair<string, object?>("destination", destinationAuthority);
            var status = new System.Collections.Generic.KeyValuePair<string, object?>("status_code", statusCode);
            _requests.Add(1, dest, status);
            _duration.Record(elapsedMs, dest, status);
        }

        /// <summary>Records a request rejected before forwarding (with a reason tag).</summary>
        public void RecordRejected(string reason)
        {
            _rejected.Add(1, new System.Collections.Generic.KeyValuePair<string, object?>("reason", reason));
        }

        /// <inheritdoc />
        public void Dispose() => _meter.Dispose();
    }
}
