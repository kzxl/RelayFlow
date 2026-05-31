using System.Diagnostics;

namespace RelayFlow.Diagnostics
{
    /// <summary>
    /// The <see cref="ActivitySource"/> RelayFlow uses for distributed tracing. Subscribe to
    /// <see cref="Name"/> in OpenTelemetry to capture relay spans with destination and outcome
    /// tags. A span is created per relayed request when there is an active listener.
    /// </summary>
    public static class RelayActivitySource
    {
        /// <summary>The activity source name to subscribe to.</summary>
        public const string Name = "RelayFlow";

        /// <summary>The shared activity source instance.</summary>
        public static readonly ActivitySource Source = new ActivitySource(Name, "0.2.0");
    }
}
