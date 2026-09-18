using System.Runtime.InteropServices;
using System.Windows;
using CustomDock.Core;
using CustomDock.Native;

namespace CustomDock.Services;

public sealed record AudioDeviceInfo(string Id, string Name, EndpointFormFactor FormFactor, bool IsDefault)
{
    public bool IsHeadphone => FormFactor is EndpointFormFactor.Headphones or EndpointFormFactor.Headset
        || Name.Contains("kulaklık", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("headphone", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("headset", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("earphone", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("buds", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("cloud", StringComparison.OrdinalIgnoreCase);
}

public sealed class AudioService : IMMNotificationClient, IAudioEndpointVolumeCallback, IDisposable
{
    private readonly IMMDeviceEnumerator? _enumerator;
    private IMMDevice? _defaultDevice;
    private IAudioEndpointVolume? _endpointVolume;
    private bool _disposed;

    public AudioService()
    {
        try
        {
            _enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            _enumerator.RegisterEndpointNotificationCallback(this);
            RefreshDevices();
            HookDefaultVolume();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AudioService başlatılamadı");
        }
    }

    public IReadOnlyList<AudioDeviceInfo> Devices { get; private set; } = Array.Empty<AudioDeviceInfo>();

    public AudioDeviceInfo? DefaultDevice { get; private set; }

    public float Volume
    {
        get
        {
            if (_endpointVolume is null) return 0;
            try
            {
                _endpointVolume.GetMasterVolumeLevelScalar(out float level);
                return level;
            }
            catch { return 0; }
        }
        set
        {
            if (_endpointVolume is null) return;
            try
            {
                float level = Math.Clamp(value, 0f, 1f);
                Guid context = Guid.Empty;
                _endpointVolume.SetMasterVolumeLevelScalar(level, ref context);
                VolumeChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Ses seviyesi ayarlanamadı");
            }
        }
    }

    public int VolumePercent
    {
        get => (int)Math.Round(Volume * 100f);
        set => Volume = value / 100f;
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
                VolumeChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Sessize alma durumu değiştirilemedi");
            }
        }
    }

    public event EventHandler? VolumeChanged;
    public event EventHandler? DevicesChanged;

    public void ToggleMute() => IsMuted = !IsMuted;

    public void StepVolume(float delta) => Volume += delta;

    public void SetDefaultDevice(string deviceId)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            policy.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policy.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            RefreshDevices();
            HookDefaultVolume();
        }
        catch (Exception ex)
        {
            Log.Error(ex, $"Varsayılan ses aygıtı ayarlanamadı: {deviceId}");
        }
    }

    public void CycleNextDevice()
    {
        if (Devices.Count < 2) return;
        int index = -1;
        for (int i = 0; i < Devices.Count; i++)
        {
            if (Devices[i].IsDefault)
            {
                index = i;
                break;
            }
        }
        int nextIndex = (index + 1) % Devices.Count;
        SetDefaultDevice(Devices[nextIndex].Id);
    }

    // ------------------------------------------------------------------ Uygulama bazlı ses oturumları (EarTrumpet tarzı)

    public sealed record AudioSessionInfo(
        uint ProcessId, string DisplayName, string ProcessName,
        float Volume, bool IsMuted, bool IsSystemSounds)
    {
        internal ISimpleAudioVolume? VolumeControl { get; init; }
    }

    public List<AudioSessionInfo> GetAudioSessions()
    {
        var result = new List<AudioSessionInfo>();
        if (_defaultDevice is null) return result;

        try
        {
            var iid = typeof(IAudioSessionManager2).GUID;
            if (_defaultDevice.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) != 0 || obj is not IAudioSessionManager2 mgr)
                return result;

            if (mgr.GetSessionEnumerator(out var enumerator) != 0)
            {
                Marshal.ReleaseComObject(mgr);
                return result;
            }

            enumerator.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (enumerator.GetSession(i, out var session) != 0) continue;
                try
                {
                    session.GetState(out var state);
                    if (state == AudioSessionState.Expired) continue;

                    session.GetProcessId(out uint pid);
                    bool isSystem = session.IsSystemSoundsSession() == 0;

                    string displayName = "";
                    session.GetDisplayName(out displayName);

                    string processName = "";
                    if (!isSystem && pid > 0)
                    {
                        try
                        {
                            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
                            processName = proc.ProcessName;
                            if (string.IsNullOrEmpty(displayName))
                                displayName = proc.MainWindowTitle;
                            if (string.IsNullOrEmpty(displayName))
                                displayName = processName;
                        }
                        catch { processName = $"PID {pid}"; }
                    }

                    if (isSystem) displayName = "Sistem Sesleri";
                    if (string.IsNullOrWhiteSpace(displayName)) displayName = processName;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    // ISimpleAudioVolume al
                    ISimpleAudioVolume? vol = null;
                    if (session is ISimpleAudioVolume sav) vol = sav;

                    float volume = 1f;
                    bool muted = false;
                    if (vol is not null)
                    {
                        vol.GetMasterVolume(out volume);
                        vol.GetMute(out muted);
                    }

                    result.Add(new AudioSessionInfo(pid, displayName, processName, volume, muted, isSystem)
                    {
                        VolumeControl = vol,
                    });
                }
                catch { }
            }
            Marshal.ReleaseComObject(enumerator);
            Marshal.ReleaseComObject(mgr);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ses oturumları listelenemedi");
        }

        return result;
    }

    public void SetSessionVolume(AudioSessionInfo session, float volume)
    {
        if (session.VolumeControl is null) return;
        Guid ctx = Guid.Empty;
        session.VolumeControl.SetMasterVolume(Math.Clamp(volume, 0f, 1f), ref ctx);
    }

    public void SetSessionMute(AudioSessionInfo session, bool mute)
    {
        if (session.VolumeControl is null) return;
        Guid ctx = Guid.Empty;
        session.VolumeControl.SetMute(mute, ref ctx);
    }


    private void RefreshDevices()
    {
        if (_enumerator is null) return;

        var list = new List<AudioDeviceInfo>();
        string? defaultId = null;

        try
        {
            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out var defaultDev) == 0)
            {
                defaultDev.GetId(out defaultId);
                Marshal.ReleaseComObject(defaultDev);
            }
        }
        catch { }

        try
        {
            if (_enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out var collection) == 0)
            {
                collection.GetCount(out uint count);
                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var dev) == 0)
                    {
                        dev.GetId(out string id);
                        string name = GetDeviceFriendlyName(dev);
                        EndpointFormFactor formFactor = GetDeviceFormFactor(dev);
                        bool isDefault = string.Equals(id, defaultId, StringComparison.OrdinalIgnoreCase);

                        var info = new AudioDeviceInfo(id, name, formFactor, isDefault);
                        list.Add(info);
                        if (isDefault) DefaultDevice = info;

                        Marshal.ReleaseComObject(dev);
                    }
                }
                Marshal.ReleaseComObject(collection);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Ses aygıtları listelenemedi");
        }

        Devices = list;
        Application.Current?.Dispatcher.BeginInvoke(() => DevicesChanged?.Invoke(this, EventArgs.Empty));
    }

    private void HookDefaultVolume()
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
            if (_defaultDevice is not null)
            {
                Marshal.ReleaseComObject(_defaultDevice);
                _defaultDevice = null;
            }

            if (_enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eMultimedia, out _defaultDevice) == 0 && _defaultDevice is not null)
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (_defaultDevice.Activate(ref iid, 1 /* CLSCTX_INPROC_SERVER */, IntPtr.Zero, out var obj) == 0 && obj is IAudioEndpointVolume epv)
                {
                    _endpointVolume = epv;
                    _endpointVolume.RegisterControlChangeNotify(this);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Varsayılan ses seviyesi dinleyicisi kurulamadı");
        }

        Application.Current?.Dispatcher.BeginInvoke(() => VolumeChanged?.Invoke(this, EventArgs.Empty));
    }

    private static string GetDeviceFriendlyName(IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0)
            {
                var key = AudioPropertyKeys.PKEY_Device_FriendlyName;
                if (store.GetValue(ref key, out var pv) == 0 && pv.pwszVal != IntPtr.Zero)
                {
                    string name = Marshal.PtrToStringUni(pv.pwszVal) ?? "";
                    AudioPropertyKeys.PropVariantClear(ref pv);
                    Marshal.ReleaseComObject(store);
                    return string.IsNullOrWhiteSpace(name) ? "Ses Aygıtı" : name;
                }
                Marshal.ReleaseComObject(store);
            }
        }
        catch { }
        return "Ses Aygıtı";
    }

    private static EndpointFormFactor GetDeviceFormFactor(IMMDevice device)
    {
        try
        {
            if (device.OpenPropertyStore(0 /* STGM_READ */, out var store) == 0)
            {
                var key = AudioPropertyKeys.PKEY_AudioEndpoint_FormFactor;
                if (store.GetValue(ref key, out var pv) == 0)
                {
                    uint formFactor = pv.uintVal;
                    AudioPropertyKeys.PropVariantClear(ref pv);
                    Marshal.ReleaseComObject(store);
                    return (EndpointFormFactor)formFactor;
                }
                Marshal.ReleaseComObject(store);
            }
        }
        catch { }
        return EndpointFormFactor.UnknownFormFactor;
    }

    // IMMNotificationClient
    int IMMNotificationClient.OnDeviceStateChanged(string pwstrDeviceId, DeviceState dwNewState)
    {
        RefreshDevices();
        return 0;
    }

    int IMMNotificationClient.OnDeviceAdded(string pwstrDeviceId)
    {
        RefreshDevices();
        return 0;
    }

    int IMMNotificationClient.OnDeviceRemoved(string pwstrDeviceId)
    {
        RefreshDevices();
        return 0;
    }

    int IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
    {
        if (flow == EDataFlow.eRender && (role == ERole.eConsole || role == ERole.eMultimedia))
        {
            RefreshDevices();
            HookDefaultVolume();
        }
        return 0;
    }

    int IMMNotificationClient.OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key) => 0;

    // IAudioEndpointVolumeCallback
    int IAudioEndpointVolumeCallback.OnNotify(IntPtr pNotify)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => VolumeChanged?.Invoke(this, EventArgs.Empty));
        return 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (_endpointVolume is not null)
            {
                _endpointVolume.UnregisterControlChangeNotify(this);
                Marshal.ReleaseComObject(_endpointVolume);
                _endpointVolume = null;
            }
            if (_defaultDevice is not null)
            {
                Marshal.ReleaseComObject(_defaultDevice);
                _defaultDevice = null;
            }
            if (_enumerator is not null)
            {
                _enumerator.UnregisterEndpointNotificationCallback(this);
                Marshal.ReleaseComObject(_enumerator);
            }
        }
        catch { }
    }
}
