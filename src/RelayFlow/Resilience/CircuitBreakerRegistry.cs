using System;
using System.Collections.Concurrent;

namespace RelayFlow.Resilience
{
    /// <summary>
    /// Options controlling the per-destination circuit breaker.
    /// </summary>
    public sealed class CircuitBreakerOptions
    {
        /// <summary>Whether the circuit breaker is enabled. Default true.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Consecutive upstream failures that trip the circuit. Default 5.</summary>
        public int FailureThreshold { get; set; } = 5;

        /// <summary>How long the circuit stays open before allowing a trial request. Default 10s.</summary>
        public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Maintains one <see cref="CircuitBreaker"/> per destination authority (scheme://host:port),
    /// so a failing internal service trips independently of healthy ones.
    /// </summary>
    public sealed class CircuitBreakerRegistry
    {
        private readonly CircuitBreakerOptions _options;
        private readonly ConcurrentDictionary<string, CircuitBreaker> _breakers =
            new ConcurrentDictionary<string, CircuitBreaker>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<long>? _clock;

        /// <summary>Creates the registry from options.</summary>
        public CircuitBreakerRegistry(CircuitBreakerOptions options, Func<long>? clock = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock;
        }

        /// <summary>True when breaking is enabled.</summary>
        public bool Enabled => _options.Enabled;

        /// <summary>
        /// Gets (or creates) the breaker for a destination's authority, e.g.
        /// <c>https://internal-api:5000</c>.
        /// </summary>
        public CircuitBreaker GetBreaker(Uri destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            string key = destination.GetLeftPart(UriPartial.Authority);
            return _breakers.GetOrAdd(key, _ =>
                new CircuitBreaker(_options.FailureThreshold, _options.Cooldown, _clock));
        }
    }
}
