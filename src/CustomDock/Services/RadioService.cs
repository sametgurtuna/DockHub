using System.Windows;
using CustomDock.Core;
using Windows.Devices.Radios;

namespace CustomDock.Services;

/// <summary>
/// Wi-Fi and Bluetooth on/off through the Windows radio API (the same switches as Quick Settings). Starts on first use
/// and follows changes made elsewhere. When Windows refuses access the widget falls back to the Settings pages.
/// </summary>
public sealed class RadioService
{
    private readonly List<Radio> _radios = new();
    private Task? _start;

    /// <summary>Wi-Fi state: null when there is no Wi-Fi adapter (or access was denied).</summary>
    public bool? WiFi => StateOf(RadioKind.WiFi);

    public bool? Bluetooth => StateOf(RadioKind.Bluetooth);

    public bool AccessDenied { get; private set; }

    public event Action? Changed;

    public Task EnsureStartedAsync() => _start ??= StartAsync();

    private async Task StartAsync()
    {
        try
        {
            if (await Radio.RequestAccessAsync() != RadioAccessStatus.Allowed)
            {
                AccessDenied = true;
                Log.Info("Radio access was denied; Wi-Fi and Bluetooth buttons open Settings instead.");
            }
            foreach (var radio in await Radio.GetRadiosAsync())
            {
                if (radio.Kind is not (RadioKind.WiFi or RadioKind.Bluetooth)) continue;
                _radios.Add(radio);
                radio.StateChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
            }
        }
        catch (Exception ex)
        {
            AccessDenied = true;
            Log.Error(ex, "Couldn't read Wi-Fi and Bluetooth radios");
        }
        Changed?.Invoke();
    }

    private bool? StateOf(RadioKind kind)
    {
        var radio = _radios.FirstOrDefault(r => r.Kind == kind);
        return radio is null ? null : radio.State == RadioState.On;
    }

    /// <summary>Turns the radio on or off; false when Windows didn't allow it (the caller then opens Settings).</summary>
    public async Task<bool> ToggleAsync(RadioKind kind)
    {
        await EnsureStartedAsync();
        var radio = _radios.FirstOrDefault(r => r.Kind == kind);
        if (radio is null || AccessDenied) return false;
        try
        {
            var target = radio.State == RadioState.On ? RadioState.Off : RadioState.On;
            var result = await radio.SetStateAsync(target);
            Changed?.Invoke();
            return result == RadioAccessStatus.Allowed;
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Couldn't switch {kind}");
            return false;
        }
    }
}
