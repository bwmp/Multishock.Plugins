using System.Buffers.Binary;
using HeartRateIntegration.Models;

namespace HeartRateIntegration.Profiles;

public sealed class StandardHeartRateProfile : IHeartRateProfile
{
    public string Id => "bluetooth.heart-rate";
    public string Name => "Standard BLE (including COOSPO H808S)";
    public Guid ServiceUuid => new("0000180d-0000-1000-8000-00805f9b34fb");
    public Guid MeasurementUuid => new("00002a37-0000-1000-8000-00805f9b34fb");

    public bool Matches(string advertisedName, IReadOnlyCollection<Guid> advertisedServices) =>
        advertisedServices.Contains(ServiceUuid) ||
        advertisedName.Contains("H808S", StringComparison.OrdinalIgnoreCase);

    public HeartRateMeasurement? Parse(ReadOnlySpan<byte> payload, DateTimeOffset timestampUtc)
    {
        if (payload.Length < 2)
            return null;

        var flags = payload[0];
        var wideHeartRate = (flags & 0x01) != 0;
        var hasEnergy = (flags & 0x08) != 0;
        var hasRr = (flags & 0x10) != 0;
        var offset = wideHeartRate ? 3 : 2;
        if (payload.Length < offset + (hasEnergy ? 2 : 0))
            return null;

        var bpm = wideHeartRate ? BinaryPrimitives.ReadUInt16LittleEndian(payload[1..]) : payload[1];
        bool? contact = (flags & 0x04) != 0 ? (flags & 0x02) != 0 : null;
        int? energy = null;
        if (hasEnergy)
        {
            energy = BinaryPrimitives.ReadUInt16LittleEndian(payload[offset..]);
            offset += 2;
        }

        var remaining = payload.Length - offset;
        if (hasRr ? remaining < 2 || remaining % 2 != 0 : remaining != 0)
            return null;

        var rr = new double[remaining / 2];
        for (var i = 0; i < rr.Length; i++)
            rr[i] = BinaryPrimitives.ReadUInt16LittleEndian(payload[(offset + i * 2)..]) * 1000.0 / 1024;

        return new HeartRateMeasurement(bpm, contact, energy, Array.AsReadOnly(rr), timestampUtc);
    }
}
