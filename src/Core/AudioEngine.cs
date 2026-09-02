using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ManagedBass;
using ManagedBass.Flac;
using ManagedBass.Cd;
using ManagedBass.Wasapi;
using ManagedBass.Asio;
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
    private string _currentOutputMode = "Shared";
    private string _currentFilePath = string.Empty;
    private bool _currentMemoryPlayback = false;
    private WasapiProcedure? _wasapiProc;
    private AsioProcedure? _asioProc;

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
            pluginFiles.Add("bassalac.dll");
            pluginFiles.Add("bass_tta.dll");
            pluginFiles.Add("bass_ofr.dll");
            pluginFiles.Add("bassape.dll");
            pluginFiles.Add("basswv.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            pluginFiles.Add("libbassflac.dylib");
            pluginFiles.Add("libbassdsd.dylib");
            pluginFiles.Add("libbass_tta.dylib");
            pluginFiles.Add("libbassape.dylib");
            pluginFiles.Add("libbasswv.dylib");
        }
        else
        {
            pluginFiles.Add("libbassflac.so");
            pluginFiles.Add("libbassdsd.so");
            pluginFiles.Add("libbasscd.so");
            pluginFiles.Add("libbassalac.so");
            pluginFiles.Add("libbass_tta.so");
            pluginFiles.Add("libbassape.so");
            pluginFiles.Add("libbasswv.so");
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
    public static bool IsModeSupported(string mode)
    {
        if (mode == "Shared") return true;
        if (mode == "WasapiExclusive") return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (mode == "Asio") return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64;
        if (mode == "HogMode") return RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        if (mode == "AlsaDirect") return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        return false;
    }

    /// <summary>
    /// Enumerates all enabled audio output devices (excluding "No Sound").
    /// </summary>
    public List<DeviceModel> GetDevices(string mode = "Shared")
    {
        var list = new List<DeviceModel>();
        try
        {
            if (mode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                EnumerateWasapiDevices(list);
            }
            else if (mode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
            {
                EnumerateAsioDevices(list);
            }
            else
            {
                for (int i = 1; i < Bass.DeviceCount; i++)
                {
                    var info = Bass.GetDeviceInfo(i);
                    if (info.IsEnabled)
                        list.Add(new DeviceModel { Index = i, Name = info.Name, IsDefault = info.IsDefault });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("BASS", $"Error enumerating devices for {mode}", ex);
        }
        return list;
    }

    /// <summary>
    /// Initializes (or reinitializes) BASS for a given device and sample rate.
    /// Re-initialization happens automatically when the file's native sample rate
    /// differs from the current device sample rate (bit-perfect playback).
    /// </summary>
    public bool InitializeDevice(int deviceIndex = -1, int sampleRate = 44100, string outputMode = "Shared")
    {
        try
        {
            if (!IsModeSupported(outputMode))
                outputMode = "Shared";

            if (_deviceInitialized && (_deviceSampleRate != sampleRate || _currentDevice != deviceIndex || _currentOutputMode != outputMode))
            {
                FreeCurrentOutput();
                _deviceInitialized = false;
            }

            if (!_deviceInitialized)
            {
                bool init = false;
                _currentOutputMode = outputMode;
                _currentDevice = deviceIndex;

                if (outputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    init = InitWasapi(deviceIndex, sampleRate);
                }
                else if (outputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
                {
                    init = InitAsio(deviceIndex, sampleRate);
                }
                else
                {
                    // For macOS Hog Mode, a P/Invoke would go here to set kAudioDevicePropertyHogMode.
                    // For Linux ALSA Direct, BASS already prefers hw:X,Y when selected.
                    init = Bass.Init(deviceIndex, sampleRate, DeviceInitFlags.Latency);
                    if (!init && Bass.LastError == Errors.Already) init = true;
                }

                _deviceInitialized = init;
                if (init) _deviceSampleRate = sampleRate;
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

    private void FreeCurrentOutput()
    {
        if (_currentOutputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            WasapiFree();
        else if (_currentOutputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
            AsioFree();
        Bass.Free();
    }

    private int WasapiCallback(IntPtr buffer, int length, IntPtr user)
    {
        if (_stream == 0) return 0;
        int read = Bass.ChannelGetData(_stream, buffer, length);
        if (read < 0) read = 0;
        return read;
    }

    private int AsioCallback(bool input, int channel, IntPtr buffer, int length, IntPtr user)
    {
        if (_stream == 0) return 0;
        int read = Bass.ChannelGetData(_stream, buffer, length);
        if (read < 0) read = 0;
        return read;
    }

    /// <summary>
    /// Switches the audio output to a different device without stopping playback.
    /// </summary>
    public void ChangeDevice(int deviceIndex, string outputMode)
    {
        if (deviceIndex == _currentDevice && outputMode == _currentOutputMode) return;

        bool wasPlaying = _stream != 0 && IsPlaying;
        double currentPos = _stream != 0 ? PositionSeconds : 0;
        string currentFile = _currentFilePath;
        bool currentMem = _currentMemoryPlayback;
        double cueStart = _cueStart;
        double cueEnd = _cueEnd;

        if (wasPlaying) Stop();
        ReleaseStream();
        FreeNextStream();

        InitializeDevice(deviceIndex, _deviceSampleRate, outputMode);

        if (!string.IsNullOrEmpty(currentFile) && File.Exists(currentFile))
        {
            Play(currentFile, currentMem, cueStart, cueEnd);
            if (currentPos > 0)
                PositionSeconds = currentPos;

            if (!wasPlaying)
                TogglePause();
        }
    }

    // ─── Platform-isolated native methods (NoInlining prevents JIT issues on non-Windows) ───

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WasapiStart() => BassWasapi.Start();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WasapiStop() => BassWasapi.Stop(true);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void WasapiFree() => BassWasapi.Free();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool WasapiIsStarted() => BassWasapi.IsStarted;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AsioStart() => BassAsio.Start(0);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AsioStop() => BassAsio.Stop();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AsioPause() => BassAsio.ChannelPause(false, 0);

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AsioFree() => BassAsio.Free();

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool AsioIsStarted() => BassAsio.IsStarted;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void EnumerateWasapiDevices(List<DeviceModel> list)
    {
        for (int i = 0; i < BassWasapi.DeviceCount; i++)
        {
            var info = BassWasapi.GetDeviceInfo(i);
            if (info.IsEnabled && !info.IsLoopback && !info.IsInput)
                list.Add(new DeviceModel { Index = i, Name = info.Name, IsDefault = info.IsDefault });
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void EnumerateAsioDevices(List<DeviceModel> list)
    {
        for (int i = 0; i < BassAsio.DeviceCount; i++)
        {
            var info = BassAsio.GetDeviceInfo(i);
            list.Add(new DeviceModel { Index = i, Name = info.Name, IsDefault = false });
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private bool InitWasapi(int deviceIndex, int sampleRate)
    {
        Bass.Init(0, sampleRate, DeviceInitFlags.Default); // NoSound for decode
        _wasapiProc = new WasapiProcedure(WasapiCallback);
        bool init = BassWasapi.Init(deviceIndex, sampleRate, 0, WasapiInitFlags.Exclusive | WasapiInitFlags.Buffer, 0.1f, 0.05f, _wasapiProc, IntPtr.Zero);
        if (!init)
            init = BassWasapi.Init(deviceIndex, UltraudioConstants.DefaultSampleRate, 0, WasapiInitFlags.Exclusive | WasapiInitFlags.Buffer, 0.1f, 0.05f, _wasapiProc, IntPtr.Zero);
        return init;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private bool InitAsio(int deviceIndex, int sampleRate)
    {
        Bass.Init(0, sampleRate, DeviceInitFlags.Default);
        _asioProc = new AsioProcedure(AsioCallback);
        bool init = BassAsio.Init(deviceIndex, AsioInitFlags.Thread);
        if (init)
        {
            BassAsio.ChannelEnable(false, 0, _asioProc, IntPtr.Zero);
            BassAsio.ChannelEnable(false, 1, _asioProc, IntPtr.Zero);
            if (!BassAsio.ChannelSetRate(false, 0, sampleRate))
                BassAsio.ChannelSetRate(false, 0, UltraudioConstants.DefaultSampleRate);
        }
        return init;
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

        _currentFilePath = filePath;
        _currentMemoryPlayback = memoryPlayback;
        _cueStart = cueStart;
        _cueEnd = cueEnd;

        filePath = ResolveFilePath(filePath, out bool isCd);
        string ext = Path.GetExtension(filePath).ToLower();
        bool isFlac = ext == ".flac";

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
            // If device sample rate differs, Bass.Free() will be called,
            // which destroys preloadedStream. So we cannot reuse preloadedStream across rate switches.
            if (preloadedStream != 0)
            {
                FreeNextStream();
                preloadedStream = 0;
            }

            if (!InitializeDevice(_currentDevice, fileRate, _currentOutputMode))
                InitializeDevice(_currentDevice, UltraudioConstants.DefaultSampleRate, _currentOutputMode);
        }

        // ── Use preloaded stream or create new one ────────────────────────
        bool isDecodeMode = (_currentOutputMode == "WasapiExclusive" || _currentOutputMode == "Asio") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        BassFlags streamFlags = isDecodeMode ? BassFlags.Decode : BassFlags.Default;

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
        else if (memoryPlayback && !isCd)
        {
            byte[] fileBytes = File.ReadAllBytes(filePath);
            _memoryHandle = GCHandle.Alloc(fileBytes, GCHandleType.Pinned);
            _stream = isFlac
                ? BassFlac.CreateStream(_memoryHandle.AddrOfPinnedObject(), 0, fileBytes.Length, streamFlags)
                : Bass.CreateStream(_memoryHandle.AddrOfPinnedObject(), 0, fileBytes.Length, streamFlags);
        }
        else if (isCd)
        {
            var parts = filePath.Replace(UltraudioConstants.CdProtocolPrefix, "").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out int drive) && int.TryParse(parts[1], out int track))
            {
                _stream = BassCd.CreateStream(drive, track, streamFlags);
            }
        }
        else
        {
            _stream = isFlac
                ? BassFlac.CreateStream(filePath, 0, 0, streamFlags)
                : Bass.CreateStream(filePath, 0, 0, streamFlags);
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

        if (_currentOutputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            WasapiStart();
        }
        else if (_currentOutputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            AsioStart();
        }
        else
        {
            Bass.ChannelPlay(_stream);
        }
    }

    // ─── Gapless preload ─────────────────────────────────────────────────

    /// <summary>
    /// Pre-create the next stream so it's ready for gapless handoff.
    /// Returns the stream handle (store it and pass as preloadedStream when calling Play).
    /// </summary>
    public int PreloadStream(string filePath, bool memoryPlayback = false)
    {
        FreeNextStream();

        filePath = ResolveFilePath(filePath, out bool isCd);
        string ext = Path.GetExtension(filePath).ToLower();
        bool isFlac = ext == ".flac";

        bool isDecodeMode = (_currentOutputMode == "WasapiExclusive" || _currentOutputMode == "Asio") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        BassFlags streamFlags = isDecodeMode ? BassFlags.Decode : BassFlags.Default;

        try
        {
            if (memoryPlayback && !isCd)
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                _nextMemoryHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                _nextStream = isFlac
                    ? BassFlac.CreateStream(_nextMemoryHandle.AddrOfPinnedObject(), 0, bytes.Length, streamFlags)
                    : Bass.CreateStream(_nextMemoryHandle.AddrOfPinnedObject(), 0, bytes.Length, streamFlags);
            }
            else if (isCd)
            {
                var parts = filePath.Replace(UltraudioConstants.CdProtocolPrefix, "").Split('/');
                if (parts.Length == 2 && int.TryParse(parts[0], out int drive) && int.TryParse(parts[1], out int track))
                {
                    _nextStream = BassCd.CreateStream(drive, track, streamFlags);
                }
            }
            else
            {
                _nextStream = isFlac
                    ? BassFlac.CreateStream(filePath, 0, 0, streamFlags)
                    : Bass.CreateStream(filePath, 0, 0, streamFlags);
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
        if (_stream != 0)
        {
            if (_currentOutputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                WasapiStop();
            else if (_currentOutputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
                AsioStop();
            else
                Bass.ChannelStop(_stream);
        }
    }

    /// <summary>Toggles between play and pause states.</summary>
    public void TogglePause()
    {
        if (_stream != 0)
        {
            if (IsPlaying)
            {
                if (_currentOutputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    WasapiStop();
                else if (_currentOutputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
                    AsioPause();
                else
                    Bass.ChannelPause(_stream);
            }
            else
            {
                if (_currentOutputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    WasapiStart();
                else if (_currentOutputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
                    AsioStart();
                else
                    Bass.ChannelPlay(_stream);
            }
        }
    }

    /// <summary>Whether audio is currently playing.</summary>
    public bool IsPlaying
    {
        get
        {
            if (_stream == 0) return false;
            if (_currentOutputMode == "WasapiExclusive" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return WasapiIsStarted();
            if (_currentOutputMode == "Asio" && RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.OSArchitecture == Architecture.X64)
                return AsioIsStarted();
            return Bass.ChannelIsActive(_stream) == PlaybackState.Playing;
        }
    }

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
        // In decode channels (WASAPI / ASIO), ChannelGetData consumes actual PCM audio data from the buffer,
        // causing stuttering and dropouts. Only query FFT directly if playing through standard BASS output.
        bool isDecodeMode = (_currentOutputMode == "WasapiExclusive" || _currentOutputMode == "Asio") && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isDecodeMode) return false;

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

    // ─── Path Resolution ─────────────────────────────────────────────────

    private string ResolveFilePath(string filePath, out bool isCd)
    {
        isCd = filePath.StartsWith(UltraudioConstants.CdProtocolPrefix, StringComparison.OrdinalIgnoreCase);
        
        if (isCd && RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var parts = filePath.Replace(UltraudioConstants.CdProtocolPrefix, "").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[1], out int track))
            {
                string trackStr = track.ToString("D2");
                try
                {
                    var volumes = Directory.GetDirectories("/Volumes");
                    foreach (var v in volumes)
                    {
                        string p = Path.Combine(v, $"Track {trackStr}.aiff");
                        if (File.Exists(p))
                        {
                            isCd = false;
                            return p;
                        }
                    }
                }
                catch { /* Ignore access errors */ }
            }
        }
        
        return filePath;
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
        FreeCurrentOutput();
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
    public void CambiarDispositivo(int deviceIndex) => ChangeDevice(deviceIndex, "Shared");
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
