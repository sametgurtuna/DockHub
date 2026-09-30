using System.Runtime.InteropServices;
using CustomDock.Core;
using Microsoft.Win32;

namespace CustomDock.Services;

/// <summary>Windows' power mode (Settings › System › Power): the overlay on the Balanced power plan.</summary>
public enum PowerMode { BestEfficiency, Balanced, BestPerformance }

/// <summary>The power mode overlays' GUIDs, as Windows' Settings sets them.</summary>
public static class PowerModeOverlays
{
    public static readonly Guid BestEfficiency = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    public static readonly Guid Balanced = Guid.Empty;
    public static readonly Guid BestPerformance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    public static Guid OverlayOf(PowerMode mode) => mode switch
    {
        PowerMode.BestEfficiency => BestEfficiency,
        PowerMode.BestPerformance => BestPerformance,
        _ => Balanced,
    };

    /// <summary>The mode of an overlay; null for one Windows' Settings doesn't offer (set by another tool).</summary>
    public static PowerMode? FromOverlay(Guid overlay)
    {
        if (overlay == BestEfficiency) return PowerMode.BestEfficiency;
        if (overlay == Balanced) return PowerMode.Balanced;
        if (overlay == BestPerformance) return PowerMode.BestPerformance;
        return null;
    }

    public static string EnglishName(PowerMode mode) => mode switch
    {
        PowerMode.BestEfficiency => "Best power efficiency",
        PowerMode.BestPerformance => "Best performance",
        _ => "Balanced",
    };
}

/// <summary>
/// Reads and sets the power mode and tells whether the PC runs on battery and whether battery saver is on. Setting the
/// mode uses the same call as Windows' Settings; when it fails (another power plan than Balanced, or a PC that manages
/// power itself), the widget only shows the state and opens Settings.
/// </summary>
public sealed class PowerModeService
{
    private bool _started;

    public PowerMode? Mode { get; private set; }

    /// <summary>False when the mode can't be read here.</summary>
    public bool Supported { get; private set; } = true;

    public bool OnBattery { get; private set; }

    public bool BatterySaver { get; private set; }

    public bool HasBattery { get; private set; }

    public event Action? Changed;

    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        SystemEvents.PowerModeChanged += (_, _) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
        // Settings, battery saver and plan changes raise nothing a desktop app can hear cheaply.
        AppServices.Clock.MinuteTick += (_, _) => Refresh();
        Refresh();
    }

    public void Refresh()
    {
        PowerMode? mode = null;
        bool supported;
        try
        {
            supported = Native.PowerGetEffectiveOverlayScheme(out var overlay) == 0;
            if (supported) mode = PowerModeOverlays.FromOverlay(overlay);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            supported = false;
        }

        bool onBattery = false, saver = false, hasBattery = false;
        if (Native.GetSystemPowerStatus(out var status))
        {
            onBattery = status.ACLineStatus == 0;
            saver = (status.SystemStatusFlag & 1) != 0;
            hasBattery = status.BatteryFlag != 128 && status.BatteryFlag != 255;
        }

        if (mode == Mode && supported == Supported && onBattery == OnBattery && saver == BatterySaver && hasBattery == HasBattery) return;
        (Mode, Supported, OnBattery, BatterySaver, HasBattery) = (mode, supported, onBattery, saver, hasBattery);
        Changed?.Invoke();
    }

    /// <summary>Sets the mode; false when Windows refused (the caller then opens Settings).</summary>
    public bool TrySet(PowerMode mode)
    {
        bool done;
        try
        {
            done = Native.PowerSetActiveOverlayScheme(PowerModeOverlays.OverlayOf(mode)) == 0;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            done = false;
        }
        if (!done) Log.Info($"Power mode {mode} could not be set.");
        Refresh();
        return done && Mode == mode;
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("powrprof.dll")]
        public static extern uint PowerGetEffectiveOverlayScheme(out Guid effectiveOverlayGuid);

        // The GUID goes by value, as declared by Windows (not a pointer).
        [DllImport("powrprof.dll")]
        public static extern uint PowerSetActiveOverlayScheme(Guid overlaySchemeGuid);

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }
}
