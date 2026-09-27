using CustomDock.Services;

namespace CustomDock.Tests;

public class BatteryProtocolTests
{
    // ------------------------------------------------------------------ Device paths

    [Theory]
    [InlineData(@"\\?\hid#vid_25a7&pid_fa7c&mi_02&col03#8&1f2e3d4c&0&0002#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x25A7, 0xFA7C)]
    [InlineData(@"\\?\HID#VID_0951&PID_1718&MI_03#7&ABC&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 0x0951, 0x1718)]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002046d_pid&b01a&col01#9&2a&0&0000#{4d1e55b2}", 0x046D, 0xB01A)]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b023_rev&0007_d4f1b2c3a4e5#a&1&0&0000#{4d1e55b2}", 0x046D, 0xB023)]
    public void Vendor_and_product_ids_are_read_from_hid_paths(string path, int vid, int pid)
    {
        Assert.Equal(((ushort)vid, (ushort)pid), BatteryProtocols.ParseVidPid(path));
    }

    [Fact]
    public void Paths_without_ids_are_ignored()
    {
        Assert.Null(BatteryProtocols.ParseVidPid(@"\\?\hid#convertedDevice&col01#5&1a2b&0&0000#{4d1e55b2}"));
    }

    [Theory]
    [InlineData("Bluetooth#Bluetooth00:1a:7d:da:71:13-38:18:4c:12:ab:cd", "38184C12ABCD")]
    [InlineData("BluetoothLE#BluetoothLE00:1a:7d:da:71:13-d4:f1:b2:c3:a4:e5", "D4F1B2C3A4E5")]
    [InlineData(@"BTHLE\DEV_D4F1B2C3A4E5\8&2a1b3c4d&0&D4F1B2C3A4E5", "D4F1B2C3A4E5")]
    [InlineData(@"BTHENUM\{0000111e-0000-1000-8000-00805f9b34fb}_LOCALMFG&005d\7&2a8b8e3b&0&38184C12ABCD_C00000000", "38184C12ABCD")]
    [InlineData(@"BTHENUM\DEV_38184C12ABCD\7&1d2c3b4a&0&BLUETOOTHDEVICE_38184C12ABCD", "38184C12ABCD")]
    public void Bluetooth_addresses_are_read_from_ids(string id, string address)
    {
        Assert.Equal(address, BatteryProtocols.ParseBluetoothAddress(id));
    }

    [Theory]
    [InlineData("38:18:4c:12:ab:cd", "38184C12ABCD")]
    [InlineData("38184c12abcd", "38184C12ABCD")]
    [InlineData(0x38184C12ABCDUL, "38184C12ABCD")]
    [InlineData("not an address", null)]
    [InlineData(null, null)]
    public void Bluetooth_addresses_are_normalized(object? value, string? expected)
    {
        Assert.Equal(expected, BatteryProtocols.NormalizeBluetoothAddress(value));
    }

    // ------------------------------------------------------------------ Compx

    [Fact]
    public void Compx_command_sums_to_0x55()
    {
        var command = BatteryProtocols.CompxCommand(BatteryProtocols.CompxBatteryCommand);
        Assert.Equal(17, command.Length);
        Assert.Equal(0x08, command[0]);
        Assert.Equal(0x04, command[1]);
        Assert.Equal(0x55, command.Sum(b => b) & 0xFF);
        Assert.Equal(0x49, command[^1]);
    }

    [Fact]
    public void Compx_battery_answer_is_parsed()
    {
        var response = new byte[17];
        response[0] = 0x09;
        response[1] = 0x04;
        response[6] = 87;
        response[7] = 1;
        Assert.True(BatteryProtocols.IsCompxBatteryResponse(response));
        Assert.Equal(new BatteryReading(87, true), BatteryProtocols.ParseCompxBattery(response));

        response[7] = 0;
        Assert.Equal(new BatteryReading(87, false), BatteryProtocols.ParseCompxBattery(response));
    }

    [Theory]
    [InlineData(0)]    // asleep: the last known level is kept instead
    [InlineData(101)]
    [InlineData(255)]
    public void Compx_levels_outside_1_to_100_are_rejected(byte level)
    {
        var response = new byte[17];
        response[0] = 0x09;
        response[1] = 0x04;
        response[6] = level;
        Assert.True(BatteryProtocols.IsCompxBatteryResponse(response));
        Assert.Null(BatteryProtocols.ParseCompxBattery(response));
    }

    [Fact]
    public void Other_compx_reports_are_not_battery_answers()
    {
        var response = new byte[17];
        response[0] = 0x09;
        response[1] = 0x02;
        response[6] = 50;
        Assert.False(BatteryProtocols.IsCompxBatteryResponse(response));
        Assert.Null(BatteryProtocols.ParseCompxBattery(response));
        Assert.Null(BatteryProtocols.ParseCompxBattery(new byte[5]));
    }

    // ------------------------------------------------------------------ HyperX

    [Fact]
    public void HyperX_status_report_is_parsed()
    {
        var report = new byte[62];
        report[0] = 0x0B;
        report[2] = 0xBB;
        report[3] = 0x02;
        report[4] = 1;
        report[7] = 64;
        Assert.Equal(new BatteryReading(64, true), BatteryProtocols.ParseHyperXInput(report));

        report[4] = 0;
        Assert.Equal(new BatteryReading(64, false), BatteryProtocols.ParseHyperXInput(report));

        report[7] = 120;
        Assert.Null(BatteryProtocols.ParseHyperXInput(report));

        report[7] = 64;
        report[3] = 0x03;
        Assert.Null(BatteryProtocols.ParseHyperXInput(report));
    }

    [Fact]
    public void HyperX_feature_answer_uses_the_first_level_from_offset_2()
    {
        var response = new byte[32];
        response[0] = 0x21;
        response[1] = 0xBB; // ignored: before offset 2
        response[5] = 42;
        response[6] = 2;
        Assert.Equal(new BatteryReading(42, true), BatteryProtocols.ParseHyperXFeature(response));

        Assert.Null(BatteryProtocols.ParseHyperXFeature(new byte[32]));
    }

    [Fact]
    public void HyperX_feature_request_layout()
    {
        var request = BatteryProtocols.HyperXFeatureRequest();
        Assert.Equal(32, request.Length);
        Assert.Equal(new byte[] { 0x21, 0xBB, 0x0B }, request[..3]);
    }

    // ------------------------------------------------------------------ Device record

    [Theory]
    [InlineData("HyperX Cloud II Wireless", BatteryDeviceKind.Headset)]
    [InlineData("MX Master 3S Mouse", BatteryDeviceKind.Mouse)]
    [InlineData("K380 Keyboard", BatteryDeviceKind.Keyboard)]
    [InlineData("DualSense Wireless Controller", BatteryDeviceKind.Controller)]
    [InlineData("Galaxy Watch", BatteryDeviceKind.Unknown)]
    public void Kind_is_guessed_from_the_name_when_unknown(string name, BatteryDeviceKind kind)
    {
        Assert.Equal(kind, new BatteryDeviceInfo("x", name, 50, false, false).EffectiveKind);
    }

    [Fact]
    public void Known_kind_wins_over_the_name()
    {
        var device = new BatteryDeviceInfo("x", "Cloud Mouse", 50, false, false, BatteryDeviceKind.Mouse);
        Assert.True(device.IsMouse);
        Assert.False(device.IsHeadset);
    }
}
