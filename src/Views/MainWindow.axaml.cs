using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ManagedBass.Cd;
using Ultraudio.Core;
using Ultraudio.Models;
using Ultraudio.Services;
using Ultraudio.Views.Windows;

namespace Ultraudio;

// ─────────────────────────────────────────────────────────────────────────────
// MainWindow — thin UI shell
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Main application window. Delegates business logic to extracted managers:
/// <see cref="PlaylistManager"/>, <see cref="SessionManager"/>, and coordinates
/// services (<see cref="AudioEngine"/>, <see cref="MediaKeysService"/>, etc.).
/// </summary>
public partial class MainWindow : Window
{
    // ── Core services ─────────────────────────────────────────────────────
    private readonly AudioEngine        _audio;
    private readonly PreferencesManager _prefs;
    private readonly CoverArtService    _coverArt;
    private readonly SpectrumAnalyzer   _spectrum;
    private readonly MediaKeysService   _mediaKeys;
    private readonly HttpRemoteService  _httpRemote;
    private readonly PlayHistory        _history;

    // ── Managers ──────────────────────────────────────────────────────────
    private readonly PlaylistManager _playlist;
    private readonly SessionManager  _session;

    // ── Platform Modifiers ────────────────────────────────────────────────
    private KeyModifiers ShortcutModifier =>
        RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? KeyModifiers.Meta : KeyModifiers.Control;

    // ── Playback UI state ─────────────────────────────────────────────────
    private bool _isDraggingSlider = false;
    private string? _pendingNextFile; // for gapless preload
    private readonly DispatcherTimer _timer;
    private DispatcherTimer? _cdTimer;
    private bool _cdWasReady = false;

    // ─────────────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        // ── Preferences ───────────────────────────────────────────────────
        _prefs = new PreferencesManager();
        _prefs.Load();

        // ── Managers ──────────────────────────────────────────────────────
        _playlist = new PlaylistManager(_prefs);
        _session  = new SessionManager(_prefs);

        // ── Audio — exclusive DAC access ──────────────────────────────────
        _audio = new AudioEngine();
        _audio.InitializeDevice(
            _prefs.Settings.LastDeviceIndex,
            UltraudioConstants.DefaultSampleRate);
        _audio.TrackEnded          += Audio_TrackEnded;
        _audio.GaplessPreloadReady += Audio_GaplessPreloadReady;

        // ── Services ──────────────────────────────────────────────────────
        _coverArt  = new CoverArtService();
        _spectrum  = new SpectrumAnalyzer(_audio);
        _history   = new PlayHistory();
        _mediaKeys = new MediaKeysService();
        _mediaKeys.OnNext  = () => Dispatcher.UIThread.Post(NextTrack);
        _mediaKeys.OnPrev  = () => Dispatcher.UIThread.Post(PrevTrack);
        _mediaKeys.OnPlay  = () => Dispatcher.UIThread.Post(TogglePlayback);
        _mediaKeys.OnPause = () => Dispatcher.UIThread.Post(TogglePlayback);

        _httpRemote = new HttpRemoteService();
        WireHttpRemote();
        if (_prefs.Settings.HttpApiEnabled)
            _httpRemote.Start(_prefs.Settings.HttpApiPort);

        // ── Restore preferences ───────────────────────────────────────────
        _playlist.ShuffleEnabled = _prefs.Settings.IsShuffleEnabled;
        _playlist.RepeatMode = Enum.TryParse<RepeatMode>(_prefs.Settings.RepeatMode, out var rm) ? rm : RepeatMode.Off;
        SliderVolumen.Value = _prefs.Settings.Volume;
        if (_prefs.Settings.IsMuted) _audio.ToggleMute();

        RestoreWindowPosition();
        UpdateShuffleButton();
        UpdateRepeatButton();

        // ── Global Keyboard Shortcuts ─────────────────────────────────────
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, Window_KeyUp, RoutingStrategies.Tunnel);
        
        // ── Fix Slider freezing ───────────────────────────────────────────
        SliderProgreso.AddHandler(PointerPressedEvent, SliderProgreso_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        SliderProgreso.AddHandler(PointerReleasedEvent, SliderProgreso_PointerReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);

        // ── Playlist binding ─────────────────────────────────────────────
        ListaReproduccion.ItemsSource = _playlist.FilteredItems;

        // ── Restore Session ───────────────────────────────────────────────
        if (_session.HasSavedSession)
        {
            _playlist.LoadTracks(_session.SavedQueue, append: false);

            if (_session.SavedCurrentIndex >= 0 && _session.SavedCurrentIndex < _playlist.Count)
            {
                _playlist.SetCurrentIndex(_session.SavedCurrentIndex);

                var track = _playlist.CurrentTrack!;
                _audio.Play(track.FilePath, _prefs.Settings.RamMode,
                    track.CueStartSeconds, track.CueEndSeconds, 0);
                _audio.TogglePause(); // Start paused
                _audio.PositionSeconds = _session.SavedPosition;
                _audio.Volume = SliderVolumen.Value;

                UpdatePlayerUI(track);
                _mediaKeys.UpdateNowPlaying(track, false);

                // Scroll to selected item
                var vm = _playlist.GetCurrentViewModel();
                if (vm != null && _playlist.FilteredItems.Contains(vm))
                {
                    ListaReproduccion.SelectedItem = vm;
                    ListaReproduccion.ScrollIntoView(vm);
                }

                // Update timer UI once
                Timer_Tick(null, EventArgs.Empty);
            }
        }

        // ── Progress timer ────────────────────────────────────────────────
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Timer_Tick;

        // ── Spectrum control ─────────────────────────────────────────────
        SpectrumViz.Initialize(_spectrum);
        if (!_prefs.Settings.SpectrumEnabled)
            SpectrumViz.Stop();

        // ── CD Button visibility and Polling ─────────────────────────────
        BtnCargarCd.IsVisible = false;

        if (UltraudioConstants.IsCdSupported)
        {
            _cdTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _cdTimer.Tick += (s, e) =>
            {
                if (!_prefs.Settings.CdEnabled)
                {
                    BtnCargarCd.IsVisible = false;
                    return;
                }

                try
                {
                    // Prevent grabbing the CD before Windows finishes mounting it
                    bool windowsDriveReady = true;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var cdDrive = DriveInfo.GetDrives()
                            .FirstOrDefault(d => d.DriveType == DriveType.CDRom);
                        if (cdDrive != null)
                            windowsDriveReady = cdDrive.IsReady;
                    }

                    if (windowsDriveReady && BassCd.DriveCount > 0)
                    {
                        bool isReady = BassCd.IsReady(0);
                        if (isReady != _cdWasReady)
                        {
                            _cdWasReady = isReady;
                            BtnCargarCd.IsVisible = isReady;

                            if (isReady)
                                BtnCargarCd_Click(null, null!);
                        }
                    }
                    else if (!windowsDriveReady && _cdWasReady)
                    {
                        _cdWasReady = false;
                        BtnCargarCd.IsVisible = false;
                    }
                }
                catch { /* CD polling is best-effort */ }
            };
            _cdTimer.Start();
        }

        CheckForUpdatesAsync();
    }

    // ─── Update Checker ───────────────────────────────────────────────────

    private async void CheckForUpdatesAsync()
    {
        var version = UpdateService.GetCurrentVersion();
        var (status, newVersion) = await UpdateService.CheckForUpdatesAsync(version);
        if (status == UpdateStatus.Outdated)
        {
            Dispatcher.UIThread.Post(() =>
            {
                BtnUpdate.IsVisible = true;
                ToolTip.SetTip(BtnUpdate, $"Nueva versión disponible: {newVersion}");
            });
        }
    }

    // ─── HTTP Remote wiring ───────────────────────────────────────────────

    private void WireHttpRemote()
    {
        _httpRemote.OnPlay   = () => Dispatcher.UIThread.Post(() => { if (!_audio.IsPlaying) TogglePlayback(); });
        _httpRemote.OnPause  = () => Dispatcher.UIThread.Post(() => { if (_audio.IsPlaying) TogglePlayback(); });
        _httpRemote.OnToggle = () => Dispatcher.UIThread.Post(TogglePlayback);
        _httpRemote.OnNext   = () => Dispatcher.UIThread.Post(NextTrack);
        _httpRemote.OnPrev   = () => Dispatcher.UIThread.Post(PrevTrack);
        _httpRemote.OnStop   = () => Dispatcher.UIThread.Post(() => { _audio.Stop(); _timer.Stop(); });
        _httpRemote.OnVolume = vol => Dispatcher.UIThread.Post(() => { SliderVolumen.Value = vol; _audio.Volume = vol; });
        _httpRemote.GetStatus = () => new
        {
            playing   = _audio.IsPlaying,
            position  = _audio.PositionSeconds,
            duration  = _audio.DurationSeconds,
            volume    = _audio.Volume,
            muted     = _audio.IsMuted,
            shuffle   = _playlist.ShuffleEnabled,
            repeat    = _playlist.RepeatMode.ToString(),
            track     = _playlist.CurrentTrack?.DisplayTitle
        };
        _httpRemote.GetPlaylist = () => _playlist.Tracks.Select((t, i) => new
        {
            index    = i,
            title    = t.DisplayTitle,
            artist   = t.Artist,
            duration = t.DurationDisplay
        }).ToList();
    }

    // ─── Window position persistence ──────────────────────────────────────

    private void RestoreWindowPosition()
    {
        var pos = _session.GetSavedWindowPosition();
        if (pos != null)
            Position = new PixelPoint(pos.Value.left, pos.Value.top);

        var (w, h) = _session.GetSavedWindowSize();
        Width  = w;
        Height = h;
    }

    // ─── Timer ────────────────────────────────────────────────────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider || _playlist.CurrentIndex < 0) return;

        double pos = _audio.PositionSeconds;
        double len = _audio.DurationSeconds;

        SliderProgreso.Maximum = len > 0 ? len : 100;
        SliderProgreso.Value   = pos;

        TxtTiempoActual.Text = TimeSpan.FromSeconds(pos).ToString(@"mm\:ss");
        TxtTiempoTotal.Text  = TimeSpan.FromSeconds(len).ToString(@"mm\:ss");
    }

    // ─── File loading ─────────────────────────────────────────────────────

    private async void BtnCargarArchivo_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar archivos de audio",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Audio sin pérdida")
                {
                    Patterns = UltraudioConstants.FilePickerPatterns
                }
            }
        });

        if (files.Count > 0)
        {
            try
            {
                var tracks = files
                    .Select(f => LibraryScanner.ScanFile(f.Path.LocalPath) ?? new TrackModel { FilePath = f.Path.LocalPath })
                    .ToList();
                LoadAndPlay(tracks, append: true);

                if (!string.IsNullOrEmpty(files[0].Path.LocalPath))
                    _prefs.Set(s => s.LastFolderPath = Path.GetDirectoryName(files[0].Path.LocalPath) ?? s.LastFolderPath);
            }
            catch (Exception ex)
            {
                Log.Error("FileLoader", "Error loading files", ex);
            }
        }
    }

    private async void BtnCargarCarpeta_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar carpeta de música",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            try
            {
                string path = folders[0].Path.LocalPath;
                _prefs.Set(s => s.LastFolderPath = path);

                var files = Directory
                    .GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => UltraudioConstants.LosslessExtensions.Contains(Path.GetExtension(f)))
                    .OrderBy(f => f)
                    .ToList();

                if (files.Count > 0)
                {
                    var tracks = files
                        .Select(f => LibraryScanner.ScanFile(f) ?? new TrackModel { FilePath = f })
                        .ToList();
                    LoadAndPlay(tracks, append: true);
                }
            }
            catch (Exception ex)
            {
                Log.Error("FileLoader", "Error loading folder", ex);
            }
        }
    }

    private async void BtnCargarCue_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Seleccionar hoja CUE",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Hoja CUE") { Patterns = new[] { "*.cue" } }
            }
        });

        if (files.Count > 0)
        {
            try
            {
                var tracks = CueParser.Parse(files[0].Path.LocalPath);
                if (tracks.Count > 0)
                    LoadAndPlay(tracks, append: true);
            }
            catch (Exception ex)
            {
                Log.Error("FileLoader", "Error loading CUE sheet", ex);
            }
        }
    }

    private void BtnCargarCd_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            int driveCount = BassCd.DriveCount;
            if (driveCount <= 0) return;

            int drive = 0;
            if (!BassCd.IsReady(drive)) return;

            int trackCount = BassCd.GetTracks(drive);
            if (trackCount < 0) return;

            // Fetch CDDB or local CD-TEXT
            string album = "CD Audio";
            string artist = "Artista desconocido";
            string[] textLines = BassCd.GetIDText(drive);

            if (textLines != null && textLines.Length > 0)
            {
                foreach (string line in textLines)
                {
                    if (line.StartsWith("TITLE=") && album == "CD Audio")
                        album = line.Substring(6);
                    if (line.StartsWith("PERFORMER=") && artist == "Artista desconocido")
                        artist = line.Substring(10);
                }
            }

            var tracks = new List<TrackModel>();
            for (int i = 0; i < trackCount; i++)
            {
                int lenBytes = BassCd.GetTrackLength(drive, i);
                double duration = lenBytes != -1 ? lenBytes / 176400.0 : 0;

                tracks.Add(new TrackModel
                {
                    FilePath = $"{UltraudioConstants.CdProtocolPrefix}{drive}/{i}",
                    Title = $"Pista {(i + 1):00}",
                    Album = album,
                    Artist = artist,
                    Format = "CDA",
                    BitDepth = 16,
                    SampleRate = UltraudioConstants.DefaultSampleRate,
                    Bitrate = 1411,
                    Duration = TimeSpan.FromSeconds(duration > 0 ? duration : 0)
                });
            }

            if (tracks.Count > 0)
                LoadAndPlay(tracks, append: true);
        }
        catch (Exception ex)
        {
            Log.Error("CD", "CD loading error", ex);
        }
    }

    // ── Unified load + play helper ────────────────────────────────────────

    private void LoadAndPlay(List<TrackModel> tracks, bool append)
    {
        int startIndex = _playlist.LoadTracks(tracks, append);
        UpdatePlaylistCount();

        if (!append && _playlist.Count > 0)
            PlayTrackAtIndex(0);
        else if (append && !_audio.IsPlaying && startIndex < _playlist.Count)
            PlayTrackAtIndex(startIndex);
    }

    private void BtnClearPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        _playlist.Clear();
        _audio.Stop();
        _timer.Stop();
        ResetPlayerUI();
        UpdatePlaylistCount();
    }

    private void BtnMoveUp_Click(object? sender, RoutedEventArgs e)
    {
        if (ListaReproduccion.SelectedItem is PlaylistItemViewModel vm)
        {
            if (_playlist.MoveUp(vm))
                ListaReproduccion.SelectedItem = vm;
        }
    }

    private void BtnMoveDown_Click(object? sender, RoutedEventArgs e)
    {
        if (ListaReproduccion.SelectedItem is PlaylistItemViewModel vm)
        {
            if (_playlist.MoveDown(vm))
                ListaReproduccion.SelectedItem = vm;
        }
    }

    private async void BtnSavePlaylist_Click(object? sender, RoutedEventArgs e)
    {
        if (_playlist.Count == 0) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Guardar Lista de Reproducción",
            DefaultExtension = "m3u8",
            SuggestedFileName = "Lista.m3u8",
            FileTypeChoices = new[] { new FilePickerFileType("Lista M3U8") { Patterns = new[] { "*.m3u8" } } }
        });

        if (file != null)
        {
            try
            {
                using var stream = await file.OpenWriteAsync();
                using var writer = new StreamWriter(stream);
                await writer.WriteLineAsync("#EXTM3U");
                foreach (var track in _playlist.Tracks)
                {
                    await writer.WriteLineAsync($"#EXTINF:{(int)track.Duration.TotalSeconds},{track.Artist} - {track.DisplayTitle}");
                    await writer.WriteLineAsync(track.FilePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Playlist", "Error saving playlist", ex);
            }
        }
    }

    private void UpdatePlaylistCount()
    {
        TxtPlaylistCount.Text = _playlist.PlaylistCountText;
    }

    // ─── Playback ─────────────────────────────────────────────────────────

    private void PlayTrackAtIndex(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;

        _playlist.SetCurrentIndex(index);

        // Scroll to selected in filtered list
        var vm = _playlist.GetCurrentViewModel();
        if (vm != null && _playlist.FilteredItems.Contains(vm))
        {
            ListaReproduccion.SelectedItem = vm;
            ListaReproduccion.ScrollIntoView(vm);
        }

        var track = _playlist.CurrentTrack!;
        bool ramMode = _prefs.Settings.RamMode;

        // Check if we have a preloaded gapless stream for this track
        int preloaded = 0;
        if (_pendingNextFile == track.FilePath)
        {
            preloaded = _audio.GetPreloadedStream();
            _pendingNextFile = null;
        }

        _audio.Play(
            track.FilePath, ramMode,
            track.CueStartSeconds, track.CueEndSeconds,
            preloaded);

        _audio.Volume = SliderVolumen.Value;
        _timer.Start();

        UpdatePlayerUI(track);
        _history.RecordPlay(track.FilePath, track.DisplayTitle);
        _mediaKeys.UpdateNowPlaying(track, true);
        BtnReproducir.Content = "⏸";
    }

    private void UpdatePlayerUI(TrackModel track)
    {
        TxtTitle.Text  = track.DisplayTitle;
        TxtArtist.Text = !string.IsNullOrEmpty(track.Artist) ? track.Artist : string.Empty;
        TxtAlbum.Text  = !string.IsNullOrEmpty(track.Album)  ? track.Album  : string.Empty;

        // Tech badge
        if (!string.IsNullOrEmpty(track.TechBadge))
        {
            TxtTechInfo.Text = track.TechBadge;
            BorderTechInfo.IsVisible = true;
        }
        else
        {
            BorderTechInfo.IsVisible = false;
        }

        // ReplayGain badge
        if (!double.IsNaN(track.ReplayGainTrack))
        {
            TxtReplayGain.Text = $"RG {track.ReplayGainTrack:+0.0;-0.0} dB";
            BorderReplayGain.IsVisible = true;
        }
        else
        {
            BorderReplayGain.IsVisible = false;
        }

        // Favorite button
        BtnFavorite.Content = track.IsFavorite ? "★" : "☆";

        // Cover art
        UpdateCoverArt(track.FilePath);
    }

    private void UpdateCoverArt(string filePath)
    {
        Bitmap? bmp = _coverArt.GetCover(filePath);
        if (bmp != null)
        {
            ImgCoverArt.Source    = bmp;
            ImgCoverArt.IsVisible = true;
            TxtNoCover.IsVisible  = false;
        }
        else
        {
            ImgCoverArt.IsVisible = false;
            TxtNoCover.IsVisible  = true;
        }
    }

    private void ResetPlayerUI()
    {
        TxtTitle.Text  = "Sin pista seleccionada";
        TxtArtist.Text = string.Empty;
        TxtAlbum.Text  = string.Empty;
        BorderTechInfo.IsVisible   = false;
        BorderReplayGain.IsVisible = false;
        ImgCoverArt.IsVisible = false;
        TxtNoCover.IsVisible  = true;
        TxtTiempoActual.Text = "00:00";
        TxtTiempoTotal.Text  = "00:00";
        SliderProgreso.Value = 0;
        BtnReproducir.Content = "▶";
    }

    // ─── Navigation ───────────────────────────────────────────────────────

    private void NextTrack()
    {
        if (_playlist.Count == 0) return;

        int next = _playlist.GetNextIndex();
        if (next >= 0)
        {
            PlayTrackAtIndex(next);
        }
        else
        {
            _timer.Stop();
            BtnReproducir.Content = "▶";
        }
    }

    private void PrevTrack()
    {
        if (_audio.PositionSeconds > 3)
        {
            _audio.PositionSeconds = 0;
            return;
        }

        int prev = _playlist.GetPreviousIndex();
        if (prev >= 0)
            PlayTrackAtIndex(prev);
        else
            _audio.PositionSeconds = 0;
    }

    // ─── Event handlers – playback ────────────────────────────────────────

    private void Audio_TrackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BtnReproducir.Content = "▶";
            NextTrack();
        });
    }

    private void Audio_GaplessPreloadReady(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_prefs.Settings.GaplessEnabled) return;
            int nextIdx = _playlist.PeekNextIndex();
            if (nextIdx < 0) return;

            var nextTrack = _playlist.Tracks[nextIdx];
            _pendingNextFile = nextTrack.FilePath;
            _audio.PreloadStream(nextTrack.FilePath, _prefs.Settings.RamMode);
        });
    }

    // ─── Transport button handlers ────────────────────────────────────────

    private void BtnReproducir_Click(object? sender, RoutedEventArgs e)
    {
        if (_playlist.CurrentIndex < 0 && _playlist.Count > 0)
        {
            PlayTrackAtIndex(0);
            return;
        }
        TogglePlayback();
    }

    private void TogglePlayback()
    {
        if (_playlist.CurrentIndex >= 0)
        {
            _audio.TogglePause();
            BtnReproducir.Content = _audio.IsPlaying ? "⏸" : "▶";
            _mediaKeys.UpdateNowPlaying(_playlist.CurrentTrack, _audio.IsPlaying);
        }
    }

    private void BtnAnterior_Click(object? sender, RoutedEventArgs e) => PrevTrack();
    private void BtnSiguiente_Click(object? sender, RoutedEventArgs e) => NextTrack();

    private void BtnMute_Click(object? sender, RoutedEventArgs e)
    {
        _audio.ToggleMute();
        BtnMute.Content = _audio.IsMuted ? "🔇" : "🔊";
    }

    private void BtnShuffle_Click(object? sender, RoutedEventArgs e)
    {
        _playlist.ShuffleEnabled = !_playlist.ShuffleEnabled;
        UpdateShuffleButton();
        _prefs.Set(s => s.IsShuffleEnabled = _playlist.ShuffleEnabled);
    }

    private void BtnRepeat_Click(object? sender, RoutedEventArgs e)
    {
        _playlist.RepeatMode = _playlist.RepeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _              => RepeatMode.Off
        };
        UpdateRepeatButton();
        _prefs.Set(s => s.RepeatMode = _playlist.RepeatMode.ToString());
    }

    private void UpdateShuffleButton()
    {
        BtnShuffle.Classes.Clear();
        BtnShuffle.Classes.Add(_playlist.ShuffleEnabled ? "toggle-active" : "toggle-inactive");
    }

    private void UpdateRepeatButton()
    {
        BtnRepeat.Classes.Clear();
        BtnRepeat.Content = _playlist.RepeatMode switch
        {
            RepeatMode.One => "↺¹",
            _              => "↺"
        };
        BtnRepeat.Classes.Add(_playlist.RepeatMode != RepeatMode.Off ? "toggle-active" : "toggle-inactive");
        ToolTip.SetTip(BtnRepeat, _playlist.RepeatMode switch
        {
            RepeatMode.One => "Repetir: Una",
            RepeatMode.All => "Repetir: Todas",
            _              => "Repetir (R)"
        });
    }

    private void BtnUpdate_Click(object? sender, RoutedEventArgs e)
    {
        var uri = UpdateService.GetDirectDownloadUrl();
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = uri, UseShellExecute = true });
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                System.Diagnostics.Process.Start("xdg-open", uri);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                System.Diagnostics.Process.Start("open", uri);
        }
        catch (Exception ex)
        {
            Log.Error("Update", "Could not open update URL", ex);
        }
    }

    // ─── Favorite ─────────────────────────────────────────────────────────

    private void BtnFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (_playlist.CurrentIndex < 0) return;
        _playlist.ToggleFavorite(_playlist.CurrentIndex);
        // Update main star button
        var track = _playlist.CurrentTrack;
        if (track != null)
            BtnFavorite.Content = track.IsFavorite ? "★" : "☆";
    }

    private void FavButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PlaylistItemViewModel vm)
        {
            int idx = _playlist.IndexOf(vm.Track);
            if (idx >= 0)
            {
                _playlist.ToggleFavorite(idx);
                // Update main star if this is the current track
                if (idx == _playlist.CurrentIndex)
                    BtnFavorite.Content = vm.Track.IsFavorite ? "★" : "☆";
            }
        }
    }

    // ─── Settings ────────────────────────────────────────────────────────

    private async void BtnSettings_Click(object? sender, RoutedEventArgs e)
    {
        var devices = _audio.GetDevices();
        var win = new SettingsWindow(_prefs.Settings, devices);
        await win.ShowDialog(this);

        if (win.Saved)
        {
            _audio.ChangeDevice(_prefs.Settings.LastDeviceIndex);

            if (_prefs.Settings.HttpApiEnabled && !_httpRemote.IsRunning)
                _httpRemote.Start(_prefs.Settings.HttpApiPort);
            else if (!_prefs.Settings.HttpApiEnabled && _httpRemote.IsRunning)
                _httpRemote.Stop();

            if (_prefs.Settings.SpectrumEnabled)
                SpectrumViz.Resume();
            else
                SpectrumViz.Stop();

            // Update CD button based on settings change
            if (UltraudioConstants.IsCdSupported)
            {
                if (!_prefs.Settings.CdEnabled)
                    BtnCargarCd.IsVisible = false;
                else
                {
                    try { BtnCargarCd.IsVisible = BassCd.IsReady(0); } catch { }
                }
            }
        }
    }

    // ─── Seekbar ─────────────────────────────────────────────────────────

    private void SliderProgreso_PointerPressed(object? sender, PointerPressedEventArgs e)
        => _isDraggingSlider = true;

    private void SliderProgreso_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingSlider = false;
        if (_playlist.CurrentIndex >= 0)
            _audio.PositionSeconds = SliderProgreso.Value;
    }

    private void SliderProgreso_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { }

    // ─── Volume ───────────────────────────────────────────────────────────

    private void SliderVolumen_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_audio != null)
        {
            _audio.Volume = SliderVolumen.Value;
            TxtVolumePercent.Text = $"{(int)(SliderVolumen.Value * 100)}%";
        }
    }

    // ─── Search ───────────────────────────────────────────────────────────

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _playlist.ApplySearchFilter(TxtSearch.Text ?? string.Empty);
        UpdatePlaylistCount();
    }

    // ─── Playlist selection ───────────────────────────────────────────────

    private void ListaReproduccion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ListaReproduccion.SelectedItem is PlaylistItemViewModel vm)
        {
            int idx = _playlist.IndexOf(vm.Track);
            if (idx >= 0 && idx != _playlist.CurrentIndex)
                PlayTrackAtIndex(idx);
        }
    }

    // ─── Keyboard shortcuts ───────────────────────────────────────────────

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        // Don't intercept when search box is focused
        if (TxtSearch.IsFocused) return;

        switch (e.Key)
        {
            case Key.Space:
                e.Handled = true;
                BtnReproducir_Click(null, new RoutedEventArgs());
                break;
            case Key.Right when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                NextTrack();
                break;
            case Key.Right when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                if (_audio.IsPlaying || _audio.PositionSeconds > 0)
                    _audio.PositionSeconds = Math.Max(0, _audio.PositionSeconds + 5);
                break;
            case Key.Left when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                PrevTrack();
                break;
            case Key.Left when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                if (_audio.IsPlaying || _audio.PositionSeconds > 0)
                    _audio.PositionSeconds = Math.Max(0, _audio.PositionSeconds - 5);
                break;
            case Key.Up when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                SliderVolumen.Value = Math.Min(1.0, SliderVolumen.Value + 0.1);
                break;
            case Key.Down when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                SliderVolumen.Value = Math.Max(0.0, SliderVolumen.Value - 0.1);
                break;
            case Key.M when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                BtnMute_Click(null, new RoutedEventArgs());
                break;
            case Key.S when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                BtnShuffle_Click(null, new RoutedEventArgs());
                break;
            case Key.R when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                BtnRepeat_Click(null, new RoutedEventArgs());
                break;
            case Key.B when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                BtnFavorite_Click(null, new RoutedEventArgs());
                break;
            case Key.F when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                TxtSearch.Focus();
                break;
            case Key.MediaPlayPause:
            case Key.F8:
                e.Handled = true;
                BtnReproducir_Click(null, new RoutedEventArgs());
                break;
            case Key.MediaPreviousTrack:
            case Key.F7:
                e.Handled = true;
                PrevTrack();
                break;
            case Key.MediaNextTrack:
            case Key.F9:
                e.Handled = true;
                NextTrack();
                break;
            case Key.MediaStop:
                e.Handled = true;
                _audio.Stop();
                break;
            case Key.O when e.KeyModifiers == ShortcutModifier:
                e.Handled = true;
                BtnCargarArchivo_Click(null, new RoutedEventArgs());
                break;
            case Key.O when e.KeyModifiers == (ShortcutModifier | KeyModifiers.Shift):
                e.Handled = true;
                BtnCargarCarpeta_Click(null, new RoutedEventArgs());
                break;
        }
    }

    private void Window_KeyUp(object? sender, KeyEventArgs e)
    {
        if (TxtSearch.IsFocused) return;

        switch (e.Key)
        {
            case Key.Space:
            case Key.MediaPlayPause:
            case Key.F8:
            case Key.MediaPreviousTrack:
            case Key.F7:
            case Key.MediaNextTrack:
            case Key.F9:
            case Key.MediaStop:
                e.Handled = true;
                break;
            case Key.Right:
            case Key.Left:
            case Key.Up:
            case Key.Down:
                if (e.KeyModifiers == ShortcutModifier || e.KeyModifiers == KeyModifiers.None)
                    e.Handled = true;
                break;
        }
    }

    // ─── Mouse wheel = volume ─────────────────────────────────────────────

    private void Window_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ListaReproduccion.IsPointerOver) return;
        double delta = e.Delta.Y * 0.05;
        SliderVolumen.Value = Math.Clamp(SliderVolumen.Value + delta, 0, 1);
        e.Handled = true;
    }

    // ─── Menu handlers ────────────────────────────────────────────────────

    private async void AcercaDe_Click(object? sender, EventArgs e)
    {
        var w = new AboutWindow();
        await w.ShowDialog(this);
    }

    private async void UpdateCheck_Click(object? sender, EventArgs e)
    {
        var w = new UpdateWindow();
        await w.ShowDialog(this);
    }

    private async void UpdateHistory_Click(object? sender, EventArgs e)
    {
        var w = new HistoryWindow();
        await w.ShowDialog(this);
    }

    private void Salir_Click(object? sender, EventArgs e) => Close();

    // ─── Window close ─────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        // Save session via SessionManager
        _session.SaveSession(
            _playlist.Tracks,
            _playlist.CurrentIndex,
            _audio.PositionSeconds,
            SliderVolumen.Value,
            _audio.IsMuted,
            _playlist.ShuffleEnabled,
            _playlist.RepeatMode,
            Width, Height,
            Position.X, Position.Y);

        _history.Save();

        // Cleanup
        _timer.Stop();
        _cdTimer?.Stop();
        SpectrumViz.Stop();
        _httpRemote.Dispose();
        _mediaKeys.Dispose();
        _coverArt.ClearCache();
        _audio.Release();

        base.OnClosed(e);
    }
}