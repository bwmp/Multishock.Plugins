using HeartRateIntegration.Profiles;

namespace HeartRateIntegration.Tests;

public sealed class StandardHeartRateProfileTests
{
    private readonly StandardHeartRateProfile _profile = new();

    [Fact]
    public void ParsesEightBitHeartRateWithoutOptionalFields()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var reading = _profile.Parse([0, 72], timestamp);

        Assert.NotNull(reading);
        Assert.Equal(72, reading.BeatsPerMinute);
        Assert.Equal(timestamp, reading.TimestampUtc);
        Assert.Null(reading.SensorContactDetected);
        Assert.Null(reading.EnergyExpendedKilojoules);
        Assert.Empty(reading.RrIntervalsMilliseconds);
    }

    [Fact]
    public void ParsesSixteenBitHeartRateEnergyAndMultipleRrIntervals()
    {
        var reading = _profile.Parse([0x1f, 0x2c, 0x01, 0x34, 0x12, 0x00, 0x04, 0x00, 0x02], DateTimeOffset.UtcNow);

        Assert.NotNull(reading);
        Assert.Equal(300, reading.BeatsPerMinute);
        Assert.True(reading.SensorContactDetected);
        Assert.Equal(0x1234, reading.EnergyExpendedKilojoules);
        Assert.Equal([1000d, 500d], reading.RrIntervalsMilliseconds);
    }

    [Theory]
    [InlineData(0x00, null)]
    [InlineData(0x02, null)]
    [InlineData(0x04, false)]
    [InlineData(0x06, true)]
    public void DistinguishesUnsupportedContactFromPoorContact(byte flags, bool? expected)
    {
        Assert.Equal(expected, _profile.Parse([flags, 80], DateTimeOffset.UtcNow)!.SensorContactDetected);
    }

    public static TheoryData<byte[]> MalformedPackets => new()
    {
        { [] }, { [0] }, { [1, 80] }, { [8, 80] }, { [8, 80, 0] }, { [0x10, 80] }, { [0x10, 80, 0] },
        { [0x10, 80, 0, 4, 1] }, { [0, 80, 1] }, { [0x19, 80, 0, 0, 0] }
    };

    [Theory]
    [MemberData(nameof(MalformedPackets))]
    public void RejectsTruncatedOrInconsistentPackets(byte[] packet)
    {
        Assert.Null(_profile.Parse(packet, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void DiscoversH808SByNameAndOtherBrandsByService()
    {
        Assert.True(_profile.Matches("COOSPO H808S", []));
        Assert.True(_profile.Matches("h808s-123", []));
        Assert.True(_profile.Matches("Another brand", [_profile.ServiceUuid]));
        Assert.True(_profile.Matches("", [_profile.ServiceUuid]));
        Assert.False(_profile.Matches("Headphones", []));
    }

    [Fact]
    public void AcceptsReservedFlagsWithoutInventingFields()
    {
        Assert.Equal(72, _profile.Parse([0xe0, 72], DateTimeOffset.UtcNow)!.BeatsPerMinute);
    }
}
