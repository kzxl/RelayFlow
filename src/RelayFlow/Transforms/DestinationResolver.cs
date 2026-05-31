using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RelayFlow.Transforms
{
    /// <summary>
    /// Outcome of resolving a destination URI for a request.
    /// </summary>
    public readonly struct DestinationResult
    {
        private DestinationResult(Uri? uri, string? error)
        {
            Uri = uri;
            Error = error;
        }

        /// <summary>The resolved absolute destination, or null on failure.</summary>
        public Uri? Uri { get; }

        /// <summary>A short error code when resolution failed; null on success.</summary>
        public string? Error { get; }

        /// <summary>True when a destination was successfully resolved.</summary>
        public bool Success => Uri != null;

        /// <summary>Creates a success result.</summary>
        public static DestinationResult Ok(Uri uri) => new DestinationResult(uri, null);

        /// <summary>Creates a failure result.</summary>
        public static DestinationResult Fail(string error) => new DestinationResult(null, error);
    }

    /// <summary>
    /// Builds the absolute upstream destination URI for a request by substituting captured route
    /// values into the route's destination template and appending the original query string.
    /// <para>
    /// Template tokens use ASP.NET route syntax: <c>{name}</c> for a single segment and
    /// <c>{**name}</c> for a catch-all. Captured values are URI-escaped per segment to avoid
    /// path-injection. The class performs no network access.
    /// </para>
    /// </summary>
    public static class DestinationResolver
    {
        /// <summary>
        /// Resolves the destination for <paramref name="context"/> using <paramref name="template"/>.
        /// </summary>
        /// <param name="context">The current request (for route values and query string).</param>
        /// <param name="template">Destination template, e.g. <c>https://internal:5000/orders/{**rest}</c>.</param>
        /// <param name="appendQuery">When true, the inbound query string is appended.</param>
        public static DestinationResult Resolve(HttpContext context, string template, bool appendQuery = true)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (string.IsNullOrWhiteSpace(template)) return DestinationResult.Fail("empty_template");

            RouteValueDictionary routeValues = context.Request.RouteValues;
            string substituted;
            try
            {
                substituted = Substitute(template, routeValues);
            }
            catch (KeyNotFoundException)
            {
                return DestinationResult.Fail("missing_route_value");
            }

            if (!Uri.TryCreate(substituted, UriKind.Absolute, out var baseUri))
                return DestinationResult.Fail("invalid_destination");

            if (!appendQuery || !context.Request.QueryString.HasValue)
                return DestinationResult.Ok(baseUri);

            // Append the original query, merging with any query already in the template.
            var builder = new UriBuilder(baseUri);
            string inboundQuery = context.Request.QueryString.Value!.TrimStart('?');
            builder.Query = string.IsNullOrEmpty(builder.Query)
                ? inboundQuery
                : builder.Query.TrimStart('?') + "&" + inboundQuery;

            return DestinationResult.Ok(builder.Uri);
        }

        /// <summary>
        /// Substitutes <c>{name}</c> and <c>{**name}</c> tokens in the template with route values.
        /// Catch-all values keep their slashes; single segments are escaped wholesale.
        /// </summary>
        internal static string Substitute(string template, RouteValueDictionary routeValues)
        {
            var sb = new StringBuilder(template.Length);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                if (c == '{')
                {
                    int end = template.IndexOf('}', i + 1);
                    if (end < 0)
                    {
                        // Unbalanced brace: treat the rest as literal.
                        sb.Append(template, i, template.Length - i);
                        break;
                    }

                    string token = template.Substring(i + 1, end - i - 1);
                    bool catchAll = token.StartsWith("**", StringComparison.Ordinal);
                    string name = catchAll ? token.Substring(2) : token;

                    if (!routeValues.TryGetValue(name, out var value) || value == null)
                    {
                        // A missing optional catch-all collapses to empty; a missing required
                        // single value is an error so the caller can reject.
                        if (catchAll) { i = end + 1; continue; }
                        throw new KeyNotFoundException(name);
                    }

                    string raw = value.ToString() ?? string.Empty;
                    sb.Append(catchAll ? EscapePath(raw) : Uri.EscapeDataString(raw));
                    i = end + 1;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        // Escape a catch-all path while preserving '/' separators.
        private static string EscapePath(string path)
        {
            var segments = path.Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Uri.EscapeDataString(segments[i]);
            }
            return string.Join("/", segments);
        }
    }
}
