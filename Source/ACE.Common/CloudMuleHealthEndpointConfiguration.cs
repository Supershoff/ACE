namespace ACE.Common
{
    /// <summary>
    /// Where AC Cloud Mule's ACE-owned world-boundary liveness endpoint listens (ARCH-008). This is
    /// the endpoint companion services (and the disposable local acceptance launcher) probe as
    /// <c>worldBoundaryHealthEndpoint</c> -- it exists only while <see cref="CloudMuleConfiguration.Enabled"/>
    /// and <see cref="Enabled"/> are both true, is bound to a loopback/private address by default, and
    /// exposes no custody mutation surface.
    /// </summary>
    public class CloudMuleHealthEndpointConfiguration
    {
        public bool Enabled { get; set; } = true;

        public string BindAddress { get; set; } = "127.0.0.1";

        public int Port { get; set; } = 9600;
    }
}
