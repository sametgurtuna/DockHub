using CustomDock.Services;

namespace CustomDock.Tests;

public class BatteryDeviceCatalogTests
{
    [Fact]
    public void Built_in_catalog_loads_without_errors()
    {
        var catalog = BatteryDeviceCatalog.BuiltIn;
        Assert.NotEmpty(catalog.Entries);

        var lamzu = catalog.Find(BatteryProtocols.Compx, 0x25A7, 0xFA7C);
        Assert.NotNull(lamzu);
        Assert.Equal("lamzu-atlantis-mini", lamzu!.Id);
        Assert.Equal(BatteryDeviceKind.Mouse, lamzu.Kind);
        Assert.False(lamzu.Wired);
        Assert.True(catalog.Find(BatteryProtocols.Compx, 0x25A7, 0xFA7B)!.Wired);
    }

    [Fact]
    public void Built_in_file_has_no_invalid_entries()
    {
        using var stream = typeof(BatteryDeviceCatalog).Assembly.GetManifestResourceStream("CustomDock.Resources.battery-devices.json");
        Assert.NotNull(stream);
        var errors = new List<string>();
        BatteryDeviceCatalog.Parse(new StreamReader(stream!).ReadToEnd(), errors);
        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(0x0951, 0x1718, "HyperX Cloud II Wireless")]
    [InlineData(0x03F0, 0x0188, "HyperX Cloud Alpha Wireless")]
    [InlineData(0x0951, 0x1234, "HyperX Headset (1234)")]
    [InlineData(0x03F0, 0x0ABC, "HyperX Headset (0ABC)")]
    public void HyperX_names_match_the_old_product_table(int vid, int pid, string name)
    {
        var entry = BatteryDeviceCatalog.BuiltIn.Find(BatteryProtocols.HyperX, (ushort)vid, (ushort)pid);
        Assert.NotNull(entry);
        Assert.Equal(name, entry!.DisplayName((ushort)pid));
        Assert.Equal(BatteryDeviceKind.Headset, entry.Kind);
    }

    [Fact]
    public void Other_vendors_are_not_matched()
    {
        Assert.Null(BatteryDeviceCatalog.BuiltIn.Find(BatteryProtocols.HyperX, 0x046D, 0xC539));
        Assert.Null(BatteryDeviceCatalog.BuiltIn.Find(BatteryProtocols.Compx, 0x25A7, 0x0001));
    }

    [Fact]
    public void Exact_product_wins_over_vendor_wide_entry()
    {
        var catalog = BatteryDeviceCatalog.Parse("""
            { "devices": [
                { "protocol": "hyperx", "vid": "0951", "name": "Any ({pid})" },
                { "protocol": "hyperx", "vid": "0951", "pid": "00AA", "name": "Exact" }
            ] }
            """);
        Assert.Equal("Exact", catalog.Find("hyperx", 0x0951, 0x00AA)!.Name);
        Assert.Equal("Any (00BB)", catalog.Find("hyperx", 0x0951, 0x00BB)!.DisplayName(0x00BB));
    }

    [Fact]
    public void Invalid_entries_are_skipped_and_reported()
    {
        var errors = new List<string>();
        var catalog = BatteryDeviceCatalog.Parse("""
            {
              // comments and trailing commas are allowed
              "devices": [
                { "protocol": "compx", "vid": "25A7", "pid": "FA7C", "name": "Good", "kind": "mouse" },
                { "protocol": "telepathy", "vid": "25A7", "pid": "FA7C", "name": "Bad protocol" },
                { "protocol": "compx", "vid": "25A", "name": "Short vid" },
                { "protocol": "compx", "vid": "25A7", "pid": "XYZW", "name": "Bad pid" },
                { "protocol": "compx", "vid": "25A7", "pid": "FA7C" },
                { "protocol": "compx", "vid": "25A7", "pid": "FA7C", "name": "Bad kind", "kind": "toaster" },
                { "protocol": "compx", "vid": "25A7", "pid": "FA7C", "name": "Bad id", "id": "Has Spaces" },
                "not an object",
              ],
            }
            """, errors);
        Assert.Single(catalog.Entries);
        Assert.Equal("Good", catalog.Entries[0].Name);
        Assert.Equal(7, errors.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{ \"devices\": 5 }")]
    public void Broken_files_give_an_empty_catalog(string json)
    {
        var errors = new List<string>();
        Assert.Empty(BatteryDeviceCatalog.Parse(json, errors).Entries);
        Assert.Single(errors);
    }

    [Fact]
    public void User_entries_replace_and_extend_the_built_in_ones()
    {
        var builtIn = BatteryDeviceCatalog.Parse("""
            { "devices": [
                { "protocol": "compx", "vid": "25A7", "pid": "FA7C", "name": "Old name", "kind": "mouse" },
                { "protocol": "compx", "vid": "25A7", "pid": "FA7B", "name": "Wired", "wired": true }
            ] }
            """);
        var user = BatteryDeviceCatalog.Parse("""
            { "devices": [
                { "protocol": "compx", "vid": "25A7", "pid": "FA7C", "name": "New name", "kind": "mouse" },
                { "protocol": "compx", "vid": "1234", "pid": "5678", "name": "Added" }
            ] }
            """);

        var merged = builtIn.MergedWith(user);
        Assert.Equal(3, merged.Entries.Count);
        Assert.Equal("New name", merged.Find("compx", 0x25A7, 0xFA7C)!.Name);
        Assert.Equal("Added", merged.Find("compx", 0x1234, 0x5678)!.Name);
        Assert.True(merged.Find("compx", 0x25A7, 0xFA7B)!.Wired);
    }
}
