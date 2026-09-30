using System.Windows;
using CustomDock.Dock;

namespace CustomDock.Tests;

/// <summary>Drag data holds only strings: other objects would need the BinaryFormatter .NET no longer has.</summary>
public class DockDragDataTests
{
    [Theory]
    [InlineData(DockDragHelper.ItemFormat, "a1b2c3")]
    [InlineData(DockDragHelper.RunningAppFormat, "exe:c:\\program files\\app\\app.exe")]
    public void String_data_round_trips(string format, string value)
    {
        var data = DockDragHelper.StringData(format, value);

        Assert.IsType<string>(data.GetData(format));
        Assert.Equal(value, DockDragHelper.ReadString(data, format));
    }

    [Fact]
    public void Other_formats_and_non_strings_read_as_null()
    {
        var data = DockDragHelper.StringData(DockDragHelper.ItemFormat, "id");
        Assert.Null(DockDragHelper.ReadString(data, DockDragHelper.RunningAppFormat));

        var files = new DataObject(DataFormats.FileDrop, new[] { "c:\\a.txt" });
        Assert.Null(DockDragHelper.ReadString(files, DataFormats.FileDrop));
    }
}
