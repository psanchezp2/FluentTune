using NAudio.Dsp;
using NAudio.Wave;

namespace FluentTune.Services;

/// <summary>
/// Captures the system audio output (WASAPI loopback) and turns it into a small set of
/// frequency-band magnitudes via FFT, so the UI can draw a live spectrum.
/// </summary>
public sealed class AudioSpectrumService : IDisposable
{
    private const int FftSize = 2048; // must be 2^FftM
    private const int FftM = 11;

    private WasapiLoopbackCapture? _capture;
    private readonly float[] _ring = new float[FftSize];
    private int _ringPos;
    private readonly object _lock = new();

    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly double[] _hann = new double[FftSize];

    public bool IsRunning { get; private set; }

    public AudioSpectrumService()
    {
        for (int i = 0; i < FftSize; i++)
            _hann[i] = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
    }

    public void Start()
    {
        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnData;
            _capture.StartRecording();
            IsRunning = true;
        }
        catch
        {
            IsRunning = false;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var fmt = _capture!.WaveFormat;
        int ch = Math.Max(1, fmt.Channels);
        int bps = fmt.BitsPerSample / 8;
        bool isFloat = bps == 4; // loopback is virtually always 32-bit IEEE float
        int frameBytes = bps * ch;
        if (frameBytes == 0) return;
        int frames = e.BytesRecorded / frameBytes;

        lock (_lock)
        {
            for (int i = 0; i < frames; i++)
            {
                float mono = 0;
                int baseIdx = i * frameBytes;
                for (int c = 0; c < ch; c++)
                {
                    int idx = baseIdx + c * bps;
                    if (isFloat) mono += BitConverter.ToSingle(e.Buffer, idx);
                    else if (bps == 2) mono += BitConverter.ToInt16(e.Buffer, idx) / 32768f;
                }
                _ring[_ringPos] = mono / ch;
                _ringPos = (_ringPos + 1) % FftSize;
            }
        }
    }

    /// <summary>Fill <paramref name="bars"/> with per-band magnitudes (log-spaced frequencies).</summary>
    public void GetBars(float[] bars)
    {
        lock (_lock)
        {
            int start = _ringPos;
            for (int i = 0; i < FftSize; i++)
            {
                float s = _ring[(start + i) % FftSize];
                _fft[i].X = (float)(s * _hann[i]);
                _fft[i].Y = 0;
            }
        }

        FastFourierTransform.FFT(true, FftM, _fft);

        int bins = FftSize / 2;
        int n = bars.Length;
        double minBin = 2, maxBin = bins - 1;

        for (int b = 0; b < n; b++)
        {
            double f0 = minBin * Math.Pow(maxBin / minBin, (double)b / n);
            double f1 = minBin * Math.Pow(maxBin / minBin, (double)(b + 1) / n);
            int i0 = (int)Math.Floor(f0);
            int i1 = Math.Max(i0 + 1, (int)Math.Ceiling(f1));
            i1 = Math.Min(i1, bins);

            double peak = 0;
            for (int i = i0; i < i1; i++)
            {
                double mag = Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                if (mag > peak) peak = mag;
            }
            bars[b] = (float)Math.Sqrt(peak); // perceptual compression; renderer applies auto-gain
        }
    }

    /// <summary>Overall loudness right now (RMS of the captured samples), for reactive UI.</summary>
    public float GetLevel()
    {
        double sum = 0;
        lock (_lock)
        {
            for (int i = 0; i < FftSize; i++)
                sum += _ring[i] * _ring[i];
        }
        return (float)Math.Sqrt(sum / FftSize);
    }

    public void Dispose()
    {
        try { _capture?.StopRecording(); } catch { }
        try { _capture?.Dispose(); } catch { }
        _capture = null;
        IsRunning = false;
    }
}
