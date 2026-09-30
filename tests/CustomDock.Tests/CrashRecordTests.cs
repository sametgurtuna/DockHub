using CustomDock.Core;

namespace CustomDock.Tests;

/// <summary>Crash events as Windows writes them to the Application log (fixtures keep the real layout).</summary>
public class CrashRecordTests
{
    private static readonly DateTime Time = new(2026, 9, 28, 14, 2, 0, DateTimeKind.Local);

    private static CrashRecord Parse(string fixture)
        => CrashRecord.Parse(TestEnvironment.Fixture("crash/" + fixture), Time) ?? throw new Xunit.Sdk.XunitException("not parsed");

    [Fact]
    public void Application_error_text_gives_module_code_and_offset()
    {
        var record = Parse("application-error-1000.txt");

        Assert.Equal(CrashRecord.ApplicationErrorSource, record.Source);
        Assert.Equal("0.9.1.0", record.AppVersion);
        Assert.Equal("coreclr.dll", record.Module);
        Assert.Equal("8.0.3126.42015", record.ModuleVersion);
        Assert.Equal("c0000005", record.ExceptionCode);
        Assert.Equal("0x1d45dc", record.Offset);
        Assert.Equal("coreclr.dll c0000005 at 0x1d45dc", record.Summary());
    }

    [Fact]
    public void Application_error_event_data_reads_the_same_in_every_language()
    {
        var data = new[]
        {
            "DockHub.exe", "0.9.1.0", "68d90000", "coreclr.dll", "8.0.3126.42015", "68b8e8c3", "c0000005",
            "00000000001d45dc", "2f14", "01dc30b5a1b2c3d4", @"C:\Program Files\DockHub\DockHub.exe",
            @"C:\Program Files\DockHub\coreclr.dll", "3f8d2a1e-9c4b-4e0a-8f5d-1b2c3d4e5f60", "", "",
        };
        var record = CrashRecord.FromApplicationError(data, Time);

        Assert.NotNull(record);
        Assert.Equal(Parse("application-error-1000.txt").Summary(), record!.Summary());
        Assert.DoesNotContain("Program Files", record.Report());
    }

    [Fact]
    public void Other_apps_are_ignored()
    {
        Assert.Null(CrashRecord.Parse(TestEnvironment.Fixture("crash/other-app-1000.txt"), Time));
        Assert.Null(CrashRecord.FromApplicationError(new[] { "explorer.exe", "10.0", "0", "ntdll.dll", "10.0", "0", "c0000409", "a1b2c" }, Time));
        Assert.Null(CrashRecord.Parse("Application: Discord.exe\nDescription: The process was terminated due to an unhandled exception.", Time));
    }

    [Fact]
    public void Unhandled_exception_keeps_the_type_and_frames_but_not_the_message()
    {
        var record = Parse("runtime-1026.txt");

        Assert.Equal(CrashRecord.RuntimeSource, record.Source);
        Assert.Equal("8.0.31", record.RuntimeVersion);
        Assert.Equal("unhandled exception", record.Description);
        Assert.Equal("System.IO.IOException", record.ExceptionType);
        Assert.Equal(CrashRecord.MaxStackFrames, record.Stack.Count);
        Assert.Equal("System.IO.File.WriteAllText(System.String, System.String)", record.Stack[1]);
        Assert.Equal("CustomDock.Widgets.NotesWidget.Save()", record.Stack[2]);

        string report = record.Report();
        Assert.DoesNotContain("samet", report, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@":\", report);
        Assert.DoesNotContain("being used by another process", report);
    }

    [Fact]
    public void FailFast_message_is_left_out()
    {
        var record = Parse("failfast-1025.txt");

        Assert.Equal("Environment.FailFast", record.Description);
        Assert.Equal("10.0.12", record.RuntimeVersion);
        Assert.Contains(record.Stack, f => f.Contains("StartCrashTest", StringComparison.Ordinal));
        Assert.DoesNotContain("samet", record.Report());
        Assert.DoesNotContain("--crash-test", record.Report());
    }

    [Fact]
    public void Internal_runtime_error_keeps_its_exit_code()
    {
        var record = Parse("internal-error-1023.txt");

        Assert.Equal("internal error in the .NET Runtime (exit code 80131506)", record.Description);
        Assert.Equal(record.Description, record.Summary());
    }

    [Fact]
    public void Events_of_one_crash_are_merged()
    {
        var native = Parse("application-error-1000.txt");
        var managed = Parse("runtime-1026.txt");
        managed.Time = Time.AddSeconds(-2);

        var merged = CrashRecord.Merge(new[] { native, managed });

        Assert.NotNull(merged);
        Assert.Equal(managed.Time, merged!.Time);
        Assert.Equal("0.9.1.0", merged.AppVersion);
        Assert.Equal("coreclr.dll", merged.Module);
        Assert.Equal("System.IO.IOException", merged.ExceptionType);
        Assert.Equal("coreclr.dll c0000005 at 0x1d45dc; System.IO.IOException at Microsoft.Win32.SafeHandles.SafeFileHandle.CreateFile(System.String, System.IO.FileMode, System.IO.FileAccess, System.IO.FileShare, System.IO.FileOptions)",
            merged.Summary());
        Assert.Null(CrashRecord.Merge(Array.Empty<CrashRecord>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage without fields")]
    [InlineData("Faulting application name: DockHub.exe")]
    public void Broken_text_does_not_throw(string? message)
    {
        var record = CrashRecord.Parse(message, Time);
        if (record is not null) Assert.Equal("no details", record.Summary());
    }

    [Fact]
    public void Odd_values_are_dropped_rather_than_passed_on()
    {
        var record = CrashRecord.FromApplicationError(
            new[] { "DockHub.exe", "0.9.1.0", "0", "coreclr.dll", "8.0", "0", "not hex", "0x0" }, Time);

        Assert.Null(record!.ExceptionCode);
        Assert.Equal("0x0", record.Offset);
        Assert.Null(CrashRecord.FromApplicationError(new[] { "DockHub.exe" }, Time));
    }

    [Fact]
    public void Record_survives_session_json()
    {
        var record = CrashRecord.Merge(new[] { Parse("application-error-1000.txt"), Parse("runtime-1026.txt") })!;
        string json = System.Text.Json.JsonSerializer.Serialize(record, JsonStore.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<CrashRecord>(json, JsonStore.Options)!;

        Assert.Equal(record.Report(), back.Report());
    }
}
