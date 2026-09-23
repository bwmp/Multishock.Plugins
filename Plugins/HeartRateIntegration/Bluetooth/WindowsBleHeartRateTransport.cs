using System.Collections.Concurrent;
using System.Threading.Channels;
using HeartRateIntegration.Models;
using HeartRateIntegration.Profiles;
using Microsoft.Extensions.Logging;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace HeartRateIntegration.Bluetooth;

public sealed class WindowsBleHeartRateTransport(ILogger logger) : IBleHeartRateTransport
{
    public async Task<IReadOnlyList<HeartRateDevice>> ScanAsync(
        IReadOnlyList<IHeartRateProfile> profiles, CancellationToken cancellationToken)
    {
        var adapter = await BluetoothAdapter.GetDefaultAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (adapter is null || !adapter.IsLowEnergySupported)
            throw new InvalidOperationException("No Bluetooth Low Energy adapter is available.");
        var radio = await adapter.GetRadioAsync().AsTask(cancellationToken).ConfigureAwait(false);
        if (radio is not null && radio.State != RadioState.On)
            throw new InvalidOperationException("Turn on Bluetooth in Windows, then scan again.");

        var devices = new ConcurrentDictionary<ulong, HeartRateDevice>();
        var advertisements = new Dictionary<ulong, (string Name, HashSet<Guid> Services)>();
        var stopped = new TaskCompletionSource<BluetoothError>(TaskCreationOptions.RunContinuationsAsynchronously);
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        void Received(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            lock (advertisements)
            {
                advertisements.TryGetValue(args.BluetoothAddress, out var previous);
                var name = string.IsNullOrWhiteSpace(args.Advertisement.LocalName)
                    ? previous.Name ?? string.Empty : args.Advertisement.LocalName;
                var services = previous.Services ?? [];
                services.UnionWith(args.Advertisement.ServiceUuids);
                advertisements[args.BluetoothAddress] = (name, services);
                var profile = profiles.FirstOrDefault(p => p.Matches(name, services));
                if (profile is not null)
                    devices[args.BluetoothAddress] = new(args.BluetoothAddress,
                        string.IsNullOrWhiteSpace(name) ? $"Heart-rate monitor {args.BluetoothAddress:X12}" : name,
                        profile.Id, args.RawSignalStrengthInDBm);
            }
        }

        void Stopped(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementWatcherStoppedEventArgs args) =>
            stopped.TrySetResult(args.Error);

        watcher.Received += Received;
        watcher.Stopped += Stopped;
        try
        {
            watcher.Start();
            await Task.WhenAny(Task.Delay(TimeSpan.FromSeconds(10), cancellationToken), stopped.Task).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (stopped.Task.IsCompletedSuccessfully && stopped.Task.Result != BluetoothError.Success)
                throw new InvalidOperationException("Bluetooth scanning stopped. Check the adapter and Windows Bluetooth permissions.");
            return devices.Values.OrderByDescending(d => d.SignalStrength).ToArray();
        }
        finally
        {
            watcher.Received -= Received;
            watcher.Stopped -= Stopped;
            watcher.Stop();
        }
    }

    public async Task RunAsync(HeartRateDevice device, IHeartRateProfile profile, Action connected,
        Func<HeartRateMeasurement, Task> measurementReceived, CancellationToken cancellationToken)
    {
        using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionTimeout.CancelAfter(TimeSpan.FromSeconds(25));
        var connectToken = connectionTimeout.Token;
        using var peripheral = await BluetoothLEDevice.FromBluetoothAddressAsync(device.Address)
            .AsTask(connectToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The monitor is unavailable. Wear the strap and close other apps using it.");

        var result = await peripheral.GetGattServicesForUuidAsync(profile.ServiceUuid, BluetoothCacheMode.Uncached)
            .AsTask(connectToken).ConfigureAwait(false);
        var services = result.Services.ToArray();
        GattCharacteristic? characteristic = null;
        var subscribed = false;
        // Keep only the newest pending sample if a flow takes longer than the monitor's update interval.
        var readings = Channel.CreateBounded<HeartRateMeasurement>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        void ConnectionChanged(BluetoothLEDevice sender, object args)
        {
            if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                readings.Writer.TryComplete();
        }

        void ValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            try
            {
                using var reader = DataReader.FromBuffer(args.CharacteristicValue);
                var data = new byte[reader.UnconsumedBufferLength];
                reader.ReadBytes(data);
                var measurement = profile.Parse(data, args.Timestamp.ToUniversalTime());
                if (measurement is not null)
                    readings.Writer.TryWrite(measurement);
                else
                    logger.LogDebug("Ignored malformed heart-rate notification ({Length} bytes)", data.Length);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not decode a heart-rate notification");
                readings.Writer.TryComplete(new InvalidOperationException("The monitor sent an unreadable heart-rate notification."));
            }
        }

        try
        {
            if (result.Status != GattCommunicationStatus.Success || services.Length == 0)
                throw new InvalidOperationException("The monitor's heart-rate service could not be opened.");
            var characteristics = await services[0].GetCharacteristicsForUuidAsync(profile.MeasurementUuid, BluetoothCacheMode.Uncached)
                .AsTask(connectToken).ConfigureAwait(false);
            if (characteristics.Status != GattCommunicationStatus.Success)
                throw new InvalidOperationException("The monitor's heart-rate measurement could not be opened.");
            characteristic = characteristics.Characteristics.FirstOrDefault(c =>
                c.CharacteristicProperties.HasFlag(GattCharacteristicProperties.Notify))
                ?? throw new InvalidOperationException("This monitor does not provide heart-rate notifications for the selected profile.");

            characteristic.ValueChanged += ValueChanged;
            peripheral.ConnectionStatusChanged += ConnectionChanged;
            var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify).AsTask(connectToken).ConfigureAwait(false);
            if (status != GattCommunicationStatus.Success)
                throw new InvalidOperationException("Could not subscribe to heart-rate notifications. Check Bluetooth permissions and other connected apps.");
            subscribed = true;
            connectToken.ThrowIfCancellationRequested();
            if (peripheral.ConnectionStatus != BluetoothConnectionStatus.Connected)
                throw new InvalidOperationException("The monitor disconnected while connecting.");
            connected();

            await foreach (var measurement in readings.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (peripheral.ConnectionStatus != BluetoothConnectionStatus.Connected)
                    break;
                await measurementReceived(measurement).ConfigureAwait(false);
            }
        }
        finally
        {
            readings.Writer.TryComplete();
            peripheral.ConnectionStatusChanged -= ConnectionChanged;
            if (characteristic is not null)
            {
                characteristic.ValueChanged -= ValueChanged;
                if (subscribed && peripheral.ConnectionStatus == BluetoothConnectionStatus.Connected)
                {
                    try
                    {
                        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        var status = await characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                            GattClientCharacteristicConfigurationDescriptorValue.None).AsTask(cleanupTimeout.Token).ConfigureAwait(false);
                        if (status != GattCommunicationStatus.Success)
                            logger.LogDebug("Heart-rate unsubscribe returned {Status}", status);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Could not unsubscribe before disposing the Bluetooth connection");
                    }
                }
            }
            foreach (var service in services)
                service.Dispose();
        }
    }
}
