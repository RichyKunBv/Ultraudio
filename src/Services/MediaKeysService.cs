using Ultraudio.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Collections.Generic;
using Ultraudio.Models;

#if WINDOWS
using Windows.Media;
using Windows.Media.Playback;
#endif

#if LINUX
using Tmds.DBus;
#endif

namespace Ultraudio.Services;

/// <summary>
/// Cross-platform OS media key / Now Playing integration.
/// Windows: System Media Transport Controls (SMTC) via WinRT
/// macOS:   MPNowPlayingInfoCenter + MPRemoteCommandCenter via ObjC P/Invoke
/// Linux:   MPRIS2 via D-Bus (Tmds.DBus)
/// </summary>
public class MediaKeysService : IDisposable
{
    private readonly Action<TrackModel?, bool>? _updateNowPlaying;
    private readonly Action? _dispose;

    public Action? OnPlay    { get; set; }
    public Action? OnPause   { get; set; }
    public Action? OnNext    { get; set; }
    public Action? OnPrev    { get; set; }
    public Action? OnStop    { get; set; }

    public MediaKeysService()
    {
#if MACOS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var mac = new MacMediaKeys(this);
            _updateNowPlaying = mac.Update;
            _dispose = mac.Dispose;
        }
#endif
#if LINUX
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linux = new LinuxMpris(this);
            _updateNowPlaying = linux.Update;
            _dispose = linux.Dispose;
        }
#endif
#if WINDOWS
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var win = new WindowsSmtc(this);
            _updateNowPlaying = win.Update;
            _dispose = win.Dispose;
        }
#endif
    }

    public void UpdateNowPlaying(TrackModel? track, bool isPlaying)
    {
        try { _updateNowPlaying?.Invoke(track, isPlaying); }
        catch (Exception ex) { Log.Warn("MediaKeys", $"UpdateNowPlaying error: {ex.Message}"); }
    }

    public void Dispose()
    {
        try { _dispose?.Invoke(); }
        catch { /* ignore */ }
    }

#if MACOS
    // ═════════════════════════════════════════════════════════════════════════
    // macOS implementation
    // ═════════════════════════════════════════════════════════════════════════
    private sealed class MacMediaKeys
    {
        private readonly MediaKeysService _parent;
        private bool _currentlyPlaying;

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_getClass(string name);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_IntPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_addTarget(IntPtr receiver, IntPtr selector, IntPtr target, IntPtr action);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_NSInteger(IntPtr receiver, IntPtr selector, nint arg);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_dict(IntPtr receiver, IntPtr selector, IntPtr val, IntPtr key);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string name);


        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, int encoding);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        // CGEventTap Implementation Constants
        private const string CoreGraphicsLibrary = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        public const uint kCGSessionEventTap = 1; // 1 = Session, 0 = HID
        public const uint kCGHeadInsertEventTap = 0;
        public const ulong NSSystemDefinedEventMask = 1UL << 14; 
        
        public delegate IntPtr CGEventTapCallBack(IntPtr proxy, uint type, IntPtr @event, IntPtr refcon);

        [DllImport(CoreGraphicsLibrary)]
        public static extern IntPtr CGEventTapCreate(uint tap, uint place, uint options, ulong eventsOfInterest, CGEventTapCallBack callback, IntPtr refcon);

        [DllImport(CoreGraphicsLibrary)]
        public static extern IntPtr CFMachPortCreateRunLoopSource(IntPtr allocator, IntPtr tap, IntPtr order);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        public static extern IntPtr CFRunLoopGetCurrent();

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        public static extern void CFRunLoopAddSource(IntPtr rl, IntPtr source, IntPtr mode);

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        public static extern IntPtr CFRunLoopCopyCurrentMode(IntPtr rl);

        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_double(IntPtr receiver, IntPtr selector, double arg);

        private CGEventTapCallBack _tapCallback = null!;
        private IntPtr _eventTap;

        public MacMediaKeys(MediaKeysService parent)
        {
            _parent = parent;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => TryRegisterRemoteCommands(), Avalonia.Threading.DispatcherPriority.Background);
        }

        private void TryRegisterRemoteCommands()
        {
            try
            {
                // CGEventTap para interceptar teclas multimedia a nivel sistema
                _tapCallback = EventTapCallback;
                
                // kCGSessionEventTap (1) es más seguro en macOS moderno que kCGHIDEventTap (0)
                _eventTap = CGEventTapCreate(
                    kCGSessionEventTap,
                    kCGHeadInsertEventTap,
                    0, // kCGEventTapOptionDefault (0) para poder consumir el evento
                    NSSystemDefinedEventMask,
                    _tapCallback,
                    IntPtr.Zero
                );

                if (_eventTap == IntPtr.Zero)
                {
                    Log.Warn("MediaKeys", "CGEventTapCreate devolvió IntPtr.Zero. Se requieren permisos de Accesibilidad.");
                    return;
                }

                IntPtr runLoopSource = CFMachPortCreateRunLoopSource(IntPtr.Zero, _eventTap, IntPtr.Zero);
                IntPtr currentRunLoop = CFRunLoopGetCurrent();
                
                // Obtenemos el modo actual (usualmente kCFRunLoopDefaultMode)
                IntPtr currentMode = CFRunLoopCopyCurrentMode(currentRunLoop);
                if (currentMode == IntPtr.Zero)
                {
                    // Fallback explícito a kCFRunLoopDefaultMode si no hay un modo actual activo
                    IntPtr nsRunLoopClass = objc_getClass("NSRunLoop");
                    IntPtr nsRunLoop = objc_msgSend(nsRunLoopClass, sel_registerName("mainRunLoop"));
                    currentMode = objc_msgSend(nsRunLoop, sel_registerName("currentMode"));
                }

                CFRunLoopAddSource(currentRunLoop, runLoopSource, currentMode);
                Log.Info("MediaKeys", "CGEventTap registrado correctamente.");
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"Error al registrar CGEventTap: {ex.Message}");
            }
        }

        private IntPtr EventTapCallback(IntPtr proxy, uint type, IntPtr @event, IntPtr refcon)
        {
            if (type == 14) // NSSystemDefined
            {
                try
                {
                    IntPtr nsEventClass = objc_getClass("NSEvent");
                    if (nsEventClass != IntPtr.Zero)
                    {
                        IntPtr nsEvent = objc_msgSend_IntPtr(nsEventClass, sel_registerName("eventWithCGEvent:"), @event);
                        if (nsEvent != IntPtr.Zero)
                        {
                            long data1 = (long)objc_msgSend(nsEvent, sel_registerName("data1"));
                            int keyCode = (int)((data1 >> 16) & 0xFFFF);
                            int keyFlags = (int)(data1 & 0xFFFF);
                            bool isKeyDown = ((keyFlags & 0xFF00) >> 8) == 0x0A;

                            if (isKeyDown)
                            {
                                switch (keyCode)
                                {
                                    case 16: // NX_KEYTYPE_PLAY
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() => 
                                        {
                                            if (_currentlyPlaying) _parent.OnPause?.Invoke();
                                            else _parent.OnPlay?.Invoke();
                                        });
                                        return IntPtr.Zero; // Consume el evento
                                    case 17: // NX_KEYTYPE_NEXT
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _parent.OnNext?.Invoke());
                                        return IntPtr.Zero;
                                    case 18: // NX_KEYTYPE_PREVIOUS
                                        Avalonia.Threading.Dispatcher.UIThread.Post(() => _parent.OnPrev?.Invoke());
                                        return IntPtr.Zero;
                                }
                            }
                            else
                            {
                                // Si es un evento de KeyUp para las mismas teclas, también lo consumimos para que no llegue a macOS
                                if (keyCode == 16 || keyCode == 17 || keyCode == 18)
                                    return IntPtr.Zero;
                            }
                        }
                    }
                }
                catch { /* Ignorar errores de P/Invoke en el tap */ }
            }
            
            return @event; // Dejar pasar otros eventos
        }

        [DllImport("/usr/lib/libSystem.dylib")]
        private static extern IntPtr dlsym(IntPtr handle, string symbol);

        [DllImport("/usr/lib/libSystem.dylib")]
        private static extern IntPtr dlopen(string path, int mode);

        private static IntPtr GetMediaPlayerSymbol(string symbol)
        {
            // First try RTLD_DEFAULT
            IntPtr RTLD_DEFAULT = new IntPtr(-2);
            IntPtr sym = dlsym(RTLD_DEFAULT, symbol);
            if (sym != IntPtr.Zero) return sym;

            // Fallback to explicitly loading the framework
            IntPtr lib = dlopen("/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer", 1);
            if (lib != IntPtr.Zero) return dlsym(lib, symbol);

            return IntPtr.Zero;
        }

        private static IntPtr MPMediaItemPropertyTitle = GetMediaPlayerSymbol("MPMediaItemPropertyTitle");
        private static IntPtr MPMediaItemPropertyArtist = GetMediaPlayerSymbol("MPMediaItemPropertyArtist");
        private static IntPtr MPMediaItemPropertyAlbumTitle = GetMediaPlayerSymbol("MPMediaItemPropertyAlbumTitle");
        private static IntPtr MPNowPlayingInfoPropertyPlaybackRate = GetMediaPlayerSymbol("MPNowPlayingInfoPropertyPlaybackRate");
        private static IntPtr MPMediaItemPropertyPlaybackDuration = GetMediaPlayerSymbol("MPMediaItemPropertyPlaybackDuration");

        public void Update(TrackModel? track, bool isPlaying)
        {
            _currentlyPlaying = isPlaying;
            try
            {
                IntPtr infoCenterClass = objc_getClass("MPNowPlayingInfoCenter");
                IntPtr dictClass = objc_getClass("NSMutableDictionary");
                if (infoCenterClass == IntPtr.Zero || dictClass == IntPtr.Zero) return;

                IntPtr defaultCenter = objc_msgSend(infoCenterClass, sel_registerName("defaultCenter"));
                if (defaultCenter == IntPtr.Zero) return;

                if (track != null)
                {
                    IntPtr dict = objc_msgSend(objc_msgSend(dictClass, sel_registerName("alloc")), sel_registerName("init"));

                    if (MPMediaItemPropertyTitle != IntPtr.Zero)
                    {
                        IntPtr titleVal = CFStringCreateWithCString(IntPtr.Zero, track.DisplayTitle ?? "Unknown", 0x08000100);
                        IntPtr keyPtr = Marshal.ReadIntPtr(MPMediaItemPropertyTitle);
                        objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), titleVal, keyPtr);
                        CFRelease(titleVal);
                    }

                    if (!string.IsNullOrEmpty(track.Artist) && MPMediaItemPropertyArtist != IntPtr.Zero)
                    {
                        IntPtr artistVal = CFStringCreateWithCString(IntPtr.Zero, track.Artist, 0x08000100);
                        IntPtr keyPtr = Marshal.ReadIntPtr(MPMediaItemPropertyArtist);
                        objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), artistVal, keyPtr);
                        CFRelease(artistVal);
                    }

                    if (!string.IsNullOrEmpty(track.Album) && MPMediaItemPropertyAlbumTitle != IntPtr.Zero)
                    {
                        IntPtr albumVal = CFStringCreateWithCString(IntPtr.Zero, track.Album, 0x08000100);
                        IntPtr keyPtr = Marshal.ReadIntPtr(MPMediaItemPropertyAlbumTitle);
                        objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), albumVal, keyPtr);
                        CFRelease(albumVal);
                    }

                    if (MPNowPlayingInfoPropertyPlaybackRate != IntPtr.Zero)
                    {
                        IntPtr nsNumberClass = objc_getClass("NSNumber");
                        IntPtr rateNumber = objc_msgSend_double(nsNumberClass, sel_registerName("numberWithDouble:"), isPlaying ? 1.0 : 0.0);
                        IntPtr keyPtr = Marshal.ReadIntPtr(MPNowPlayingInfoPropertyPlaybackRate);
                        objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), rateNumber, keyPtr);
                    }

                    if (MPMediaItemPropertyPlaybackDuration != IntPtr.Zero && track.Duration.TotalSeconds > 0)
                    {
                        IntPtr nsNumberClass = objc_getClass("NSNumber");
                        IntPtr durationNumber = objc_msgSend_double(nsNumberClass, sel_registerName("numberWithDouble:"), track.Duration.TotalSeconds);
                        IntPtr keyPtr = Marshal.ReadIntPtr(MPMediaItemPropertyPlaybackDuration);
                        objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), durationNumber, keyPtr);
                    }

                    objc_msgSend_IntPtr(defaultCenter, sel_registerName("setNowPlayingInfo:"), dict);
                    objc_msgSend(dict, sel_registerName("release"));
                }
                else
                {
                    objc_msgSend_IntPtr(defaultCenter, sel_registerName("setNowPlayingInfo:"), IntPtr.Zero);
                }

                // Apple recommends setting playbackState AFTER setting the NowPlayingInfo dictionary
                nint playbackState = track != null ? (isPlaying ? 1 : 2) : 0;
                objc_msgSend_NSInteger(defaultCenter, sel_registerName("setPlaybackState:"), playbackState);
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"macOS update error: {ex.Message}");
            }
        }

        public void Dispose() { }
    }
#endif

#if LINUX
    // ═════════════════════════════════════════════════════════════════════════
    // Linux MPRIS2 implementation
    // ═════════════════════════════════════════════════════════════════════════

    [DBusInterface("org.mpris.MediaPlayer2")]
    public interface IMediaPlayer2 : IDBusObject
    {
        Task RaiseAsync();
        Task QuitAsync();

        Task<object> GetAsync(string prop);
        Task<IDictionary<string, object>> GetAllAsync();
        Task SetAsync(string prop, object val);
        Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    }

    [DBusInterface("org.mpris.MediaPlayer2.Player")]
    public interface IMediaPlayer2Player : IDBusObject
    {
        Task NextAsync();
        Task PreviousAsync();
        Task PauseAsync();
        Task PlayPauseAsync();
        Task StopAsync();
        Task PlayAsync();
        Task SeekAsync(long offset);
        Task SetPositionAsync(ObjectPath trackId, long position);
        Task OpenUriAsync(string uri);

        Task<object> GetAsync(string prop);
        Task<IDictionary<string, object>> GetAllAsync();
        Task SetAsync(string prop, object val);
        Task<IDisposable> WatchPropertiesAsync(Action<PropertyChanges> handler);
    }

    private sealed class LinuxMpris : IMediaPlayer2, IMediaPlayer2Player
    {
        public ObjectPath ObjectPath => new ObjectPath("/org/mpris/MediaPlayer2");
        private readonly MediaKeysService _parent;
        private IConnection? _connection;
        private TrackModel? _currentTrack;
        private bool _isPlaying;

        public event Action<PropertyChanges>? OnPlayerPropertiesChanged;
        public event Action<PropertyChanges>? OnRootPropertiesChanged;

        public LinuxMpris(MediaKeysService parent)
        {
            _parent = parent;
            _ = TryInitAsync();
        }

        private async Task TryInitAsync()
        {
            try
            {
                _connection = new Connection(Address.Session);
                await _connection.ConnectAsync();
                await _connection.RegisterObjectAsync(this);

                // Register standard MPRIS name first (for playerctl, desktop environments, media key daemons)
                await _connection.RegisterServiceAsync("org.mpris.MediaPlayer2.Ultraudio");

                // Also register Flatpak-compliant app-id name
                try
                {
                    await _connection.RegisterServiceAsync("org.mpris.MediaPlayer2.io.github.RichyKunBv.Ultraudio");
                }
                catch { /* Ignore secondary registration failure if not in Flatpak */ }

                Log.Info("MediaKeys", "MPRIS2 service registered.");
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"MPRIS2 init error: {ex.Message}");
            }
        }

        public void Update(TrackModel? track, bool isPlaying)
        {
            _currentTrack = track;
            _isPlaying = isPlaying;

            try
            {
                var changedProps = new[]
                {
                    new KeyValuePair<string, object>("PlaybackStatus", GetPlaybackStatus()),
                    new KeyValuePair<string, object>("Metadata", GetMetadata()),
                    new KeyValuePair<string, object>("CanGoNext", true),
                    new KeyValuePair<string, object>("CanGoPrevious", true),
                    new KeyValuePair<string, object>("CanPlay", true),
                    new KeyValuePair<string, object>("CanPause", true),
                    new KeyValuePair<string, object>("CanControl", true)
                };

                OnPlayerPropertiesChanged?.Invoke(new PropertyChanges(changedProps));
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"Error emitting MPRIS PropertiesChanged: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try { _connection?.Dispose(); }
            catch { /* ignore */ }
        }

        private string GetPlaybackStatus() => _currentTrack != null ? (_isPlaying ? "Playing" : "Paused") : "Stopped";

        private IDictionary<string, object> GetMetadata()
        {
            var dict = new Dictionary<string, object>();
            if (_currentTrack != null)
            {
                dict["mpris:trackid"] = new ObjectPath($"/org/mpris/MediaPlayer2/TrackList/{Guid.NewGuid():N}");
                dict["xesam:title"] = _currentTrack.DisplayTitle;
                if (!string.IsNullOrEmpty(_currentTrack.Artist)) dict["xesam:artist"] = new[] { _currentTrack.Artist };
                if (!string.IsNullOrEmpty(_currentTrack.Album)) dict["xesam:album"] = _currentTrack.Album;
                if (_currentTrack.Duration.TotalSeconds > 0)
                {
                    dict["mpris:length"] = (long)(_currentTrack.Duration.TotalMilliseconds * 1000); // Microseconds
                }
                if (!string.IsNullOrEmpty(_currentTrack.FilePath))
                {
                    dict["xesam:url"] = _currentTrack.FilePath.StartsWith("/") ? $"file://{_currentTrack.FilePath}" : _currentTrack.FilePath;
                }
            }
            else
            {
                dict["mpris:trackid"] = new ObjectPath("/org/mpris/MediaPlayer2/TrackList/NoTrack");
            }
            return dict;
        }

        private IDictionary<string, object> GetAllPlayerProperties()
        {
            return new Dictionary<string, object>
            {
                { "PlaybackStatus", GetPlaybackStatus() },
                { "LoopStatus", "None" },
                { "Rate", 1.0 },
                { "Shuffle", false },
                { "Metadata", GetMetadata() },
                { "Volume", 1.0 },
                { "Position", 0L },
                { "MinimumRate", 1.0 },
                { "MaximumRate", 1.0 },
                { "CanGoNext", true },
                { "CanGoPrevious", true },
                { "CanPlay", true },
                { "CanPause", true },
                { "CanSeek", false },
                { "CanControl", true }
            };
        }

        private IDictionary<string, object> GetAllRootProperties()
        {
            return new Dictionary<string, object>
            {
                { "CanQuit", false },
                { "CanRaise", false },
                { "CanSetFullscreen", false },
                { "HasTrackList", false },
                { "Identity", "Ultraudio" },
                { "DesktopEntry", "io.github.RichyKunBv.Ultraudio" },
                { "SupportedUriSchemes", new[] { "file" } },
                { "SupportedMimeTypes", new[] { "audio/flac", "audio/wav", "audio/mpeg", "audio/ogg", "audio/aac", "audio/mp4" } }
            };
        }

        // IMediaPlayer2
        Task IMediaPlayer2.QuitAsync() => Task.CompletedTask;
        Task IMediaPlayer2.RaiseAsync() => Task.CompletedTask;
        Task<object> IMediaPlayer2.GetAsync(string prop)
        {
            var all = GetAllRootProperties();
            return Task.FromResult(all.TryGetValue(prop, out var val) ? val : null!);
        }
        Task<IDictionary<string, object>> IMediaPlayer2.GetAllAsync() => Task.FromResult(GetAllRootProperties());
        Task IMediaPlayer2.SetAsync(string prop, object val) => Task.CompletedTask;
        Task<IDisposable> IMediaPlayer2.WatchPropertiesAsync(Action<PropertyChanges> handler)
            => SignalWatcher.AddAsync(this, nameof(OnRootPropertiesChanged), handler);

        // IMediaPlayer2Player
        Task IMediaPlayer2Player.NextAsync() { _parent.OnNext?.Invoke(); return Task.CompletedTask; }
        Task IMediaPlayer2Player.PreviousAsync() { _parent.OnPrev?.Invoke(); return Task.CompletedTask; }
        Task IMediaPlayer2Player.PauseAsync() { _parent.OnPause?.Invoke(); return Task.CompletedTask; }
        Task IMediaPlayer2Player.PlayPauseAsync()
        {
            if (_isPlaying) _parent.OnPause?.Invoke();
            else _parent.OnPlay?.Invoke();
            return Task.CompletedTask;
        }
        Task IMediaPlayer2Player.StopAsync() { _parent.OnStop?.Invoke(); return Task.CompletedTask; }
        Task IMediaPlayer2Player.PlayAsync() { _parent.OnPlay?.Invoke(); return Task.CompletedTask; }
        Task IMediaPlayer2Player.SeekAsync(long offset) => Task.CompletedTask;
        Task IMediaPlayer2Player.SetPositionAsync(ObjectPath trackId, long position) => Task.CompletedTask;
        Task IMediaPlayer2Player.OpenUriAsync(string uri) => Task.CompletedTask;

        Task<object> IMediaPlayer2Player.GetAsync(string prop)
        {
            var all = GetAllPlayerProperties();
            return Task.FromResult(all.TryGetValue(prop, out var val) ? val : null!);
        }
        Task<IDictionary<string, object>> IMediaPlayer2Player.GetAllAsync() => Task.FromResult(GetAllPlayerProperties());
        Task IMediaPlayer2Player.SetAsync(string prop, object val) => Task.CompletedTask;
        Task<IDisposable> IMediaPlayer2Player.WatchPropertiesAsync(Action<PropertyChanges> handler)
            => SignalWatcher.AddAsync(this, nameof(OnPlayerPropertiesChanged), handler);
    }
#endif

#if WINDOWS
    // ═════════════════════════════════════════════════════════════════════════
    // Windows SMTC implementation
    // ═════════════════════════════════════════════════════════════════════════
    private sealed class WindowsSmtc
    {
        private readonly MediaKeysService _parent;
        private MediaPlayer? _player;
        private SystemMediaTransportControls? _smtc;

        public WindowsSmtc(MediaKeysService parent)
        {
            _parent = parent;
            TryInit();
        }

        private void TryInit()
        {
            try
            {
                // Instantiate a WinRT MediaPlayer just to acquire its SMTC globally
                _player = new MediaPlayer();
                _player.CommandManager.IsEnabled = true;
                _smtc = _player.SystemMediaTransportControls;
                _smtc.IsPlayEnabled = true;
                _smtc.IsPauseEnabled = true;
                _smtc.IsNextEnabled = true;
                _smtc.IsPreviousEnabled = true;
                _smtc.ButtonPressed += Smtc_ButtonPressed;
                
                Log.Info("MediaKeys", "Windows SMTC initialized.");
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"SMTC init error: {ex.Message}");
            }
        }

        private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Play: _parent.OnPlay?.Invoke(); break;
                case SystemMediaTransportControlsButton.Pause: _parent.OnPause?.Invoke(); break;
                case SystemMediaTransportControlsButton.Next: _parent.OnNext?.Invoke(); break;
                case SystemMediaTransportControlsButton.Previous: _parent.OnPrev?.Invoke(); break;
            }
        }

        public void Update(TrackModel? track, bool isPlaying)
        {
            if (_smtc != null)
            {
                if (track != null)
                {
                    var updater = _smtc.DisplayUpdater;
                    updater.Type = MediaPlaybackType.Music;
                    updater.MusicProperties.Title = track.DisplayTitle;
                    updater.MusicProperties.Artist = track.Artist;
                    updater.Update();
                    _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
                }
                else
                {
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                }
            }
        }

        public void Dispose()
        {
            if (_smtc != null)
            {
                _smtc.ButtonPressed -= Smtc_ButtonPressed;
            }
            _player?.Dispose();
        }
    }
#endif
}
