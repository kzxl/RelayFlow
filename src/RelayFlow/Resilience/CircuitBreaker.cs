using System;
using System.Threading;

namespace RelayFlow.Resilience
{
    /// <summary>The state of a <see cref="CircuitBreaker"/>.</summary>
    public enum CircuitState
    {
        /// <summary>Requests flow normally; failures are counted.</summary>
        Closed,

        /// <summary>The circuit is tripped; requests are rejected fast until the cooldown elapses.</summary>
        Open,

        /// <summary>A single trial request is allowed to probe whether the destination recovered.</summary>
        HalfOpen
    }

    /// <summary>
    /// A thread-safe circuit breaker for a single destination. It trips to
    /// <see cref="CircuitState.Open"/> after a configured number of consecutive failures, rejects
    /// requests for a cooldown window, then allows a single trial in
    /// <see cref="CircuitState.HalfOpen"/>: success closes the circuit, failure re-opens it.
    /// <para>
    /// The breaker holds no timers; state transitions are evaluated lazily on each
    /// <see cref="TryAcquire"/> using a monotonic clock, so it is cheap and allocation-free on
    /// the hot path.
    /// </para>
    /// </summary>
    public sealed class CircuitBreaker
    {
        private readonly int _failureThreshold;
        private readonly long _cooldownTicks;
        private readonly Func<long> _clock;

        private readonly object _gate = new object();
        private CircuitState _state = CircuitState.Closed;
        private int _consecutiveFailures;
        private long _openedAtTicks;
        private bool _trialInFlight;

        /// <summary>
        /// Creates a circuit breaker.
        /// </summary>
        /// <param name="failureThreshold">Consecutive failures that trip the circuit (min 1).</param>
        /// <param name="cooldown">How long the circuit stays open before allowing a trial.</param>
        /// <param name="clock">
        /// Optional monotonic clock returning timestamp ticks (for tests). Defaults to
        /// <see cref="Environment.TickCount64"/>-based timing.
        /// </param>
        public CircuitBreaker(int failureThreshold, TimeSpan cooldown, Func<long>? clock = null)
        {
            if (failureThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Must be at least 1.");
            if (cooldown < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(cooldown), "Cooldown cannot be negative.");

            _failureThreshold = failureThreshold;
            // Use the same unit as the clock (milliseconds by default).
            _clock = clock ?? (() => Environment.TickCount64);
            _cooldownTicks = clock == null ? (long)cooldown.TotalMilliseconds : (long)cooldown.Ticks;
        }

        /// <summary>The current state (evaluated without advancing transitions).</summary>
        public CircuitState State
        {
            get { lock (_gate) return _state; }
        }

        /// <summary>
        /// Attempts to acquire permission to send a request. Returns true if the request may
        /// proceed (closed, or the single half-open trial). When it returns true in the
        /// half-open state, the caller MUST report the outcome via
        /// <see cref="RecordSuccess"/>/<see cref="RecordFailure"/> so the trial is released.
        /// </summary>
        public bool TryAcquire()
        {
            lock (_gate)
            {
                switch (_state)
                {
                    case CircuitState.Closed:
                        return true;

                    case CircuitState.Open:
                        // Has the cooldown elapsed? If so, move to half-open and allow one trial.
                        if (_clock() - _openedAtTicks >= _cooldownTicks)
                        {
                            _state = CircuitState.HalfOpen;
                            _trialInFlight = true;
                            return true;
                        }
                        return false;

                    case CircuitState.HalfOpen:
                        // Only one trial at a time.
                        if (!_trialInFlight)
                        {
                            _trialInFlight = true;
                            return true;
                        }
                        return false;

                    default:
                        return false;
                }
            }
        }

        /// <summary>Records a successful outcome, closing the circuit and resetting counters.</summary>
        public void RecordSuccess()
        {
            lock (_gate)
            {
                _consecutiveFailures = 0;
                _trialInFlight = false;
                _state = CircuitState.Closed;
            }
        }

        /// <summary>
        /// Records a failed outcome. In closed state, trips to open when the threshold is reached.
        /// In half-open state, immediately re-opens and restarts the cooldown.
        /// </summary>
        public void RecordFailure()
        {
            lock (_gate)
            {
                if (_state == CircuitState.HalfOpen)
                {
                    Trip();
                    return;
                }

                _consecutiveFailures++;
                if (_consecutiveFailures >= _failureThreshold)
                {
                    Trip();
                }
            }
        }

        private void Trip()
        {
            _state = CircuitState.Open;
            _openedAtTicks = _clock();
            _trialInFlight = false;
            // Keep the failure count saturated at the threshold.
            _consecutiveFailures = _failureThreshold;
        }
    }
}
