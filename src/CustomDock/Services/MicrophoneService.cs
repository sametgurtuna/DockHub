using System.Runtime.InteropServices;
using System.Windows;
using CustomDock.Core;
using CustomDock.Native;
using Microsoft.Win32;

namespace CustomDock.Services;

/// <summary>
/// Default recording device: mute state, device list, and whether an app is using the microphone right now
/// (read from Windows' privacy "recent activity" records, which is how the Windows taskbar shows its mic icon).
/// Started by the first subscriber.
/// </summary>
public sealed class MicrophoneService : IMMNotificationClient, IAudioEndpointVolumeCallback
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";

    private IMMDeviceEnumerator? _enumerator;
    private IAudioEndpointVolume? _endpointVolume;
    private RegistryWatcher? _usageWatcher;
    private bool _started;

    public IReadOnlyList<AudioDeviceInfo> Devices { get; private set; } = Array.Empty<AudioDeviceInfo>();

    public AudioDeviceInfo? DefaultDevice { get; private set; }

    /// <summary>Apps currently recording (friendly names), empty when the microphone is idle.</summary>
    public IReadOnlyList<string> AppsInUse { get; private set; } = Array.Empty<string>();

    public bool IsInUse => AppsInUse.Count > 0;

    /// <summary>Raised on the UI thread when mute, devices or usage change.</summary>
    public event Action? Changed;

    public void EnsureStarted()
    {
        if (_started) return;
        _started = true;
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(this);
            RefreshDevices();
            HookDefaultDevice();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Microphone service could not be initialized");
        }

        RefreshUsage();
        _usageWatcher = new RegistryWatcher(RegistryHive.CurrentUser, ConsentStore, subtree: true);
        _usageWatcher.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RefreshUsage();
            Changed?.Invoke();
        });
        _usageWatcher.Start();
    }

    public bool IsMuted
    {
        get
        {
            if (_endpointVolume is null) return false;
            try
            {
                _endpointVolume.GetMute(out bool muted);
                return muted;
            }
            catch { return false; }
        }
        set
        {
            if (_endpointVolume is null) return;
            try
            {
                Guid context = Guid.Empty;
                _endpointVolume.SetMute(value, ref context);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to change microphone mute state");
            }
            Changed?.Invoke();
        }
    }

    public void ToggleMute()
    {
        EnsureStarted();
        IsMuted = !IsMuted;
        Log.Info($"Microphone {(IsMuted ? "muted" : "unmuted")}.");
    }

    public void SetDefaultDevice(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policy.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            RefreshDevices();
            HookDefaultDevice();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Failed to set default recording device: {deviceId}");
        }
    }

    private void RefreshUsage()
    {
        var apps = new List<string>();
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(ConsentStore);
            if (root is not null)
            {
                foreach (var name in root.GetSubKeyNames())
                {
                    using var key = root.OpenSubKey(name);
                    if (key is null) continue;
                    if (name.Equals("NonPackaged", StringComparison.OrdinalIgnoreCase))
                    {
                        // Desktop apps: one subkey per executable path with '#' for '\'.
                        foreach (var exe in key.GetSubKeyNames())
                        {
                            using var app = key.OpenSubKey(exe);
                            if (app is not null && IsActive(app))
                                apps.Add(Path.GetFileNameWithoutExtension(exe.Replace('#', '\\')));
                        }
                    }
                    else if (IsActive(key))
                    {
                        // Packaged apps: package family name ("Microsoft.WindowsCamera_8wekyb3d8bbwe").
                        apps.Add(name.Split('_')[0].Split('.').Last());
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug($"Microphone usage could not be read: {ex.Message}");
        }
        AppsInUse = apps.Where(a => !a.Equals("DockHub", StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Started and not yet stopped.</summary>
    private static bool IsActive(RegistryKey key)
        => key.GetValue("LastUsedTimeStop") is long stop && stop == 0
           && key.GetValue("LastUsedTimeStart") is long start && start != 0;

    private void RefreshDevices()
    {
        if (_enumerator is null) return;
        var list = new List<AudioDeviceInfo>();
        string? defaultId = null;
        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out var device) == 0)
            {
                device.GetId(out defaultId);
                Marshal.ReleaseComObject(device);
            }
        }
        catch { /* no recording device */ }

        AudioDeviceInfo? defaultDevice = null;
        try
        {
            if (_enumerator.EnumAudioEndpoints(EDataFlow.eCapture, DeviceState.Active, out var collection) == 0)
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) != 0) continue;
                    device.GetId(out string id);
                    var info = new AudioDeviceInfo(id, FriendlyName(device), EndpointFormFactor.Microphone,
                        string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase));
                    list.Add(info);
                    if (info.IsDefault) defaultDevice = info;
                    Marshal.ReleaseComObject(device);
                }
                Marshal.ReleaseComObject(collection);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to enumerate recording devices");
        }
        Devices = list;
        DefaultDevice = defaultDevice;
    }

    private void HookDefaultDevice()
    {
        if (_enumerator is null) return;
        try
        {
            if (_endpointVolume is not null)
            {
                _endpointVolume.UnregisterControlChangeNotify(this);
                Marshal.ReleaseComObject(_endpointVolume);
                _endpointVolume = null;
            }
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eCapture, ERole.eConsole, out var device) == 0 && device is not null)
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (device.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) == 0 && obj is IAudioEndpointVolume volume)
                {
                    _endpointVolume = volume;
                    _endpointVolume.RegisterControlChangeNotify(this);
                }
                Marshal.ReleaseComObject(device);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to watch the recording device");
        }
        RaiseChanged();
    }

    private static string FriendlyName(IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0)
            {
                var key = AudioPropertyKeys.PKEY_Device_FriendlyName;
                string? name = null;
                if (store.GetValue(ref key, out var value) == 0 && value.pwszVal != IntPtr.Zero)
                {
                    name = Marshal.PtrToStringUni(value.pwszVal);
                    AudioPropertyKeys.PropVariantClear(ref value);
                }
                Marshal.ReleaseComObject(store);
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { /* fall through */ }
        return L.T("Microphone");
    }

    private void RaiseChanged() => Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());

    int IMMNotificationClient.OnDeviceStateChanged(string pwstrDeviceId, DeviceState dwNewState) => DeviceListChanged();

    int IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId) => DeviceListChanged();

    int IMMNotificationClient.OnDeviceRemoved(string pwstrDeviceId) => DeviceListChanged();

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
    {
        if (flow == EDataFlow.eCapture && role == ERole.eConsole)
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                RefreshDevices();
                HookDefaultDevice();
            });
        return 0;
    }

    int IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key) => 0;

    private int DeviceListChanged()
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            RefreshDevices();
            Changed?.Invoke();
        });
        return 0;
    }

    int IAudioEndpointVolumeCallback.OnNotify(IntPtr pNotify)
    {
        RaiseChanged();
        return 0;
    }
}
