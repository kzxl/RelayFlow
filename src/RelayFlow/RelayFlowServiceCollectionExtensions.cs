using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RelayFlow.Configuration;
using RelayFlow.Diagnostics;
using RelayFlow.Forwarding;
using RelayFlow.Resilience;
using Yarp.ReverseProxy.Forwarder;

namespace RelayFlow
{
    /// <summary>
    /// Dependency-injection extensions for registering RelayFlow.
    /// </summary>
    public static class RelayFlowServiceCollectionExtensions
    {
        /// <summary>
        /// Registers RelayFlow services: the configured <see cref="RelayOptions"/>, YARP's
        /// <see cref="IHttpForwarder"/>, the shared upstream HTTP client, and the
        /// <see cref="RelayForwarder"/> engine.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="configure">
        /// Configures global options, most importantly the SSRF destination allow-list. This is
        /// required: with no allowed origins, every relay is denied.
        /// </param>
        public static IServiceCollection AddRelayFlow(this IServiceCollection services, Action<RelayOptions> configure)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var options = new RelayOptions();
            configure(options);

            services.TryAddSingleton(options);
            services.AddHttpForwarder(); // registers IHttpForwarder
            services.TryAddSingleton<UpstreamHttpClient>();
            services.TryAddSingleton(sp => new CircuitBreakerRegistry(options.CircuitBreaker));
            services.TryAddSingleton<RelayMetrics>();
            services.TryAddSingleton<RelayForwarder>();

            return services;
        }
    }
}
