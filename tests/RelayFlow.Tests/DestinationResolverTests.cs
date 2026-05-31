using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RelayFlow.Transforms;
using Xunit;

namespace RelayFlow.Tests
{
    /// <summary>Tests for destination template substitution and query handling.</summary>
    public class DestinationResolverTests
    {
        private static HttpContext ContextWith(string path, string? query = null, params (string, string)[] routeValues)
        {
            var ctx = new DefaultHttpContext();
            ctx.Request.Path = path;
            if (query != null) ctx.Request.QueryString = new QueryString(query);
            var rv = new RouteValueDictionary();
            foreach (var (k, v) in routeValues) rv[k] = v;
            ctx.Request.RouteValues = rv;
            return ctx;
        }

        [Fact]
        public void Substitutes_CatchAll_PreservingSlashes()
        {
            var ctx = ContextWith("/api/orders/1/items", routeValues: ("rest", "1/items"));
            var result = DestinationResolver.Resolve(ctx, "https://internal:5000/orders/{**rest}");

            Assert.True(result.Success);
            Assert.Equal("https://internal:5000/orders/1/items", result.Uri!.ToString());
        }

        [Fact]
        public void Substitutes_SingleSegment_Escaped()
        {
            var ctx = ContextWith("/api/orders/a b", routeValues: ("id", "a b"));
            var result = DestinationResolver.Resolve(ctx, "https://internal:5000/orders/{id}");

            Assert.True(result.Success);
            // Space must be percent-encoded on the wire (AbsoluteUri preserves encoding;
            // ToString() would show the unescaped display form).
            Assert.Equal("https://internal:5000/orders/a%20b", result.Uri!.AbsoluteUri);
        }

        [Fact]
        public void Appends_InboundQueryString()
        {
            var ctx = ContextWith("/api/orders", query: "?page=2&size=10", routeValues: ("rest", ""));
            var result = DestinationResolver.Resolve(ctx, "https://internal:5000/orders/{**rest}");

            Assert.True(result.Success);
            Assert.Contains("page=2", result.Uri!.Query);
            Assert.Contains("size=10", result.Uri!.Query);
        }

        [Fact]
        public void Merges_TemplateQuery_WithInboundQuery()
        {
            var ctx = ContextWith("/api/orders", query: "?page=2", routeValues: ("rest", ""));
            var result = DestinationResolver.Resolve(ctx, "https://internal:5000/orders?source=edge");

            Assert.True(result.Success);
            Assert.Contains("source=edge", result.Uri!.Query);
            Assert.Contains("page=2", result.Uri!.Query);
        }

        [Fact]
        public void MissingRequiredSegment_Fails()
        {
            var ctx = ContextWith("/api/orders");
            var result = DestinationResolver.Resolve(ctx, "https://internal:5000/orders/{id}");

            Assert.False(result.Success);
            Assert.Equal("missing_route_value", result.Error);
        }

        [Fact]
        public void MissingCatchAll_CollapsesToEmpty()
        {
            var ctx = ContextWith("/api/orders");
            var result = DestinationResolver.Resolve(ctx, "https://internal:5000/orders/{**rest}");

            Assert.True(result.Success);
            Assert.Equal("https://internal:5000/orders/", result.Uri!.ToString());
        }

        [Fact]
        public void InvalidTemplate_Fails()
        {
            var ctx = ContextWith("/api/x");
            var result = DestinationResolver.Resolve(ctx, "not-a-url");
            Assert.False(result.Success);
            Assert.Equal("invalid_destination", result.Error);
        }
    }
}
