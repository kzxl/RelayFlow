using System;
using RelayFlow.Resilience;
using Xunit;

namespace RelayFlow.Tests
{
    /// <summary>
    /// Tests for the circuit breaker state machine. A manual clock makes cooldown timing
    /// deterministic (the breaker measures cooldown in the clock's own ticks when a custom clock
    /// is supplied).
    /// </summary>
    public class CircuitBreakerTests
    {
        private long _now;
        private Func<long> Clock => () => _now;

        private CircuitBreaker NewBreaker(int threshold = 3, long cooldownTicks = 100)
            => new CircuitBreaker(threshold, TimeSpan.FromTicks(cooldownTicks), Clock);

        [Fact]
        public void StartsClosed_AndAllows()
        {
            var cb = NewBreaker();
            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.True(cb.TryAcquire());
        }

        [Fact]
        public void Trips_AfterConsecutiveFailures()
        {
            var cb = NewBreaker(threshold: 3);
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Closed, cb.State); // 2 < 3
            cb.RecordFailure();
            Assert.Equal(CircuitState.Open, cb.State);   // 3 == threshold
        }

        [Fact]
        public void Success_ResetsFailureCount()
        {
            var cb = NewBreaker(threshold: 3);
            cb.RecordFailure();
            cb.RecordFailure();
            cb.RecordSuccess(); // resets
            cb.RecordFailure();
            cb.RecordFailure();
            Assert.Equal(CircuitState.Closed, cb.State); // only 2 since reset
        }

        [Fact]
        public void Open_RejectsUntilCooldownElapses()
        {
            var cb = NewBreaker(threshold: 1, cooldownTicks: 100);
            cb.RecordFailure(); // trips
            Assert.Equal(CircuitState.Open, cb.State);

            // Before cooldown: rejected.
            _now += 50;
            Assert.False(cb.TryAcquire());

            // After cooldown: allows a single trial and moves to half-open.
            _now += 60; // total 110 >= 100
            Assert.True(cb.TryAcquire());
            Assert.Equal(CircuitState.HalfOpen, cb.State);
        }

        [Fact]
        public void HalfOpen_SuccessClosesCircuit()
        {
            var cb = NewBreaker(threshold: 1, cooldownTicks: 100);
            cb.RecordFailure();
            _now += 100;
            Assert.True(cb.TryAcquire()); // half-open trial
            cb.RecordSuccess();
            Assert.Equal(CircuitState.Closed, cb.State);
            Assert.True(cb.TryAcquire());
        }

        [Fact]
        public void HalfOpen_FailureReopensCircuit()
        {
            var cb = NewBreaker(threshold: 1, cooldownTicks: 100);
            cb.RecordFailure();
            _now += 100;
            Assert.True(cb.TryAcquire()); // half-open trial
            cb.RecordFailure();           // trial fails
            Assert.Equal(CircuitState.Open, cb.State);

            // Immediately after re-open, rejected again.
            Assert.False(cb.TryAcquire());
        }

        [Fact]
        public void HalfOpen_OnlyOneTrialAtATime()
        {
            var cb = NewBreaker(threshold: 1, cooldownTicks: 100);
            cb.RecordFailure();
            _now += 100;

            Assert.True(cb.TryAcquire());   // first trial acquires
            Assert.False(cb.TryAcquire());  // second is blocked while trial in flight
        }

        [Fact]
        public void Registry_IsolatesBreakersPerAuthority()
        {
            var registry = new CircuitBreakerRegistry(
                new CircuitBreakerOptions { FailureThreshold = 1, Cooldown = TimeSpan.FromSeconds(10) });

            var a = registry.GetBreaker(new Uri("https://a:5000/x"));
            var b = registry.GetBreaker(new Uri("https://b:5000/y"));
            Assert.NotSame(a, b);

            // Same authority returns the same breaker instance.
            var a2 = registry.GetBreaker(new Uri("https://a:5000/different/path"));
            Assert.Same(a, a2);
        }
    }
}
