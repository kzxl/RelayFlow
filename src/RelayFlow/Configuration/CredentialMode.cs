namespace RelayFlow.Configuration
{
    /// <summary>
    /// How RelayFlow handles credentials when forwarding a request from the edge API to the
    /// internal API. The incoming caller is authenticated by ASP.NET Core before forwarding;
    /// this controls what (if anything) is sent upstream to the internal service.
    /// </summary>
    public enum CredentialMode
    {
        /// <summary>
        /// Strip the inbound <c>Authorization</c> header and send nothing in its place.
        /// Use when the internal API trusts the network boundary (e.g. private subnet/mTLS).
        /// This is the safest default: the public token never reaches the internal service.
        /// </summary>
        None,

        /// <summary>
        /// Replace the inbound <c>Authorization</c> with a service-to-service bearer token
        /// supplied by an <see cref="RelayFlow.Security.ICredentialProvider"/>.
        /// </summary>
        ServiceToken,

        /// <summary>
        /// Replace credentials with a static API key header (name + value) supplied by an
        /// <see cref="RelayFlow.Security.ICredentialProvider"/>.
        /// </summary>
        ApiKey,

        /// <summary>
        /// Forward the original inbound <c>Authorization</c> header unchanged. Only safe when
        /// the internal API validates the same token issuer/audience as the edge. Opt-in.
        /// </summary>
        PassThrough,

        /// <summary>
        /// Project selected authenticated user claims into trusted headers (e.g.
        /// <c>X-Relay-User-Id</c>) for the internal API to consume. Inbound copies of those
        /// headers from the client are always stripped first to prevent spoofing.
        /// </summary>
        ForwardClaims
    }
}
