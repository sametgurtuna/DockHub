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

If a device is asleep or switched off, its last known level stays on the dock.

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

Your entries are added to the built-in list, and an entry with the same `protocol`, `vid` and `pid` replaces the
built-in one. DockHub reads the file again whenever it changes; invalid entries are skipped and described in
`%AppData%\DockHub\log.txt`. The built-in list is
[`src/CustomDock/Resources/battery-devices.json`](../src/CustomDock/Resources/battery-devices.json); if a device works
for you, a pull request adding it there helps everyone with the same model.
