using HeartRateIntegration.Models;

namespace HeartRateIntegration.Profiles;

public interface IHeartRateProfile
{
    string Id { get; }
    string Name { get; }
    Guid ServiceUuid { get; }
    Guid MeasurementUuid { get; }
    bool Matches(string advertisedName, IReadOnlyCollection<Guid> advertisedServices);
    HeartRateMeasurement? Parse(ReadOnlySpan<byte> payload, DateTimeOffset timestampUtc);
}
