using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Flac;
using ManagedBass.Cd;
using Ultraudio.Core;

namespace Ultraudio;

// ─────────────────────────────────────────────────────────────────────────────
// Supporting types
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Represents an audio output device available for playback.
/// </summary>
public class DeviceModel
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public override string ToString() => Name;
}

/// <summary>
/// Repeat playback mode.
/// </summary>
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
    // ── Active stream ────────────────────────────────────────────────────
    private int _stream;
    private GCHandle _memoryHandle;

    // ── Gapless: pre-loaded next stream ──────────────────────────────────
    private int _nextStream;
    private GCHandle _nextMemoryHandle;
    private SyncProcedure? _gaplessTriggerSync;
    private SyncProcedure? _trackEndSync;

    // ── Device state ─────────────────────────────────────────────────────
    private bool _deviceInitialized = false;
    private int _deviceSampleRate = UltraudioConstants.DefaultSampleRate;
    private int _currentDevice = -1;

    // ── Volume / Mute ────────────────────────────────────────────────────
    private double _volumeBeforeMute = 1.0;
    private bool _isMuted = false;

    // ── CUE virtual track bounds ─────────────────────────────────────────
    private double _cueStart = 0;
    private double _cueEnd = -1; // -1 = play to file end

    // ── Events ───────────────────────────────────────────────────────────
    public event EventHandler? TrackEnded;
    public event EventHandler? GaplessPreloadReady;

    // ── FFT buffer ───────────────────────────────────────────────────────
    private const int FftSize = 2048;
    private readonly float[] _fftBuffer = new float[FftSize / 2];

    // ─────────────────────────────────────────────────────────────────────
    public AudioEngine()
    {
        _trackEndSync = new SyncProcedure(OnTrackEnd);
        _gaplessTriggerSync = new SyncProcedure(OnGaplessTrigger);
        LoadBassPlugins();
    }

    // ─── Plugin loading ──────────────────────────────────────────────────

    private void LoadBassPlugins()
    {
        string baseDir = AppContext.BaseDirectory;
        var pluginFiles = new List<string>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            pluginFiles.Add("bassflac.dll");
            pluginFiles.Add("bassdsd.dll");
            pluginFiles.Add("basscd.dll");
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
            pluginFiles.Add("libbasscd.so");
        }

        foreach (var pluginFile in pluginFiles)
        {
            string pluginPath = Path.Combine(baseDir, pluginFile);
            try
            {
                if (!File.Exists(pluginPath))
                {
                    Log.Warn("BASS", $"Plugin not found: {pluginPath}");
                    continue;
                }
                Bass.PluginLoad(pluginPath);
                Log.Info("BASS", $"Plugin loaded: {pluginFile}");
            }
            catch (Exception ex)
            {
                Log.Warn("BASS", $"Could not load {pluginFile}: {ex.Message}");
            }
        }
    }

    // ─── Device management ───────────────────────────────────────────────

    /// <summary>
    /// Enumerates all enabled audio output devices (excluding "No Sound").
    /// </summary>
    public List<DeviceModel> GetDevices()
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

    /// <summary>
    /// Initializes (or reinitializes) BASS for a given device and sample rate.
    /// Re-initialization happens automatically when the file's native sample rate
    /// differs from the current device sample rate (bit-perfect playback).
    /// </summary>
    public bool InitializeDevice(int deviceIndex = -1, int sampleRate = 44100)
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
            Log.Error("BASS", "Exception initializing device", ex);
            return false;
        }
    }

    /// <summary>
    /// Switches the audio output to a different device without stopping playback.
    /// </summary>
    public void ChangeDevice(int deviceIndex)
    {
        if (deviceIndex == _currentDevice) return;

        bool wasPlaying = _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
        InitializeDevice(deviceIndex, _deviceSampleRate);

        if (_stream != 0)
        {
            Bass.ChannelSetDevice(_stream, deviceIndex);
            if (wasPlaying) Bass.ChannelPlay(_stream);
        }
    }

    // ─── Playback ────────────────────────────────────────────────────────

    /// <summary>
    /// Begin playback of a file (or virtual CUE segment).
    /// </summary>
    /// <param name="filePath">Path to the audio file.</param>
    /// <param name="memoryPlayback">Load file into RAM before playback.</param>
    /// <param name="cueStart">CUE start offset in seconds (0 = beginning).</param>
    /// <param name="cueEnd">CUE end offset in seconds (-1 = file end).</param>
    /// <param name="preloadedStream">Already-preloaded stream from gapless engine (0 = none).</param>
    public void Play(
        string filePath,
        bool memoryPlayback = false,
        double cueStart = 0,
        double cueEnd = -1,
        int preloadedStream = 0)
    {
        ReleaseStream();

        _cueStart = cueStart;
        _cueEnd = cueEnd;

        string ext = Path.GetExtension(filePath).ToLower();
        bool isFlac = ext == ".flac";
        bool isCd = filePath.StartsWith(UltraudioConstants.CdProtocolPrefix, StringComparison.OrdinalIgnoreCase);

        // ── Detect sample rate ────────────────────────────────────────────
        int infoStream = 0;
        if (isCd)
        {
            var parts = filePath.Replace(UltraudioConstants.CdProtocolPrefix, "").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out int drive) && int.TryParse(parts[1], out int track))
            {
                infoStream = BassCd.CreateStream(drive, track, BassFlags.Decode);
            }
        }
        else
        {
            infoStream = isFlac
                ? BassFlac.CreateStream(filePath, 0, 0, BassFlags.Decode)
                : Bass.CreateStream(filePath, 0, 0, BassFlags.Decode);
        }

        float freqf = UltraudioConstants.DefaultSampleRate;
        if (infoStream != 0)
        {
            Bass.ChannelGetAttribute(infoStream, ChannelAttribute.Frequency, out freqf);
            Bass.StreamFree(infoStream);
        }
        int fileRate = Math.Max(UltraudioConstants.MinSampleRate,
                      Math.Min(UltraudioConstants.MaxSampleRate, (int)Math.Round(freqf)));

        // ── Reinit device at file's native sample rate ────────────────────
        if (!_deviceInitialized || _deviceSampleRate != fileRate)
        {
            if (!InitializeDevice(_currentDevice, fileRate))
                InitializeDevice(_currentDevice, UltraudioConstants.DefaultSampleRate);
        }

        // ── Use preloaded stream or create new one ────────────────────────
        if (preloadedStream != 0)
        {
            _stream = preloadedStream;
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
        else if (isCd)
        {
            var parts = filePath.Replace(UltraudioConstants.CdProtocolPrefix, "").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out int drive) && int.TryParse(parts[1], out int track))
            {
                _stream = BassCd.CreateStream(drive, track, BassFlags.Default);
            }
        }
        else
        {
            _stream = isFlac
                ? BassFlac.CreateStream(filePath, 0, 0, BassFlags.Default)
                : Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
        }

        if (_stream == 0)
        {
            Log.Error("BASS", $"Stream creation failed: {Bass.LastError}");
            return;
        }

        // ── Seek to CUE start if needed ─────────────────────────────────
        if (cueStart > 0)
            Bass.ChannelSetPosition(_stream, Bass.ChannelSeconds2Bytes(_stream, cueStart));

        // ── Register end sync ───────────────────────────────────────────
        if (cueEnd > 0)
        {
            long endPos = Bass.ChannelSeconds2Bytes(_stream, cueEnd);
            Bass.ChannelSetSync(_stream, SyncFlags.Position, endPos, _trackEndSync!);
        }
        else
        {
            Bass.ChannelSetSync(_stream, SyncFlags.End, 0, _trackEndSync!);
        }

        // ── Gapless trigger: fire 2s before end ─────────────────────────
        double duration = cueEnd > 0 ? cueEnd - cueStart : DurationSeconds;
        if (duration > 4)
        {
            double totalDur = Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetLength(_stream));
            double triggerAt = (cueEnd > 0 ? cueEnd : totalDur) - 2.0;
            long triggerPos = Bass.ChannelSeconds2Bytes(_stream, triggerAt);
            Bass.ChannelSetSync(_stream, SyncFlags.Position | SyncFlags.Onetime, triggerPos, _gaplessTriggerSync!);
        }

        // ── Apply mute / volume ─────────────────────────────────────────
        double vol = _isMuted ? 0 : _volumeBeforeMute;
        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, (float)vol);

        Bass.ChannelPlay(_stream);
    }

    // ─── Gapless preload ─────────────────────────────────────────────────

    /// <summary>
    /// Pre-create the next stream so it's ready for gapless handoff.
    /// Returns the stream handle (store it and pass as preloadedStream when calling Play).
    /// </summary>
    public int PreloadStream(string filePath, bool memoryPlayback = false)
    {
        FreeNextStream();

        string ext = Path.GetExtension(filePath).ToLower();
        bool isFlac = ext == ".flac";
        bool isCd = filePath.StartsWith(UltraudioConstants.CdProtocolPrefix, StringComparison.OrdinalIgnoreCase);

        try
        {
            if (memoryPlayback && !isCd)
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                _nextMemoryHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                _nextStream = isFlac
                    ? BassFlac.CreateStream(_nextMemoryHandle.AddrOfPinnedObject(), 0, bytes.Length, BassFlags.Default)
                    : Bass.CreateStream(_nextMemoryHandle.AddrOfPinnedObject(), 0, bytes.Length, BassFlags.Default);
            }
            else if (isCd)
            {
                var parts = filePath.Replace(UltraudioConstants.CdProtocolPrefix, "").Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out int drive) && int.TryParse(parts[1], out int track))
                {
                    _nextStream = BassCd.CreateStream(drive, track, BassFlags.Default);
                }
            }
            else
            {
                _nextStream = isFlac
                    ? BassFlac.CreateStream(filePath, 0, 0, BassFlags.Default)
                    : Bass.CreateStream(filePath, 0, 0, BassFlags.Default);
            }

            Log.Debug("Gapless", $"Pre-loaded stream {_nextStream} for: {Path.GetFileName(filePath)}");
        }
        catch (Exception ex)
        {
            Log.Error("Gapless", "Preload failed", ex);
            _nextStream = 0;
        }

        return _nextStream;
    }

    public int GetPreloadedStream() => _nextStream;

    // ─── Transport controls ──────────────────────────────────────────────

    /// <summary>Stops playback of the current stream.</summary>
    public void Stop()
    {
        if (_stream != 0) Bass.ChannelStop(_stream);
    }

    /// <summary>Toggles between play and pause states.</summary>
    public void TogglePause()
    {
        if (_stream != 0)
        {
            if (Bass.ChannelIsActive(_stream) == PlaybackState.Playing)
                Bass.ChannelPause(_stream);
            else
                Bass.ChannelPlay(_stream);
        }
    }

    /// <summary>Whether audio is currently playing.</summary>
    public bool IsPlaying =>
        _stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Playing;

    // ─── Volume / Mute ───────────────────────────────────────────────────

    /// <summary>Gets or sets the playback volume (0.0 to 1.0).</summary>
    public double Volume
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

    /// <summary>Whether the audio is currently muted.</summary>
    public bool IsMuted => _isMuted;

    /// <summary>Toggles mute on/off.</summary>
    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        if (_stream != 0)
        {
            float vol = _isMuted ? 0f : (float)_volumeBeforeMute;
            Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, vol);
        }
    }

    // ─── Position / Duration ─────────────────────────────────────────────

    /// <summary>Gets or sets the current playback position in seconds (CUE-aware).</summary>
    public double PositionSeconds
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

    /// <summary>Gets the total duration of the current track in seconds (CUE-aware).</summary>
    public double DurationSeconds
    {
        get
        {
            if (_stream == 0) return 0;
            double total = Bass.ChannelBytes2Seconds(_stream, Bass.ChannelGetLength(_stream));
            if (_cueEnd > 0) return _cueEnd - _cueStart;
            return _cueStart > 0 ? total - _cueStart : total;
        }
    }

    // ─── FFT Spectrum Data ───────────────────────────────────────────────

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

    // ─── Sync callbacks ──────────────────────────────────────────────────

    private void OnTrackEnd(int handle, int channel, int data, IntPtr user)
    {
        TrackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnGaplessTrigger(int handle, int channel, int data, IntPtr user)
    {
        GaplessPreloadReady?.Invoke(this, EventArgs.Empty);
    }

    // ─── Stream cleanup ──────────────────────────────────────────────────

    private void ReleaseStream()
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

    /// <summary>Releases all resources: streams, preloaded streams, and BASS itself.</summary>
    public void Release()
    {
        ReleaseStream();
        FreeNextStream();
        Bass.Free();
        _deviceInitialized = false;
    }

    // ─── Legacy API compatibility (deprecated, will be removed) ──────────
    // These methods delegate to the new English-named API for backwards compat
    // during the transition period.

    [Obsolete("Use GetDevices() instead")]
    public List<DeviceModel> ObtenerDispositivos() => GetDevices();
    [Obsolete("Use InitializeDevice() instead")]
    public bool InicializarDispositivo(int deviceIndex = -1, int sampleRate = 44100) => InitializeDevice(deviceIndex, sampleRate);
    [Obsolete("Use ChangeDevice() instead")]
    public void CambiarDispositivo(int deviceIndex) => ChangeDevice(deviceIndex);
    [Obsolete("Use Play() instead")]
    public void Reproducir(string filePath, bool memoryPlayback = false, double cueStart = 0, double cueEnd = -1, int preloadedStream = 0)
        => Play(filePath, memoryPlayback, cueStart, cueEnd, preloadedStream);
    [Obsolete("Use Stop() instead")]
    public void Detener() => Stop();
    [Obsolete("Use TogglePause() instead")]
    public void AlternarPausa() => TogglePause();
    [Obsolete("Use IsPlaying instead")]
    public bool EstaReproduciendo => IsPlaying;
    [Obsolete("Use Volume instead")]
    public double Volumen { get => Volume; set => Volume = value; }
    [Obsolete("Use PositionSeconds instead")]
    public double PosicionSegundos { get => PositionSeconds; set => PositionSeconds = value; }
    [Obsolete("Use DurationSeconds instead")]
    public double DuracionSegundos => DurationSeconds;
    [Obsolete("Use PreloadStream() instead")]
    public int PrecargarStream(string filePath, bool memoryPlayback = false) => PreloadStream(filePath, memoryPlayback);
    [Obsolete("Use Release() instead")]
    public void Liberar() => Release();
}
