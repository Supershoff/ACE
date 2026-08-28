namespace ACE.Cloud.Hosting;

/// <summary>
/// Reports whether ACE's private world-boundary endpoint is currently reachable (ARCH-008). The
/// companion Backend/Worker never reference ACE.Server directly (ARCH-003/ARCH-004), so this is
/// always an out-of-process probe (see <see cref="HttpCloudWorldBoundaryHealthProbe"/>), never an
/// in-process call.
/// </summary>
public interface ICloudWorldBoundaryHealthProbe
{
    Task<CloudStartupCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
