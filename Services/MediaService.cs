using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace FluentTune.Services;

/// <summary>Immutable snapshot of whatever is playing right now, system-wide.</summary>
public sealed class NowPlayingInfo
{
    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public bool IsPlaying { get; init; }
    public bool HasMedia { get; init; }
    public BitmapImage? Thumbnail { get; init; }
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public bool CanSeek { get; init; }

    public static NowPlayingInfo Empty { get; } = new() { HasMedia = false };
}

/// <summary>Lightweight position/duration update (no track metadata or artwork reload).</summary>
public sealed class TimelineInfo
{
    public TimeSpan Position { get; init; }
    public TimeSpan Duration { get; init; }
    public bool CanSeek { get; init; }
    public bool IsPlaying { get; init; }
}

/// <summary>
/// Wraps the Windows GlobalSystemMediaTransportControls (GSMTC) API so we can read and
/// control whatever app is currently playing audio (Spotify, a browser, etc.).
/// </summary>
public sealed class MediaService
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;

    /// <summary>Full update — track, playback state, artwork, timeline. (Background thread.)</summary>
    public event Action<NowPlayingInfo>? NowPlayingChanged;

    /// <summary>Cheap timeline-only update, e.g. the source app reported a new position.</summary>
    public event Action<TimelineInfo>? TimelineChanged;

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += (_, _) => HookCurrentSession();
        HookCurrentSession();
    }

    private void HookCurrentSession()
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnFullChanged;
            _session.PlaybackInfoChanged -= OnFullChanged;
            _session.TimelinePropertiesChanged -= OnTimelineNative;
        }

        _session = _manager?.GetCurrentSession();

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnFullChanged;
            _session.PlaybackInfoChanged += OnFullChanged;
            _session.TimelinePropertiesChanged += OnTimelineNative;
        }

        _ = UpdateAsync();
    }

    private void OnFullChanged(GlobalSystemMediaTransportControlsSession sender, object args) => _ = UpdateAsync();
    private void OnTimelineNative(GlobalSystemMediaTransportControlsSession sender, object args) => RaiseTimeline();

    private void RaiseTimeline()
    {
        var session = _session;
        if (session is null) return;

        try
        {
            var tl = session.GetTimelineProperties();
            var pb = session.GetPlaybackInfo();
            TimelineChanged?.Invoke(new TimelineInfo
            {
                Position = Clamp(tl.Position - tl.StartTime),
                Duration = Clamp(tl.EndTime - tl.StartTime),
                CanSeek = pb?.Controls.IsPlaybackPositionEnabled ?? false,
                IsPlaying = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
            });
        }
        catch { /* session churn — ignore */ }
    }

    private async Task UpdateAsync()
    {
        var session = _session;
        if (session is null)
        {
            NowPlayingChanged?.Invoke(NowPlayingInfo.Empty);
            return;
        }

        try
        {
            var props = await session.TryGetMediaPropertiesAsync();
            var pb = session.GetPlaybackInfo();
            var tl = session.GetTimelineProperties();
            var thumb = await LoadThumbnailAsync(props?.Thumbnail);

            NowPlayingChanged?.Invoke(new NowPlayingInfo
            {
                Title = props?.Title ?? "",
                Artist = props?.Artist ?? "",
                IsPlaying = pb?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                HasMedia = !string.IsNullOrWhiteSpace(props?.Title),
                Thumbnail = thumb,
                Position = Clamp(tl.Position - tl.StartTime),
                Duration = Clamp(tl.EndTime - tl.StartTime),
                CanSeek = pb?.Controls.IsPlaybackPositionEnabled ?? false,
            });
        }
        catch
        {
            // Transient errors (e.g. session disappearing mid-read) — ignore this tick.
        }
    }

    private static TimeSpan Clamp(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;

    private static async Task<BitmapImage?> LoadThumbnailAsync(IRandomAccessStreamReference? thumbRef)
    {
        if (thumbRef is null) return null;

        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            if (stream is null || stream.Size == 0) return null;

            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            var bytes = new byte[stream.Size];
            reader.ReadBytes(bytes);

            using var ms = new MemoryStream(bytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze(); // Frozen => safe to hand across to the UI thread.
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public Task TogglePlayPauseAsync() => _session?.TryTogglePlayPauseAsync().AsTask() ?? Task.CompletedTask;
    public Task NextAsync() => _session?.TrySkipNextAsync().AsTask() ?? Task.CompletedTask;
    public Task PreviousAsync() => _session?.TrySkipPreviousAsync().AsTask() ?? Task.CompletedTask;
    public Task SeekAsync(TimeSpan position) => _session?.TryChangePlaybackPositionAsync(position.Ticks).AsTask() ?? Task.CompletedTask;
}
