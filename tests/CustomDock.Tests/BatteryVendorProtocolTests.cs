using CustomDock.Services;

namespace CustomDock.Tests;

public class BatteryVendorProtocolTests
{
    // ------------------------------------------------------------------ PlayStation

    private static byte[] Report(byte id, int length, int statusOffset, byte status)
    {
        var report = new byte[length];
        report[0] = id;
        report[statusOffset] = status;
        return report;
    }

    [Theory]
    [InlineData(0x07, false, 75, false)] // on battery: level * 10 + 5
    [InlineData(0x00, false, 5, false)]
    [InlineData(0x0A, false, 100, false)]
    [InlineData(0x14, false, 45, true)]  // cable (bit 4), level 4: charging
    [InlineData(0x1A, false, 100, true)] // cable, level 10: charging the last bit
    [InlineData(0x1B, false, 100, false)] // cable, level 11: full
    public void DualShock4_status_byte_is_decoded(byte status, bool bluetooth, int percent, bool charging)
    {
        var usb = Report(0x01, 64, 30, status);
        Assert.Equal(new BatteryReading(percent, charging), BatteryProtocols.ParseDualShock4(usb, bluetooth));
        var bt = Report(0x11, 78, 32, status);
        Assert.Equal(new BatteryReading(percent, charging), BatteryProtocols.ParseDualShock4(bt, bluetooth: true));
    }

    [Fact]
    public void DualShock4_basic_bluetooth_report_has_no_battery()
    {
        Assert.Null(BatteryProtocols.ParseDualShock4(Report(0x01, 547, 30, 0x07), bluetooth: true));
        Assert.Null(BatteryProtocols.ParseDualShock4(Report(0x01, 64, 30, 0x0C), bluetooth: false)); // 12: invalid
        Assert.Null(BatteryProtocols.ParseDualShock4(new byte[10], bluetooth: false));
    }

    [Theory]
    [InlineData(0x08, 85, false)]
    [InlineData(0x13, 35, true)]
    [InlineData(0x2A, 100, false)]
    [InlineData(0x0A, 100, false)]
    public void DualSense_status_byte_is_decoded(byte status, int percent, bool charging)
    {
        Assert.Equal(new BatteryReading(percent, charging), BatteryProtocols.ParseDualSense(Report(0x01, 64, 53, status), bluetooth: false));
        Assert.Equal(new BatteryReading(percent, charging), BatteryProtocols.ParseDualSense(Report(0x31, 78, 54, status), bluetooth: true));
    }

    [Theory]
    [InlineData(0xA5)] // voltage or temperature out of range
    [InlineData(0xB5)]
    [InlineData(0xF5)] // charging error
    public void DualSense_error_states_give_no_reading(byte status)
    {
        Assert.Null(BatteryProtocols.ParseDualSense(Report(0x01, 64, 53, status), bluetooth: false));
    }

    [Fact]
    public void DualSense_basic_bluetooth_report_has_no_battery()
    {
        Assert.Null(BatteryProtocols.ParseDualSense(Report(0x01, 78, 53, 0x08), bluetooth: true));
        Assert.Null(BatteryProtocols.ParseDualSense(Report(0x31, 78, 54, 0x08), bluetooth: false));
    }

    // ------------------------------------------------------------------ Razer

    [Fact]
    public void Razer_request_layout_and_crc()
    {
        var request = BatteryProtocols.RazerRequest(0x1F, 0x07, 0x80, 0x02);
        Assert.Equal(90, request.Length);
        Assert.Equal(0x00, request[0]);
        Assert.Equal(0x1F, request[1]);
        Assert.Equal(0x02, request[5]);
        Assert.Equal(0x07, request[6]);
        Assert.Equal(0x80, request[7]);
        Assert.Equal(0x02 ^ 0x07 ^ 0x80, request[88]);
        Assert.Equal(0x00, request[89]);
    }

    private static byte[] RazerAnswer(byte status, byte commandId, byte argument1)
    {
        var answer = BatteryProtocols.RazerRequest(0x1F, 0x07, commandId, 0x02);
        answer[0] = status;
        answer[9] = argument1;
        answer[88] = BatteryProtocols.RazerCrc(answer);
        return answer;
    }

    [Fact]
    public void Razer_battery_answer_is_parsed()
    {
        var arguments = BatteryProtocols.ParseRazerResponse(RazerAnswer(0x02, 0x80, 0xFF), 0x07, 0x80, out bool busy);
        Assert.False(busy);
        Assert.NotNull(arguments);
        Assert.Equal(100, BatteryProtocols.RazerPercent(arguments![1]));
        Assert.Equal(50, BatteryProtocols.RazerPercent(128));
        Assert.Equal(0, BatteryProtocols.RazerPercent(0));
    }

    [Fact]
    public void Razer_busy_failed_mismatched_and_corrupt_answers_are_rejected()
    {
        Assert.Null(BatteryProtocols.ParseRazerResponse(RazerAnswer(0x01, 0x80, 10), 0x07, 0x80, out bool busy));
        Assert.True(busy);
        Assert.Null(BatteryProtocols.ParseRazerResponse(RazerAnswer(0x05, 0x80, 10), 0x07, 0x80, out busy));
        Assert.False(busy);
        Assert.Null(BatteryProtocols.ParseRazerResponse(RazerAnswer(0x02, 0x84, 10), 0x07, 0x80, out _));

        var corrupt = RazerAnswer(0x02, 0x80, 10);
        corrupt[88] ^= 0xFF;
        Assert.Null(BatteryProtocols.ParseRazerResponse(corrupt, 0x07, 0x80, out _));
        Assert.Null(BatteryProtocols.ParseRazerResponse(new byte[20], 0x07, 0x80, out _));
    }

    // ------------------------------------------------------------------ Logitech HID++

    [Fact]
    public void Hidpp_request_layout()
    {
        var request = BatteryProtocols.HidppRequest(0x02, 0x00, 0, 0x10, 0x04);
        Assert.Equal(20, request.Length);
        Assert.Equal(new byte[] { 0x11, 0x02, 0x00, 0x0B, 0x10, 0x04, 0x00 }, request[..7]);

        var status = BatteryProtocols.HidppRequest(0xFF, 0x06, 1);
        Assert.Equal(new byte[] { 0x11, 0xFF, 0x06, 0x1B }, status[..4]);
    }

    [Fact]
    public void Hidpp_replies_errors_and_notifications_are_told_apart()
    {
        var reply = new byte[20];
        reply[0] = 0x11; reply[1] = 0x01; reply[2] = 0x06; reply[3] = 0x1B; reply[4] = 55; reply[5] = 0x04; reply[6] = 0x00;
        Assert.Equal(BatteryProtocols.HidppAnswer.Reply, BatteryProtocols.MatchHidpp(reply, 0x01, 0x06, 1, out var parameters));
        Assert.Equal(55, parameters[0]);

        var otherDevice = (byte[])reply.Clone();
        otherDevice[1] = 0x02;
        Assert.Equal(BatteryProtocols.HidppAnswer.Unrelated, BatteryProtocols.MatchHidpp(otherDevice, 0x01, 0x06, 1, out _));

        var notification = (byte[])reply.Clone();
        notification[3] = 0x00; // software id 0: an event, not our reply
        Assert.Equal(BatteryProtocols.HidppAnswer.Unrelated, BatteryProtocols.MatchHidpp(notification, 0x01, 0x06, 1, out _));

        var error20 = new byte[20];
        error20[0] = 0x11; error20[1] = 0x01; error20[2] = 0xFF; error20[3] = 0x06; error20[4] = 0x1B; error20[5] = 0x02;
        Assert.Equal(BatteryProtocols.HidppAnswer.Error, BatteryProtocols.MatchHidpp(error20, 0x01, 0x06, 1, out _));

        var error10 = new byte[7];
        error10[0] = 0x10; error10[1] = 0x03; error10[2] = 0x8F; error10[3] = 0x00; error10[4] = 0x0B; error10[5] = 0x09;
        Assert.Equal(BatteryProtocols.HidppAnswer.Error, BatteryProtocols.MatchHidpp(error10, 0x03, 0x00, 0, out _));
    }

    [Theory]
    [InlineData(new byte[] { 72, 0x04, 0x00 }, 72, false)]
    [InlineData(new byte[] { 40, 0x04, 0x01 }, 40, true)]
    [InlineData(new byte[] { 97, 0x08, 0x03 }, 100, false)]
    [InlineData(new byte[] { 0, 0x02, 0x00 }, 20, false)]   // no percentage: level flags
    [InlineData(new byte[] { 0, 0x08, 0x00 }, 90, false)]
    public void Hidpp_unified_battery(byte[] parameters, int percent, bool charging)
    {
        Assert.Equal(new BatteryReading(percent, charging), BatteryProtocols.ParseHidppUnifiedBattery(parameters));
    }

    [Theory]
    [InlineData(new byte[] { 60, 50, 0 }, 60, false)]
    [InlineData(new byte[] { 60, 50, 1 }, 60, true)]
    [InlineData(new byte[] { 90, 0, 3 }, 100, false)]
    public void Hidpp_battery_status(byte[] parameters, int percent, bool charging)
    {
        Assert.Equal(new BatteryReading(percent, charging), BatteryProtocols.ParseHidppBatteryStatus(parameters));
        Assert.Null(BatteryProtocols.ParseHidppBatteryStatus(new byte[] { 0, 0, 0 }));
    }

    [Theory]
    [InlineData(4200, 100)]
    [InlineData(4186, 100)]
    [InlineData(3811, 50)]
    [InlineData(3835, 55)]
    [InlineData(3500, 0)]
    [InlineData(3000, 0)]
    public void Voltage_is_mapped_along_the_discharge_curve(int millivolts, int percent)
    {
        Assert.Equal(percent, BatteryProtocols.VoltageToPercent(millivolts));
    }

    [Fact]
    public void Hidpp_battery_voltage()
    {
        // 3811 mV = 0x0EE3
        Assert.Equal(new BatteryReading(50, false), BatteryProtocols.ParseHidppBatteryVoltage(new byte[] { 0x0E, 0xE3, 0x00 }));
        Assert.Equal(new BatteryReading(50, true), BatteryProtocols.ParseHidppBatteryVoltage(new byte[] { 0x0E, 0xE3, 0x80 }));
        Assert.Equal(new BatteryReading(100, false), BatteryProtocols.ParseHidppBatteryVoltage(new byte[] { 0x10, 0x5A, 0x81 }));
        Assert.Equal(new BatteryReading(50, false), BatteryProtocols.ParseHidppBatteryVoltage(new byte[] { 0x0E, 0xE3, 0x82 }));
        Assert.Null(BatteryProtocols.ParseHidppBatteryVoltage(new byte[] { 0x00, 0x10, 0x00 }));
    }

    [Theory]
    [InlineData(0x00, BatteryDeviceKind.Keyboard)]
    [InlineData(0x03, BatteryDeviceKind.Mouse)]
    [InlineData(0x05, BatteryDeviceKind.Mouse)]
    [InlineData(0x08, BatteryDeviceKind.Headset)]
    [InlineData(0x07, BatteryDeviceKind.Unknown)]
    public void Hidpp_device_types(byte type, BatteryDeviceKind kind)
    {
        Assert.Equal(kind, BatteryProtocols.HidppDeviceKind(type));
    }

    // ------------------------------------------------------------------ Catalog-defined requests

    [Fact]
    public void Hid_request_answer_is_matched_and_scaled()
    {
        var spec = new HidRequestSpec(false, new byte[] { 0x06, 0x18 }, 0xFF43, new byte[] { 0x06 }, 2, LevelMax: 4, ChargingOffset: 3, ChargingValue: 1, OfflineValue: 0xFF);
        Assert.Equal(new BatteryReading(75, true), BatteryProtocols.ParseHidRequest(spec, new byte[] { 0x06, 0x18, 3, 1 }));
        Assert.Equal(new BatteryReading(100, false), BatteryProtocols.ParseHidRequest(spec, new byte[] { 0x06, 0x18, 9, 0 })); // clamped
        Assert.Null(BatteryProtocols.ParseHidRequest(spec, new byte[] { 0x07, 0x18, 3, 1 }));  // other report
        Assert.Null(BatteryProtocols.ParseHidRequest(spec, new byte[] { 0x06, 0x18, 0xFF, 0 })); // headset off
        Assert.Null(BatteryProtocols.ParseHidRequest(spec, new byte[] { 0x06 }));
    }

    [Fact]
    public void Hid_request_entries_are_parsed_from_the_catalog()
    {
        var errors = new List<string>();
        var catalog = BatteryDeviceCatalog.Parse("""
            { "devices": [
                { "protocol": "hidrequest", "vid": "1038", "pid": "12AD", "name": "Headset", "kind": "headset",
                  "method": "feature", "request": "06 18", "usagePage": "FF43", "match": "0618",
                  "levelOffset": 2, "levelMax": 4, "chargingOffset": 3, "chargingValue": 2, "offlineValue": 255 },
                { "protocol": "hidrequest", "vid": "1038", "pid": "0001", "name": "No request", "levelOffset": 2 },
                { "protocol": "hidrequest", "vid": "1038", "pid": "0002", "name": "Bad offset", "request": "06", "levelOffset": 99 },
                { "protocol": "hidrequest", "vid": "1038", "pid": "0003", "name": "Bad method", "request": "06", "levelOffset": 1, "method": "shout" },
                { "protocol": "playstation", "vid": "054C", "pid": "0CE6", "name": "No variant" },
                { "protocol": "hidpp", "vid": "046D", "variant": "toaster", "name": "Bad variant" }
            ] }
            """, errors);

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal(5, errors.Count);
        var spec = entry.Request!;
        Assert.True(spec.Feature);
        Assert.Equal(new byte[] { 0x06, 0x18 }, spec.Request);
        Assert.Equal((ushort)0xFF43, spec.UsagePage);
        Assert.Equal(new byte[] { 0x06, 0x18 }, spec.Match);
        Assert.Equal(2, spec.LevelOffset);
        Assert.Equal(4, spec.LevelMax);
        Assert.Equal(3, spec.ChargingOffset);
        Assert.Equal(2, spec.ChargingValue);
        Assert.Equal(255, spec.OfflineValue);
    }

    [Fact]
    public void Built_in_catalog_covers_the_new_vendors()
    {
        var catalog = BatteryDeviceCatalog.BuiltIn;
        Assert.Equal("dualsense", catalog.Find(BatteryProtocols.PlayStation, 0x054C, 0x0CE6)!.Variant);
        Assert.Equal("ds4", catalog.Find(BatteryProtocols.PlayStation, 0x054C, 0x09CC)!.Variant);
        Assert.Equal("receiver", catalog.Find(BatteryProtocols.Logitech, 0x046D, 0xC548)!.Variant);
        Assert.Equal("device", catalog.Find(BatteryProtocols.Logitech, 0x046D, 0xB023)!.Variant);
        var viper = catalog.Find(BatteryProtocols.Razer, 0x1532, 0x00A6)!;
        Assert.Equal("razer-viper-v2-pro", viper.Id);
        Assert.Equal(viper.Id, catalog.Find(BatteryProtocols.Razer, 0x1532, 0x00A5)!.Id);
        Assert.NotNull(catalog.Find(BatteryProtocols.HidRequest, 0x1038, 0x12AD)!.Request);
    }

    // ------------------------------------------------------------------ Controllers

    [Theory]
    [InlineData(50, 100, 50)]
    [InlineData(1450, 1450, 100)]
    [InlineData(2000, 1450, 100)]
    [InlineData(null, 100, null)]
    [InlineData(10, 0, null)]
    [InlineData(10, null, null)]
    public void Gamepad_percent(int? remaining, int? full, int? percent)
    {
        Assert.Equal(percent, BatteryProtocols.GamepadPercent(remaining, full));
    }

    [Theory]
    [InlineData(@"\\?\hid#{00001124-0000-1000-8000-00805f9b34fb}_vid&0002054c_pid&0ce6#9&1&0&0000#{4d1e55b2}", true)]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&02046d_pid&b023#a&1&0&0000#{4d1e55b2}", true)]
    [InlineData(@"\\?\hid#vid_054c&pid_0ce6&mi_03#8&1&0&0000#{4d1e55b2}", false)]
    public void Bluetooth_hid_paths_are_recognized(string path, bool bluetooth)
    {
        Assert.Equal(bluetooth, BatteryProtocols.IsBluetoothHidPath(path));
    }
}
