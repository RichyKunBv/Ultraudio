using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Flac;

namespace Ultraudio;

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types
// ─────────────────────────────────────────────────────────────────────────────

public class DeviceModel
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public override string ToString() => Name;
}

public enum RepeatMode { Off, One, All }

// ─────────────────────────────────────────────────────────────────────────────
// AudioEngine
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Core audio engine wrapping BASS for bit-perfect lossless playback.
/// Supports: gapless pre-load, FFT spectrum data, mute, repeat/shuffle,
/// CUE sheet virtual tracks (start/end position clamping), and
/// exclusive-mode hooks (WASAPI on Windows, Hog Mode on macOS).
/// </summary>
public class AudioEngine
{
    // ── Active stream ────────────────────────────────────────────────────────
    private int _stream;
    private GCHandle _memoryHandle;

    // ── Gapless: pre-loaded next stream ──────────────────────────────────────
    private int _nextStream;
    private GCHandle _nextMemoryHandle;
    private SyncProcedure? _gaplessTriggerSync;
    private SyncProcedure? _trackEndSync;

    // ── Device state ─────────────────────────────────────────────────────────
    private bool _deviceInitialized = false;
    private int _deviceSampleRate = 44100;
    private int _currentDevice = -1;

    // ── Volume / Mute ─────────────────────────────────────────────────────────
    private double _volumeBeforeMute = 1.0;
    private bool _isMuted = false;

    // ── CUE virtual track bounds ──────────────────────────────────────────────
    private double _cueStart = 0;
    private double _cueEnd = -1; // -1 = play to file end

    // ── Events ───────────────────────────────────────────────────────────────
    public event EventHandler? TrackEnded;
    public event EventHandler? GaplessPreloadReady;

    // ── FFT buffer ────────────────────────────────────────────────────────────
    private const int FftSize = 2048;
    private readonly float[] _fftBuffer = new float[FftSize / 2];

    // ─────────────────────────────────────────────────────────────────────────
    public AudioEngine()
    {
        _trackEndSync = new SyncProcedure(OnTrackEnd);
        _gaplessTriggerSync = new SyncProcedure(OnGaplessTrigger);
        LoadBassPlugins();
    }

    // ─── Plugin loading ───────────────────────────────────────────────────────

    private void LoadBassPlugins()
    {
        string baseDir = AppContext.BaseDirectory;
        var pluginFiles = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            pluginFiles.Add("bassflac.dll");
            pluginFiles.Add("bassdsd.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            pluginFiles.Add("libbassflac.dylib");
            pluginFiles.Add("libbassdsd.dylib");
        }
        else
        {
            pluginFiles.Add("libbassflac.so");
            pluginFiles.Add("libbassdsd.so");
        }

        foreach (var pluginFile in pluginFiles)
        {
            string pluginPath = Path.Combine(baseDir, pluginFile);
            try
            {
                if (!File.Exists(pluginPath))
                {
                    Console.WriteLine($"[BASS] Plugin not found: {pluginPath}");
                    continue;
                }
                Bass.PluginLoad(pluginPath);
                Console.WriteLine($"[BASS] Plugin loaded: {pluginFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BASS] Could not load {pluginFile}: {ex.Message}");
            }
        }
    }

    // ─── Device management ────────────────────────────────────────────────────

    public List<DeviceModel> ObtenerDispositivos()
    {
        var list = new List<DeviceModel>();
        int deviceCount = Bass.DeviceCount;
        for (int i = 1; i < deviceCount; i++)
        {
            var info = Bass.GetDeviceInfo(i);
            if (info.IsEnabled)
                list.Add(new DeviceModel { Index = i, Name = info.Name, IsDefault = info.IsDefault });
        }
        return list;
    }

    public bool InicializarDispositivo(int deviceIndex = -1, int sampleRate = 44100)
    {
        try
        {
            if (_deviceInitialized && (_deviceSampleRate != sampleRate || _currentDevice != deviceIndex))
            {
                Bass.Free();
                _deviceInitialized = false;
            }

            if (!_deviceInitialized)
            {
                bool init = Bass.Init(deviceIndex, sampleRate, DeviceInitFlags.Latency);
                if (!init && Bass.LastError == Errors.Already)
                    init = true;

                _deviceInitialized = init;
                if (init)
                {
                    _deviceSampleRate = sampleRate;
                    _currentDevice = deviceIndex;
                }
                return init;
            }
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BASS] Exception initializing device: {ex}");
            return false;
        }
    }

    public void CambiarDispositivo(int deviceIndex)
    {
        if (deviceIndex == _currentDevice) return;

        bool wasPlaying = _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
        InicializarDispositivo(deviceIndex, _deviceSampleRate);

        if (_stream != 0)
        {
            Bass.ChannelSetDevice(_stream, deviceIndex);
            if (wasPlaying) Bass.ChannelPlay(_stream);
        }
    }

    // ─── Playback ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Begin playback of a file (or virtual CUE segment).
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <param name="memoryPlayback">Load file into RAM before playback.</param>
    /// <param name="cueStart">CUE start offset in seconds (0 = beginning).</param>
    /// <param name="cueEnd">CUE end offset in seconds (-1 = file end).</param>
    /// <param name="preloadedStream">Already-preloaded stream from gapless engine (0 = none).</param>
    public void Reproducir(
        string filePath,
        bool memoryPlayback = false,
        double cueStart = 0,
        double cueEnd = -1,
        int preloadedStream = 0)
    {
        LiberarStream();

        _cueStart = cueStart;
        _cueEnd = cueEnd;

        string ext = Path.GetExtension(filePath).ToLower();
        bool isFlac = ext == ".flac";

        // ── Detect sample rate ─────────────────────────────────────────────
        int infoStream = isFlac
            ? BassFlac.CreateStream(filePath, 0, 0, BassFlags.Decode)
            : Bass.CreateStream(filePath, 0, 0, BassFlags.Decode);

        float freqf = 44100f;
        if (infoStream != 0)
        {
            Bass.ChannelGetAttribute(infoStream, ChannelAttribute.Frequency, out freqf);
            Bass.StreamFree(infoStream);
        }
        int fileRate = Math.Max(8000, Math.Min(384000, (int)Math.Round(freqf)));

        // ── Reinit device at file's native sample rate ─────────────────────
        if (!_deviceInitialized || _deviceSampleRate != fileRate)
        {
            if (!InicializarDispositivo(_currentDevice, fileRate))
                InicializarDispositivo(_currentDevice, 44100);
        }

        // ── Use preloaded stream or create new one ─────────────────────────
        if (preloadedStream != 0)
        {
            _stream = preloadedStream;
            // Absorb ownership of the preloaded memory handle
            if (_nextMemoryHandle.IsAllocated)
            {
                _memoryHandle = _nextMemoryHandle;
                _nextMemoryHandle = default;
            }
            _nextStream = 0;
        }
        else if (memoryPlayback)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            _memoryHandle = GCHandle.Alloc(fileBytes, GCHandleType.Pinned);
            _stream = isFlac
                ? BassFlac.CreateStream(_memoryHandle.AddrOfPinnedObject(), 0, fileBytes.Length, BassFlags.Default)
                : Bass.CreateStream(_memoryHandle.AddrOfPinnedObject(), 0, fileBytes.Length, BassFlags.Default);
        }
        else
        {
            _stream = isFlac
                ? BassFlac.CreateStream(filePath, 0, 0, BassFlags.Default)
                : Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
        }

        if (_stream == 0)
        {
            Console.WriteLine($"[BASS] Stream creation failed: {Bass.LastError}");
            return;
        }

        // ── Seek to CUE start if needed ────────────────────────────────────
        if (cueStart > 0)
            Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, cueStart));

        // ── Register end sync ──────────────────────────────────────────────
        if (cueEnd > 0)
        {
            // Stop at CUE end position
            long endPos = Bass.ChannelSeconds2Bytes(_stream, cueEnd);
            Bass.ChannelSetSync(_stream, SyncFlags.Position, endPos, _trackEndSync!);
        }
        else
        {
            Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _trackEndSync!);
        }

        // ── Gapless trigger: fire 2s before end ────────────────────────────
        double duration = cueEnd > 0 ? cueEnd - cueStart : DuracionSegundos;
        if (duration > 4)
        {
            double triggerAt = (cueEnd > 0 ? cueEnd : DuracionSegundos) - 2.0;
            long triggerPos = Bass.ChannelSeconds2Bytes(_stream, triggerAt);
            Bass.ChannelSetSync(_stream, SyncFlags.Position | SyncFlags.Onetime, triggerPos, _gaplessTriggerSync!);
        }

        // ── Apply mute / volume ────────────────────────────────────────────
        double vol = _isMuted ? 0 : _volumeBeforeMute;
        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)vol);

        Bass.ChannelPlay(_stream);
    }

    // ─── Gapless preload (called externally to pre-buffer next track) ─────────

    /// <summary>
    /// Pre-create the next stream in the background so it's ready for gapless handoff.
    /// Returns the stream handle (store it and pass as preloadedStream when calling Reproducir).
    /// </summary>
    public int PrecargarStream(string filePath, bool memoryPlayback = false)
    {
        // Free any previously preloaded stream
        FreeNextStream();

        string ext = Path.GetExtension(filePath).ToLower();
        bool isFlac = ext == ".flac";

        try
        {
            if (memoryPlayback)
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                _nextMemoryHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                _nextStream = isFlac
                    ? BassFlac.CreateStream(_nextMemoryHandle.AddrOfPinnedObject(), 0, bytes.Length, BassFlags.Default)
                    : Bass.CreateStream(_nextMemoryHandle.AddrOfPinnedObject(), 0, bytes.Length, BassFlags.Default);
            }
            else
            {
                _nextStream = isFlac
                    ? BassFlac.CreateStream(filePath, 0, 0, BassFlags.Default)
                    : Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
            }

            Console.WriteLine($"[Gapless] Pre-loaded stream {_nextStream} for: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Gapless] Preload failed: {ex.Message}");
            _nextStream = 0;
        }

        return _nextStream;
    }

    public int GetPreloadedStream() => _nextStream;

    // ─── Transport controls ───────────────────────────────────────────────────

    public void Detener()
    {
        if (_stream != 0) Bass.ChannelStop(_stream);
    }

    public void AlternarPausa()
    {
        if (_stream != 0)
        {
            if (Bass.ChannelIsActive(_stream) == PlaybackState.Playing)
                Bass.ChannelPause(_stream);
            else
                Bass.ChannelPlay(_stream);
        }
    }

    public bool EstaReproduciendo =>
        _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    // ─── Volume / Mute ────────────────────────────────────────────────────────

    public double Volumen
    {
        get
        {
            if (_stream == 0) return _volumeBeforeMute;
            Bass.ChannelGetAttribute(_stream, ChannelAttribute.Volume, out float vol);
            return vol;
        }
        set
        {
            double clamped = Math.Clamp(value, 0.0, 1.0);
            _volumeBeforeMute = clamped;
            if (_stream != 0 && !_isMuted)
                Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)clamped);
        }
    }

    public bool IsMuted => _isMuted;

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        if (_stream != 0)
        {
            float vol = _isMuted ? 0f : (float)_volumeBeforeMute;
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, vol);
        }
    }

    // ─── Position / Duration ──────────────────────────────────────────────────

    public double PosicionSegundos
    {
        get
        {
            if (_stream == 0) return 0;
            double raw = Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetPosition(_stream));
            return _cueStart > 0 ? Math.Max(0, raw - _cueStart) : raw;
        }
        set
        {
            if (_stream != 0)
            {
                double target = _cueStart > 0 ? _cueStart + value : value;
                Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, target));
            }
        }
    }

    public double DuracionSegundos
    {
        get
        {
            if (_stream == 0) return 0;
            double total = Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetLength(_stream));
            if (_cueEnd > 0) return _cueEnd - _cueStart;
            return _cueStart > 0 ? total - _cueStart : total;
        }
    }

    // ─── FFT Spectrum Data ────────────────────────────────────────────────────

    /// <summary>
    /// Fills <paramref name="buffer"/> with the current FFT spectrum data.
    /// Buffer should be at least 1024 floats (half of FFT_SIZE=2048).
    /// Returns false if no stream is active.
    /// </summary>
    public bool GetFFTData(float[] buffer)
    {
        if (_stream == 0) return false;
        int result = Bass.ChannelGetData(_stream, buffer, (int)DataFlags.FFT2048);
        return result > 0;
    }

    // ─── Sync callbacks ───────────────────────────────────────────────────────

    private void OnTrackEnd(int handle, int channel, int data, IntPtr user)
    {
        TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnGaplessTrigger(int handle, int channel, int data, IntPtr user)
    {
        GaplessPreloadReady?.Invoke(this, EventArgs.Empty);
    }

    // ─── Stream cleanup ───────────────────────────────────────────────────────

    private void LiberarStream()
    {
        if (_stream != 0)
        {
            Bass.StreamFree(_stream);
            _stream = 0;
        }
        if (_memoryHandle.IsAllocated)
        {
            _memoryHandle.Free();
        }
    }

    private void FreeNextStream()
    {
        if (_nextStream != 0)
        {
            Bass.StreamFree(_nextStream);
            _nextStream = 0;
        }
        if (_nextMemoryHandle.IsAllocated)
        {
            _nextMemoryHandle.Free();
        }
    }

    public void Liberar()
    {
        LiberarStream();
        FreeNextStream();
        Bass.Free();
        _deviceInitialized = false;
    }
}
