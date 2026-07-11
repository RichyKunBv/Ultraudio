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

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, int encoding);
        
        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern void CFRelease(IntPtr cf);

        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_getClass(string name);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_add(IntPtr receiver, IntPtr selector, IntPtr target, IntPtr action);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern IntPtr objc_msgSend_dict(IntPtr receiver, IntPtr selector, IntPtr val, IntPtr key);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_set(IntPtr receiver, IntPtr selector, IntPtr arg);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "objc_msgSend")]
        private static extern void objc_msgSend_nuint(IntPtr receiver, IntPtr selector, nuint arg);
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string name);
        
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern void objc_registerClassPair(IntPtr cls);
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern bool class_addMethod(IntPtr cls, IntPtr name, CommandHandlerDelegate imp, string types);

        private delegate long CommandHandlerDelegate(IntPtr self, IntPtr cmd, IntPtr eventPtr);

        private readonly CommandHandlerDelegate _playDel;
        private readonly CommandHandlerDelegate _pauseDel;
        private readonly CommandHandlerDelegate _toggleDel;
        private readonly CommandHandlerDelegate _nextDel;
        private readonly CommandHandlerDelegate _prevDel;
        private IntPtr _target;

        public MacMediaKeys(MediaKeysService parent)
        {
            _parent = parent;

            _playDel = (s, c, e) => { _parent.OnPlay?.Invoke(); return 0; };
            _pauseDel = (s, c, e) => { _parent.OnPause?.Invoke(); return 0; };
            _toggleDel = (s, c, e) => { _parent.OnPlay?.Invoke(); return 0; }; 
            _nextDel = (s, c, e) => { _parent.OnNext?.Invoke(); return 0; };
            _prevDel = (s, c, e) => { _parent.OnPrev?.Invoke(); return 0; };

            TryRegisterRemoteCommands();
        }

        private void TryRegisterRemoteCommands()
        {
            try
            {
                IntPtr nsObjectClass = objc_getClass("NSObject");
                if (nsObjectClass == IntPtr.Zero) return;

                string className = "UltraudioMediaKeys_" + Guid.NewGuid().ToString("N");
                IntPtr myClass = objc_allocateClassPair(nsObjectClass, className, IntPtr.Zero);
                if (myClass == IntPtr.Zero) return;

                class_addMethod(myClass, sel_registerName("onPlay:"), _playDel, "q@:@");
                class_addMethod(myClass, sel_registerName("onPause:"), _pauseDel, "q@:@");
                class_addMethod(myClass, sel_registerName("onToggle:"), _toggleDel, "q@:@");
                class_addMethod(myClass, sel_registerName("onNext:"), _nextDel, "q@:@");
                class_addMethod(myClass, sel_registerName("onPrev:"), _prevDel, "q@:@");

                objc_registerClassPair(myClass);
                _target = objc_msgSend(objc_msgSend(myClass, sel_registerName("alloc")), sel_registerName("init"));

                IntPtr commandCenterClass = objc_getClass("MPRemoteCommandCenter");
                if (commandCenterClass != IntPtr.Zero)
                {
                    IntPtr center = objc_msgSend(commandCenterClass, sel_registerName("sharedCommandCenter"));
                    if (center != IntPtr.Zero)
                    {
                        RegisterCommand(center, "playCommand", _target, "onPlay:");
                        RegisterCommand(center, "pauseCommand", _target, "onPause:");
                        RegisterCommand(center, "togglePlayPauseCommand", _target, "onToggle:");
                        RegisterCommand(center, "nextTrackCommand", _target, "onNext:");
                        RegisterCommand(center, "previousTrackCommand", _target, "onPrev:");
                        Log.Info("MediaKeys", "macOS MPRemoteCommandCenter initialized.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"macOS init error: {ex.Message}");
            }
        }

        private void RegisterCommand(IntPtr center, string cmdName, IntPtr target, string selName)
        {
            IntPtr command = objc_msgSend(center, sel_registerName(cmdName));
            if (command != IntPtr.Zero)
            {
                objc_msgSend_add(command, sel_registerName("addTarget:action:"), target, sel_registerName(selName));
                objc_msgSend_set(command, sel_registerName("setEnabled:"), (IntPtr)1);
            }
        }

        public void Update(TrackModel? track, bool isPlaying)
        {
            try
            {
                IntPtr infoCenterClass = objc_getClass("MPNowPlayingInfoCenter");
                IntPtr dictClass = objc_getClass("NSMutableDictionary");
                if (infoCenterClass == IntPtr.Zero || dictClass == IntPtr.Zero) return;

                IntPtr defaultCenter = objc_msgSend(infoCenterClass, sel_registerName("defaultCenter"));
                if (defaultCenter == IntPtr.Zero) return;

                // 1 = Playing, 2 = Paused, 0 = Stopped
                nuint playbackState = (nuint)(track != null ? (isPlaying ? 1 : 2) : 0);
                objc_msgSend_nuint(defaultCenter, sel_registerName("setPlaybackState:"), playbackState);

                if (track != null)
                {
                    IntPtr dict = objc_msgSend(objc_msgSend(dictClass, sel_registerName("alloc")), sel_registerName("init"));
                    
                    IntPtr titleKey = CFStringCreateWithCString(IntPtr.Zero, "title", 0x08000100);
                    IntPtr titleVal = CFStringCreateWithCString(IntPtr.Zero, track.DisplayTitle, 0x08000100);
                    objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), titleVal, titleKey);
                    CFRelease(titleKey); CFRelease(titleVal);

                    if (!string.IsNullOrEmpty(track.Artist))
                    {
                        IntPtr artistKey = CFStringCreateWithCString(IntPtr.Zero, "artist", 0x08000100);
                        IntPtr artistVal = CFStringCreateWithCString(IntPtr.Zero, track.Artist, 0x08000100);
                        objc_msgSend_dict(dict, sel_registerName("setObject:forKey:"), artistVal, artistKey);
                        CFRelease(artistKey); CFRelease(artistVal);
                    }

                    objc_msgSend_set(defaultCenter, sel_registerName("setNowPlayingInfo:"), dict);
                    objc_msgSend(dict, sel_registerName("release"));
                }
                else
                {
                    objc_msgSend_set(defaultCenter, sel_registerName("setNowPlayingInfo:"), IntPtr.Zero);
                }
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
        Task QuitAsync();
        Task RaiseAsync();
        Task<bool> GetCanQuitAsync();
        Task<bool> GetCanRaiseAsync();
        Task<bool> GetHasTrackListAsync();
        Task<string> GetIdentityAsync();
        Task<string[]> GetSupportedUriSchemesAsync();
        Task<string[]> GetSupportedMimeTypesAsync();
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
        Task<string> GetPlaybackStatusAsync();
        Task<string> GetLoopStatusAsync();
        Task<double> GetRateAsync();
        Task<bool> GetShuffleAsync();
        Task<IDictionary<string, object>> GetMetadataAsync();
        Task<double> GetVolumeAsync();
        Task<long> GetPositionAsync();
        Task<double> GetMinimumRateAsync();
        Task<double> GetMaximumRateAsync();
        Task<bool> GetCanGoNextAsync();
        Task<bool> GetCanGoPreviousAsync();
        Task<bool> GetCanPlayAsync();
        Task<bool> GetCanPauseAsync();
        Task<bool> GetCanSeekAsync();
        Task<bool> GetCanControlAsync();
    }

    private sealed class LinuxMpris : IMediaPlayer2, IMediaPlayer2Player
    {
        public ObjectPath ObjectPath => new ObjectPath("/org/mpris/MediaPlayer2");
        private readonly MediaKeysService _parent;
        private IConnection? _connection;
        private TrackModel? _currentTrack;
        private bool _isPlaying;

        public LinuxMpris(MediaKeysService parent)
        {
            _parent = parent;
            _ = TryInitAsync();
        }

        private async Task TryInitAsync()
        {
            try
            {
                _connection = Connection.Session;
                await _connection.ConnectAsync();
                await _connection.RegisterObjectAsync(this);
                await _connection.RegisterServiceAsync("org.mpris.MediaPlayer2.Ultraudio");
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
        }

        public void Dispose() { }

        // IMediaPlayer2
        public Task QuitAsync() => Task.CompletedTask;
        public Task RaiseAsync() => Task.CompletedTask;
        public Task<bool> GetCanQuitAsync() => Task.FromResult(false);
        public Task<bool> GetCanRaiseAsync() => Task.FromResult(false);
        public Task<bool> GetHasTrackListAsync() => Task.FromResult(false);
        public Task<string> GetIdentityAsync() => Task.FromResult("Ultraudio");
        public Task<string[]> GetSupportedUriSchemesAsync() => Task.FromResult(Array.Empty<string>());
        public Task<string[]> GetSupportedMimeTypesAsync() => Task.FromResult(Array.Empty<string>());

        // IMediaPlayer2Player
        public Task NextAsync() { _parent.OnNext?.Invoke(); return Task.CompletedTask; }
        public Task PreviousAsync() { _parent.OnPrev?.Invoke(); return Task.CompletedTask; }
        public Task PauseAsync() { _parent.OnPause?.Invoke(); return Task.CompletedTask; }
        public Task PlayPauseAsync() { _parent.OnPlay?.Invoke(); return Task.CompletedTask; }
        public Task StopAsync() { _parent.OnStop?.Invoke(); return Task.CompletedTask; }
        public Task PlayAsync() { _parent.OnPlay?.Invoke(); return Task.CompletedTask; }
        public Task SeekAsync(long offset) => Task.CompletedTask;
        public Task SetPositionAsync(ObjectPath trackId, long position) => Task.CompletedTask;
        public Task OpenUriAsync(string uri) => Task.CompletedTask;
        public Task<string> GetPlaybackStatusAsync() => Task.FromResult(_currentTrack != null ? (_isPlaying ? "Playing" : "Paused") : "Stopped");
        public Task<string> GetLoopStatusAsync() => Task.FromResult("None");
        public Task<double> GetRateAsync() => Task.FromResult(1.0);
        public Task<bool> GetShuffleAsync() => Task.FromResult(false);
        
        public Task<IDictionary<string, object>> GetMetadataAsync()
        {
            var dict = new Dictionary<string, object>();
            if (_currentTrack != null)
            {
                dict["mpris:trackid"] = new ObjectPath($"/org/mpris/MediaPlayer2/TrackList/{Guid.NewGuid():N}");
                dict["xesam:title"] = _currentTrack.DisplayTitle;
                if (!string.IsNullOrEmpty(_currentTrack.Artist)) dict["xesam:artist"] = new[] { _currentTrack.Artist };
                if (!string.IsNullOrEmpty(_currentTrack.Album)) dict["xesam:album"] = _currentTrack.Album;
            }
            return Task.FromResult<IDictionary<string, object>>(dict);
        }

        public Task<double> GetVolumeAsync() => Task.FromResult(1.0);
        public Task<long> GetPositionAsync() => Task.FromResult(0L);
        public Task<double> GetMinimumRateAsync() => Task.FromResult(1.0);
        public Task<double> GetMaximumRateAsync() => Task.FromResult(1.0);
        public Task<bool> GetCanGoNextAsync() => Task.FromResult(true);
        public Task<bool> GetCanGoPreviousAsync() => Task.FromResult(true);
        public Task<bool> GetCanPlayAsync() => Task.FromResult(true);
        public Task<bool> GetCanPauseAsync() => Task.FromResult(true);
        public Task<bool> GetCanSeekAsync() => Task.FromResult(false);
        public Task<bool> GetCanControlAsync() => Task.FromResult(true);
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
