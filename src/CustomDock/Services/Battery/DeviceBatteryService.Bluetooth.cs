using CustomDock.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace CustomDock.Services;

public sealed partial class DeviceBatteryService
{
    // Association endpoint protocol ids (the same GUID also names the Bluetooth device setup class).
    private const string BluetoothClassicProtocol = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";
    private const string BluetoothLeProtocol = "{bb7bb05e-5972-42b5-94fc-76eaa7084d49}";
    private const string BluetoothClassGuid = "{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}";

    /// <summary>DEVPKEY_Bluetooth_Battery: the level Windows shows in Settings › Bluetooth &amp; devices.</summary>
    private const string BatteryPropertyKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";

    private static readonly TimeSpan GattTimeout = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan GattRetryAfter = TimeSpan.FromMinutes(10);

    /// <summary>LE devices without a readable Battery Service are not asked again until this time.</summary>
    private static readonly Dictionary<string, DateTime> s_gattSkipUntil = new();

    private sealed record ConnectedDevice(string Address, string Name, string AepId, bool LowEnergy, int? Battery);

    /// <summary>
    /// Connected Bluetooth devices with a battery level: the level on the association endpoint, else the one Windows
    /// keeps on the device node, else (Low Energy only) the GATT Battery Service read directly.
    /// </summary>
    private static async Task<List<BatteryDeviceInfo>> ScanBluetoothDevicesAsync()
    {
        var connected = new Dictionary<string, ConnectedDevice>(StringComparer.OrdinalIgnoreCase);
        await AddConnectedAsync(connected, BluetoothClassicProtocol, lowEnergy: false).ConfigureAwait(false);
        await AddConnectedAsync(connected, BluetoothLeProtocol, lowEnergy: true).ConfigureAwait(false);
        if (connected.Count == 0) return new();

        Dictionary<string, int>? nodeLevels = null;
        var result = new List<BatteryDeviceInfo>();
        foreach (var device in connected.Values)
        {
            int? level = device.Battery;
            if (level is null)
            {
                nodeLevels ??= await ReadDeviceNodeLevelsAsync().ConfigureAwait(false);
                if (nodeLevels.TryGetValue(device.Address, out int stored)) level = stored;
            }
            if (level is null && device.LowEnergy)
                level = await ReadGattBatteryAsync(device).ConfigureAwait(false);

            if (level is >= 0 and <= 100)
                result.Add(new BatteryDeviceInfo("bt-" + device.Address, device.Name, level.Value, false, false));
        }
        return result;
    }

    private static async Task AddConnectedAsync(Dictionary<string, ConnectedDevice> connected, string protocol, bool lowEnergy)
    {
        try
        {
            string aqs = $"System.Devices.Aep.ProtocolId:=\"{protocol}\" AND {ConnectedProperty}:=System.StructuredQueryType.Boolean#True";
            var properties = new[] { ConnectedProperty, AddressProperty, BatteryPropertyKey };
            var endpoints = await DeviceInformation.FindAllAsync(aqs, properties, DeviceInformationKind.AssociationEndpoint).AsTask().ConfigureAwait(false);

            foreach (var endpoint in endpoints)
            {
                string? address = BatteryProtocols.NormalizeBluetoothAddress(Property(endpoint, AddressProperty))
                                  ?? BatteryProtocols.ParseBluetoothAddress(endpoint.Id);
                if (address is null) continue;

                int? battery = Property(endpoint, BatteryPropertyKey) is { } value ? ToLevel(value) : null;
                string name = string.IsNullOrWhiteSpace(endpoint.Name) ? L.T("Bluetooth Device") : endpoint.Name;

                // A dual-mode device shows up on both radios: keep one entry, preferring the one with a level, then
                // the Low Energy one (its Battery Service can still be read).
                if (connected.TryGetValue(address, out var existing))
                {
                    bool better = existing.Battery is null && (battery is not null || (lowEnergy && !existing.LowEnergy));
                    if (!better) continue;
                }
                connected[address] = new ConnectedDevice(address, name, endpoint.Id, lowEnergy, battery);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Bluetooth endpoint query failed ({(lowEnergy ? "LE" : "classic")}): {ex.Message}");
        }
    }

    /// <summary>Battery levels stored on Bluetooth device nodes, keyed by device address.</summary>
    private static async Task<Dictionary<string, int>> ReadDeviceNodeLevelsAsync()
    {
        var levels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string aqs = $"System.Devices.ClassGuid:=\"{BluetoothClassGuid}\"";
            var nodes = await DeviceInformation.FindAllAsync(aqs, new[] { BatteryPropertyKey }, DeviceInformationKind.Device).AsTask().ConfigureAwait(false);
            foreach (var node in nodes)
            {
                if (Property(node, BatteryPropertyKey) is not { } value || ToLevel(value) is not { } level) continue;
                if (BatteryProtocols.ParseBluetoothAddress(node.Id) is { } address) levels.TryAdd(address, level);
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Bluetooth device node query failed: {ex.Message}");
        }
        return levels;
    }

    /// <summary>Reads the standard GATT Battery Level characteristic (0x2A19) of a connected LE device.</summary>
    private static async Task<int?> ReadGattBatteryAsync(ConnectedDevice device)
    {
        if (s_gattSkipUntil.TryGetValue(device.Address, out var until) && DateTime.UtcNow < until) return null;

        using var timeout = new CancellationTokenSource(GattTimeout);
        try
        {
            using var le = await BluetoothLEDevice.FromIdAsync(device.AepId).AsTask(timeout.Token).ConfigureAwait(false);
            if (le is null) return SkipGatt(device);

            var services = await le.GetGattServicesForUuidAsync(GattServiceUuids.Battery, BluetoothCacheMode.Cached).AsTask(timeout.Token).ConfigureAwait(false);
            try
            {
                if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0) return SkipGatt(device);

                var characteristics = await services.Services[0]
                    .GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, BluetoothCacheMode.Cached)
                    .AsTask(timeout.Token).ConfigureAwait(false);
                if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
                    return SkipGatt(device);

                var read = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token).ConfigureAwait(false);
                if (read.Status != GattCommunicationStatus.Success || read.Value is not { Length: > 0 } buffer) return null;

                using var reader = DataReader.FromBuffer(buffer);
                byte level = reader.ReadByte();
                return level <= 100 ? level : null;
            }
            finally
            {
                foreach (var service in services.Services) service.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"GATT battery read failed for {device.Name}: {ex.Message}");
            return SkipGatt(device);
        }
    }

    private static int? SkipGatt(ConnectedDevice device)
    {
        s_gattSkipUntil[device.Address] = DateTime.UtcNow + GattRetryAfter;
        return null;
    }

    private static object? Property(DeviceInformation info, string key)
        => info.Properties.TryGetValue(key, out var value) ? value : null;

    private static int? ToLevel(object value)
    {
        try
        {
            int level = Convert.ToInt32(value);
            return level is >= 0 and <= 100 ? level : null;
        }
        catch
        {
            return null;
        }
    }
}
