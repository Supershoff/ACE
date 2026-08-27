namespace ACE.Cloud.Persistence;

/// <summary>
/// Thrown only by a test-supplied fault injector to simulate a process crash at one
/// <see cref="CloudBoundaryFaultPoint"/>. Never thrown by production code paths.
/// </summary>
public sealed class CloudBoundarySimulatedCrashException : Exception
{
    public CloudBoundarySimulatedCrashException(CloudBoundaryFaultPoint faultPoint)
        : base($"Simulated crash injected at {faultPoint} for fault-injection testing.")
    {
        FaultPoint = faultPoint;
    }

    public CloudBoundaryFaultPoint FaultPoint { get; }
}
