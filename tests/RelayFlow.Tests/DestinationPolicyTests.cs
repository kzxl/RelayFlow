using System;
using RelayFlow.Security;
using Xunit;

namespace RelayFlow.Tests
{
    /// <summary>Tests for the SSRF allow-list policy. This is security-critical.</summary>
    public class DestinationPolicyTests
    {
        [Fact]
        public void Empty_DeniesEverything()
        {
            var policy = new DestinationPolicy();
            var decision = policy.Evaluate(new Uri("https://internal:5000/x"));
            Assert.False(decision.IsAllowed);
            Assert.Equal("no_allowed_origins_configured", decision.Reason);
        }

        [Fact]
        public void ExactOrigin_AllowsMatching_DeniesOthers()
        {
            var policy = new DestinationPolicy().AllowOrigin("https://internal-api:5000");

            Assert.True(policy.Evaluate(new Uri("https://internal-api:5000/orders/1")).IsAllowed);
            // Different host.
            Assert.False(policy.Evaluate(new Uri("https://evil.com:5000/orders/1")).IsAllowed);
            // Different port.
            Assert.False(policy.Evaluate(new Uri("https://internal-api:6000/orders/1")).IsAllowed);
            // Different scheme.
            Assert.False(policy.Evaluate(new Uri("http://internal-api:5000/orders/1")).IsAllowed);
        }

        [Fact]
        public void NonHttpScheme_IsAlwaysDenied()
        {
            var policy = new DestinationPolicy().AllowOrigin("https://internal-api:5000");
            // Classic SSRF vectors.
            Assert.False(policy.Evaluate(new Uri("file:///etc/passwd")).IsAllowed);
            Assert.False(policy.Evaluate(new Uri("ftp://internal-api:5000/x")).IsAllowed);
        }

        [Fact]
        public void DefaultPort_IsInferredFromScheme()
        {
            var policy = new DestinationPolicy().AllowOrigin("https", "api.internal");
            Assert.True(policy.Evaluate(new Uri("https://api.internal/x")).IsAllowed);   // 443
            Assert.True(policy.Evaluate(new Uri("https://api.internal:443/x")).IsAllowed);
            Assert.False(policy.Evaluate(new Uri("https://api.internal:8443/x")).IsAllowed);
        }

        [Fact]
        public void WildcardSubdomain_MatchesSubdomains_NotApex()
        {
            var policy = new DestinationPolicy().AllowOrigin("https", "*.internal.example.com");

            Assert.True(policy.Evaluate(new Uri("https://orders.internal.example.com/x")).IsAllowed);
            Assert.True(policy.Evaluate(new Uri("https://a.b.internal.example.com/x")).IsAllowed);
            // The bare apex must NOT match "*.".
            Assert.False(policy.Evaluate(new Uri("https://internal.example.com/x")).IsAllowed);
            // A look-alike suffix must not match.
            Assert.False(policy.Evaluate(new Uri("https://evilinternal.example.com/x")).IsAllowed);
        }

        [Fact]
        public void MultipleOrigins_AnyMatchAllows()
        {
            var policy = new DestinationPolicy()
                .AllowOrigin("https://orders:5000")
                .AllowOrigin("https://billing:5001");

            Assert.True(policy.Evaluate(new Uri("https://orders:5000/x")).IsAllowed);
            Assert.True(policy.Evaluate(new Uri("https://billing:5001/y")).IsAllowed);
            Assert.False(policy.Evaluate(new Uri("https://shipping:5002/z")).IsAllowed);
        }

        [Fact]
        public void AllowOrigin_RejectsNonAbsoluteUrl()
        {
            var policy = new DestinationPolicy();
            Assert.Throws<ArgumentException>(() => policy.AllowOrigin("/relative/path"));
        }
    }
}
