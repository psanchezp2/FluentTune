using NAudio.CoreAudioApi;

namespace FluentTune.Services;

/// <summary>Reads and controls the default playback device's master volume via NAudio (WASAPI).</summary>
public sealed class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private MMDevice? _device;

    /// <summary>Master volume as a scalar 0..1 (fires on external changes too).</summary>
    public event Action<float>? VolumeChanged;

    public bool IsAvailable => _device is not null;

    public VolumeService()
    {
        _enumerator = new MMDeviceEnumerator();
        try
        {
            _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
        }
        catch
        {
            _device = null; // No output device — volume control simply stays inert.
        }
    }

    private void OnVolumeNotification(AudioVolumeNotificationData data) => VolumeChanged?.Invoke(data.MasterVolume);

    public float GetVolume()
    {
        try { return _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f; }
        catch { return 0f; }
    }

    public void SetVolume(float scalar)
    {
        if (_device is null) return;
        try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(scalar, 0f, 1f); }
        catch { /* device may have vanished */ }
    }

    public void Dispose()
    {
        try
        {
            if (_device is not null)
                _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        }
        catch { }
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
