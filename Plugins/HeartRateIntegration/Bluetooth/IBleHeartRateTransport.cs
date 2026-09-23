using HeartRateIntegration.Models;
using HeartRateIntegration.Profiles;

namespace HeartRateIntegration.Bluetooth;

public interface IBleHeartRateTransport
{
    Task<IReadOnlyList<HeartRateDevice>> ScanAsync(IReadOnlyList<IHeartRateProfile> profiles, CancellationToken cancellationToken);
    Task RunAsync(HeartRateDevice device, IHeartRateProfile profile, Action connected,
        Func<HeartRateMeasurement, Task> measurementReceived, CancellationToken cancellationToken);
}
