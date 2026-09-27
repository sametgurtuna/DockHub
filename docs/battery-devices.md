# Device batteries

The **Device batteries** widget shows the battery level of your peripherals. It reads them from two sources.

## Bluetooth

Every connected Bluetooth device (classic and Low Energy) that Windows knows a battery level for, which is the level
*Windows Settings › Bluetooth & devices* shows. For Low Energy devices without that level, DockHub reads the standard
Battery Service (GATT 0x180F) directly. A device that doesn't offer the service is asked again only after 10 minutes.

## USB receivers and cables

Devices listed in DockHub's device catalog, read over HID with the manufacturer's protocol. DockHub only asks for the
battery level; it never changes a device's settings.

| Device | Protocol | Notes |
|---|---|---|
| HyperX Cloud II Wireless, Cloud Core Wireless, Cloud Alpha Wireless, Cloud Flight | `hyperx` | Other HyperX dongles are tried too and shown as *HyperX Headset (product id)*. |
| LAMZU Atlantis Mini | `compx` | 2.4 GHz receiver; shown as charging while the cable is plugged in. |
| Sony DualShock 4, DualSense, DualSense Edge | `playstation` | Read from the input report the controller sends anyway. Over Bluetooth only while an app (Steam, DS4Windows) has switched it to full reports; DockHub never switches it itself, because that stops the controller from working in DirectInput games. |
| Logitech devices on Unifying, Lightspeed and Bolt receivers, or on a cable or Bluetooth | `hidpp` | HID++ 2.0 devices; the name and kind come from the device. Older HID++ 1.0 devices are not read. |
| Razer Viper Ultimate, Viper V2 Pro, DeathAdder V2 Pro, DeathAdder V3 Pro, DeathAdder V2 X HyperSpeed, Basilisk Ultimate, Basilisk V3 Pro, Basilisk X HyperSpeed, Naga Pro, Orochi V2 | `razer` | Receiver or cable. |
| SteelSeries Arctis 7 | `hidrequest` | One request, one answer (as in HeadsetControl). |

Xbox controllers (and any other controller that reports a battery to Windows) are read through Windows' game
controller API, whether they use the Xbox Wireless Adapter, Bluetooth or a cable.

If a device is asleep or switched off, its last known level stays on the dock.

> [!NOTE]
> The PlayStation, Logitech, Razer and SteelSeries protocols follow the open source drivers that document them (the
> Linux kernel, Solaar, OpenRazer, HeadsetControl) and were not tested with every model. If a device you own is
> missing or shows a wrong level, open an issue with *Settings › About › Report a problem* and, with
> `"debugLogging": true` in `config.json`, the related lines of `log.txt`.

## Adding a device

A model that speaks one of the protocols above but has a different product id can be added without waiting for a
DockHub update. Create `%AppData%\DockHub\battery-devices.json`:

```json
{
  "devices": [
    { "protocol": "compx", "vid": "25A7", "pid": "FA7D", "name": "My mouse", "kind": "mouse" }
  ]
}
```

| Field | Meaning |
|---|---|
| `protocol` | One of the protocols in the table above. |
| `vid`, `pid` | Vendor and product id, four hex digits each (see *Device Manager › Properties › Details › Hardware Ids*). Leave `pid` out to match every product of the vendor. |
| `name` | Name shown on the dock. `{pid}` is replaced with the product id. |
| `kind` | `headset`, `mouse`, `keyboard` or `controller`; picks the icon. |
| `id` | Optional stable id (lower-case letters, digits and dashes). Entries with the same id are one device, for example a mouse's receiver and its cable. |
| `wired` | `true` when this product id means the device is plugged in with its cable (shown as charging). |
| `variant` | Required for `playstation` (`ds4` or `dualsense`) and `hidpp` (`receiver` for a receiver whose paired devices are read, `device` for a device on a cable or Bluetooth). |

### Devices with a simple request and answer

Many headsets answer a single request with their battery level. The `hidrequest` protocol describes such a device
completely in the file, without code:

```json
{ "protocol": "hidrequest", "vid": "1038", "pid": "12AD", "name": "SteelSeries Arctis 7", "kind": "headset",
  "method": "output", "request": "06 18", "usagePage": "FF43", "levelOffset": 2 }
```

| Field | Meaning |
|---|---|
| `method` | `output` (write an output report, then read input reports) or `feature` (set a feature report, then get it back). |
| `request` | The bytes to send; the first byte is the report id. The report is padded with zeros to the collection's length. |
| `usagePage` | Optional: only the HID collection with this usage page is used. |
| `match` | Optional hex bytes the answer must start with, so other reports are ignored. |
| `levelOffset` | Byte of the answer (counting the report id as byte 0) that holds the level. |
| `levelMax` | Value that means full (default 100); the level is scaled to percent. |
| `chargingOffset`, `chargingValue` | Optional: the device is charging when that byte has that value (default 1). |
| `offlineValue` | Optional: a level value that means the device is switched off. |

Your entries are added to the built-in list, and an entry with the same `protocol`, `vid` and `pid` replaces the
built-in one. DockHub reads the file again whenever it changes; invalid entries are skipped and described in
`%AppData%\DockHub\log.txt`. The built-in list is
[`src/CustomDock/Resources/battery-devices.json`](../src/CustomDock/Resources/battery-devices.json); if a device works
for you, a pull request adding it there helps everyone with the same model.
