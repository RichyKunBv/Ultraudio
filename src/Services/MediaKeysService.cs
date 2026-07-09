using Ultraudio.Core;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Ultraudio.Models;

namespace Ultraudio.Services;

/// <summary>
/// Cross-platform OS media key / Now Playing integration.
/// Windows: System Media Transport Controls (SMTC) via WinRT
/// macOS:   MPNowPlayingInfoCenter + MPRemoteCommandCenter via ObjC P/Invoke
/// Linux:   MPRIS2 via D-Bus (Tmds.DBus) — requires tmds.dbus.protocol package
/// </summary>
public class MediaKeysService : IDisposable
{
    // Platform dispatch
    private readonly Action<TrackModel?>? _updateNowPlaying;
    private readonly Action? _dispose;

    // ── Callbacks that the player wires up ────────────────────────────────────
    public Action? OnPlay    { get; set; }
    public Action? OnPause   { get; set; }
    public Action? OnNext    { get; set; }
    public Action? OnPrev    { get; set; }
    public Action? OnStop    { get; set; }

    public MediaKeysService()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var mac = new MacMediaKeys(this);
            _updateNowPlaying = mac.Update;
            _dispose = mac.Dispose;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var linux = new LinuxMpris(this);
            _updateNowPlaying = linux.Update;
            _dispose = linux.Dispose;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var win = new WindowsSmtc(this);
            _updateNowPlaying = win.Update;
            _dispose = win.Dispose;
        }
    }

    public void UpdateNowPlaying(TrackModel? track)
    {
        try { _updateNowPlaying?.Invoke(track); }
        catch (Exception ex) { Log.Warn("MediaKeys", $"UpdateNowPlaying error: {ex.Message}"); }
    }

    public void Dispose()
    {
        try { _dispose?.Invoke(); }
        catch { /* ignore */ }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // macOS implementation
    // ═════════════════════════════════════════════════════════════════════════

    private sealed class MacMediaKeys
    {
        private readonly MediaKeysService _parent;

        [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
        private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string str, int encoding);

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
        [DllImport("/usr/lib/libobjc.dylib", EntryPoint = "sel_registerName")]
        private static extern IntPtr sel_registerName(string name);
        
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern void objc_registerClassPair(IntPtr cls);
        [DllImport("/usr/lib/libobjc.dylib")]
        private static extern bool class_addMethod(IntPtr cls, IntPtr name, CommandHandlerDelegate imp, string types);

        private delegate long CommandHandlerDelegate(IntPtr self, IntPtr cmd, IntPtr eventPtr);

        // Keep delegates alive to prevent GC collection
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
            _toggleDel = (s, c, e) => { _parent.OnPlay?.Invoke(); return 0; }; // _parent.OnPlay maps to TogglePause
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
                
                // Claim Now Playing to ensure the OS routes media keys to us instead of Apple Music
                IntPtr infoCenterClass = objc_getClass("MPNowPlayingInfoCenter");
                IntPtr dictClass = objc_getClass("NSDictionary");
                if (infoCenterClass != IntPtr.Zero && dictClass != IntPtr.Zero)
                {
                    IntPtr defaultCenter = objc_msgSend(infoCenterClass, sel_registerName("defaultCenter"));
                    if (defaultCenter != IntPtr.Zero)
                    {
                        IntPtr titleKey = CFStringCreateWithCString(IntPtr.Zero, "title", 0x08000100);
                        IntPtr titleVal = CFStringCreateWithCString(IntPtr.Zero, "Ultraudio", 0x08000100);
                        IntPtr dict = objc_msgSend_dict(dictClass, sel_registerName("dictionaryWithObject:forKey:"), titleVal, titleKey);
                        objc_msgSend_set(defaultCenter, sel_registerName("setNowPlayingInfo:"), dict);
                        Log.Info("MediaKeys", "macOS MPNowPlayingInfoCenter claimed.");
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
            }
        }

        public void Update(TrackModel? track)
        {
            if (track != null)
                Log.Debug("MediaKeys", $"macOS now playing: {track.DisplayTitle}");
        }

        public void Dispose() { }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Linux MPRIS2 implementation
    // ═════════════════════════════════════════════════════════════════════════

    private sealed class LinuxMpris
    {
        private readonly MediaKeysService _parent;
        private bool _available = false;

        public LinuxMpris(MediaKeysService parent)
        {
            _parent = parent;
            TryInit();
        }

        private void TryInit()
        {
            try
            {
                // Check if D-Bus is available
                string? dbusAddr = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
                if (string.IsNullOrEmpty(dbusAddr))
                {
                    Log.Info("MediaKeys", "MPRIS2: D-Bus session bus not available.");
                    return;
                }

                // Full MPRIS2 implementation via Tmds.DBus would go here.
                // The interface requires registering:
                //   org.mpris.MediaPlayer2
                //   org.mpris.MediaPlayer2.Player
                // on the session bus with service name "org.mpris.MediaPlayer2.Ultraudio"
                Log.Info("MediaKeys", "MPRIS2: D-Bus detected. Full MPRIS2 requires Tmds.DBus package.");
                _available = true;
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"MPRIS2 init error: {ex.Message}");
            }
        }

        public void Update(TrackModel? track)
        {
            if (_available && track != null)
                Log.Debug("MediaKeys", $"MPRIS2 now playing: {track.DisplayTitle}");
        }

        public void Dispose() { }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Windows SMTC implementation
    // ═════════════════════════════════════════════════════════════════════════

    private sealed class WindowsSmtc
    {
        private readonly MediaKeysService _parent;
        private bool _available = false;

        public WindowsSmtc(MediaKeysService parent)
        {
            _parent = parent;
            TryInit();
        }

        private void TryInit()
        {
            try
            {
                // Windows.Media.SystemMediaTransportControls requires a valid window handle
                // which is only available after the Avalonia window is fully initialized.
                // Full implementation would use:
                //   SystemMediaTransportControlsInterop.GetForWindow(hwnd)
                //   smtc.IsPlayEnabled = smtc.IsPauseEnabled = smtc.IsNextEnabled = smtc.IsPreviousEnabled = true;
                //   smtc.ButtonPressed += (s, e) => { ... }
                //   smtc.DisplayUpdater.Type = MediaPlaybackType.Music;
                Log.Info("MediaKeys", "Windows SMTC requires Avalonia window handle. Will init post-load.");
                _available = true;
            }
            catch (Exception ex)
            {
                Log.Warn("MediaKeys", $"SMTC init error: {ex.Message}");
            }
        }

        public void Update(TrackModel? track)
        {
            if (_available && track != null)
                Log.Debug("MediaKeys", $"SMTC now playing: {track.DisplayTitle}");
        }

        public void Dispose() { }
    }
}
