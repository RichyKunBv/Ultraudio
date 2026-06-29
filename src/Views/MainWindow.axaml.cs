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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using TagLib;
using Ultraudio.Core;
using Ultraudio.Models;
using Ultraudio.Services;
using Ultraudio.Views.Windows;
using System.Runtime.InteropServices;
using ManagedBass.Cd;

namespace Ultraudio;

// ─────────────────────────────────────────────────────────────────────────────
// Playlist item view model
// ─────────────────────────────────────────────────────────────────────────────

public class PlaylistItemViewModel : INotifyPropertyChanged
{
    public TrackModel Track { get; }

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); OnPropertyChanged(nameof(TitleColor)); }
    }

    public string DisplayTitle => Track.DisplayTitle;
    public string TitleColor   => _isPlaying ? "#00E576" : "#CCCCCC";
    public string FavStar      => Track.IsFavorite ? "★" : "☆";
    public string FavColor     => Track.IsFavorite ? "#F5C542" : "#333333";

    public void RefreshFav() { OnPropertyChanged(nameof(FavStar)); OnPropertyChanged(nameof(FavColor)); }

    public PlaylistItemViewModel(TrackModel track) => Track = track;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ─────────────────────────────────────────────────────────────────────────────
// MainWindow
// ─────────────────────────────────────────────────────────────────────────────

public partial class MainWindow : Window
{
    // ── Core services ─────────────────────────────────────────────────────────
    private readonly AudioEngine          _audio;
    private readonly PreferencesManager   _prefs;
    private readonly CoverArtService      _coverArt;
    private readonly SpectrumAnalyzer     _spectrum;
    private readonly MediaKeysService     _mediaKeys;
    private readonly HttpRemoteService    _httpRemote;
    private readonly PlayHistory          _history;

    // ── Playlist state ────────────────────────────────────────────────────────
    private readonly ObservableCollection<PlaylistItemViewModel> _allItems   = new();
    private readonly ObservableCollection<PlaylistItemViewModel> _filteredItems = new();
    private readonly List<TrackModel> _playlist = new();
    private int _currentIndex  = -1;
    private int[] _shuffleOrder = Array.Empty<int>();
    private int _shufflePos = 0;

    // ── Playback state ────────────────────────────────────────────────────────
    private bool _isDraggingSlider = false;
    private RepeatMode _repeatMode = RepeatMode.Off;
    private bool _shuffleEnabled = false;
    private string _searchText = string.Empty;
    private string? _pendingNextFile; // for gapless preload
    private readonly DispatcherTimer _timer;
    private DispatcherTimer? _cdTimer;
    private bool _cdWasReady = false;

    // ─────────────────────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();

        // ── Preferences ───────────────────────────────────────────────────────
        _prefs = new PreferencesManager();
        _prefs.Load();

        // ── Audio ─────────────────────────────────────────────────────────────
        _audio = new AudioEngine();
        _audio.InicializarDispositivo(
            _prefs.Settings.LastDeviceIndex,
            44100);
        _audio.TrackEnded         += Audio_TrackEnded;
        _audio.GaplessPreloadReady += Audio_GaplessPreloadReady;

        // ── Services ──────────────────────────────────────────────────────────
        _coverArt  = new CoverArtService();
        _spectrum  = new SpectrumAnalyzer(_audio);
        _history   = new PlayHistory();
        _mediaKeys = new MediaKeysService();
        _mediaKeys.OnNext  = () => Dispatcher.UIThread.Post(NextTrack);
        _mediaKeys.OnPrev  = () => Dispatcher.UIThread.Post(PrevTrack);
        _mediaKeys.OnPlay  = () => Dispatcher.UIThread.Post(() => _audio.AlternarPausa());
        _mediaKeys.OnPause = () => Dispatcher.UIThread.Post(() => _audio.AlternarPausa());

        _httpRemote = new HttpRemoteService();
        WireHttpRemote();
        if (_prefs.Settings.HttpApiEnabled)
            _httpRemote.Start(_prefs.Settings.HttpApiPort);

        // ── UI devices ────────────────────────────────────────────────────────
        // (device selector now lives in SettingsWindow)

        // ── Restore preferences ───────────────────────────────────────────────
        _shuffleEnabled = _prefs.Settings.IsShuffleEnabled;
        _repeatMode     = Enum.TryParse<RepeatMode>(_prefs.Settings.RepeatMode, out var rm) ? rm : RepeatMode.Off;
        SliderVolumen.Value = _prefs.Settings.Volume;
        if (_prefs.Settings.IsMuted) _audio.ToggleMute();

        RestoreWindowPosition();
        UpdateShuffleButton();
        UpdateRepeatButton();

        // ── Playlist binding ─────────────────────────────────────────────────
        ListaReproduccion.ItemsSource = _filteredItems;

        // ── Progress timer ────────────────────────────────────────────────────
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += Timer_Tick;

        // ── Spectrum control ─────────────────────────────────────────────────
        SpectrumViz.Initialize(_spectrum);
        if (!_prefs.Settings.SpectrumEnabled)
            SpectrumViz.Stop();

        // ── CD Button visibility and Polling ───────────────────────────────────
        bool isCdSupported = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) 
                             && RuntimeInformation.OSArchitecture == Architecture.X64;
        BtnCargarCd.IsVisible = false;

        if (isCdSupported)
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
                    if (BassCd.DriveCount > 0)
                    {
                        bool isReady = BassCd.IsReady(0);
                        if (isReady != _cdWasReady)
                        {
                            _cdWasReady = isReady;
                            BtnCargarCd.IsVisible = isReady;

                            if (isReady)
                            {
                                // Auto-load tracks when CD is inserted
                                BtnCargarCd_Click(null, null);
                            }
                        }
                    }
                }
                catch { }
            };
            _cdTimer.Start();
        }
    }

    // ─── HTTP Remote wiring ───────────────────────────────────────────────────

    private void WireHttpRemote()
    {
        _httpRemote.OnPlay   = () => Dispatcher.UIThread.Post(() => { if (!_audio.EstaReproduciendo) _audio.AlternarPausa(); });
        _httpRemote.OnPause  = () => Dispatcher.UIThread.Post(() => { if (_audio.EstaReproduciendo)  _audio.AlternarPausa(); });
        _httpRemote.OnToggle = () => Dispatcher.UIThread.Post(() => _audio.AlternarPausa());
        _httpRemote.OnNext   = () => Dispatcher.UIThread.Post(NextTrack);
        _httpRemote.OnPrev   = () => Dispatcher.UIThread.Post(PrevTrack);
        _httpRemote.OnStop   = () => Dispatcher.UIThread.Post(() => { _audio.Detener(); _timer.Stop(); });
        _httpRemote.OnVolume = vol => Dispatcher.UIThread.Post(() => { SliderVolumen.Value = vol; _audio.Volumen = vol; });
        _httpRemote.GetStatus = () => new
        {
            playing   = _audio.EstaReproduciendo,
            position  = _audio.PosicionSegundos,
            duration  = _audio.DuracionSegundos,
            volume    = _audio.Volumen,
            muted     = _audio.IsMuted,
            shuffle   = _shuffleEnabled,
            repeat    = _repeatMode.ToString(),
            track     = _currentIndex >= 0 ? _playlist[_currentIndex].DisplayTitle : null
        };
        _httpRemote.GetPlaylist = () => _playlist.Select((t, i) => new
        {
            index  = i,
            title  = t.DisplayTitle,
            artist = t.Artist,
            duration = t.DurationDisplay
        }).ToList();
    }

    // ─── Window position persistence ──────────────────────────────────────────

    private void RestoreWindowPosition()
    {
        if (_prefs.Settings.WindowLeft >= 0 && _prefs.Settings.WindowTop >= 0)
        {
            Position = new PixelPoint(
                (int)_prefs.Settings.WindowLeft,
                (int)_prefs.Settings.WindowTop);
        }
        Width  = _prefs.Settings.WindowWidth;
        Height = _prefs.Settings.WindowHeight;
    }

    // ─── Timer ────────────────────────────────────────────────────────────────

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_isDraggingSlider || _currentIndex < 0) return;

        double pos = _audio.PosicionSegundos;
        double len = _audio.DuracionSegundos;

        SliderProgreso.Maximum = len > 0 ? len : 100;
        SliderProgreso.Value   = pos;

        TxtTiempoActual.Text = TimeSpan.FromSeconds(pos).ToString(@"mm\:ss");
        TxtTiempoTotal.Text  = TimeSpan.FromSeconds(len).ToString(@"mm\:ss");
    }

    // ─── File loading ──────────────────────────────────────────────────────────

    private async void BtnCargarArchivo_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select audio files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Lossless Audio")
                {
                    Patterns = new[] { "*.flac", "*.wav", "*.aiff", "*.aif", "*.dsf", "*.dff" }
                }
            }
        });

        if (files.Count > 0)
        {
            var tracks = files
                .Select(f => LibraryScanner.ScanFile(f.Path.LocalPath) ?? new TrackModel { FilePath = f.Path.LocalPath })
                .ToList();
            LoadPlaylist(tracks, autoPlay: true);

            if (!string.IsNullOrEmpty(files[0].Path.LocalPath))
                _prefs.Set(s => s.LastFolderPath = Path.GetDirectoryName(files[0].Path.LocalPath) ?? s.LastFolderPath);
        }
    }

    private async void BtnCargarCarpeta_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select music folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string path = folders[0].Path.LocalPath;
            _prefs.Set(s => s.LastFolderPath = path);

            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".flac", ".wav", ".aiff", ".aif", ".dsf", ".dff" };

            var files = Directory
                .GetFiles(path, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => extensions.Contains(Path.GetExtension(f)))
                .OrderBy(f => f)
                .ToList();

            if (files.Count > 0)
            {
                var tracks = files
                    .Select(f => LibraryScanner.ScanFile(f) ?? new TrackModel { FilePath = f })
                    .ToList();
                LoadPlaylist(tracks, autoPlay: true);
            }
        }
    }

    private async void BtnCargarCue_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select CUE sheet",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("CUE Sheet") { Patterns = new[] { "*.cue" } }
            }
        });

        if (files.Count > 0)
        {
            var tracks = CueParser.Parse(files[0].Path.LocalPath);
            if (tracks.Count > 0)
                LoadPlaylist(tracks, autoPlay: true);
        }
    }

    private void BtnCargarCd_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            int driveCount = BassCd.DriveCount;
            if (driveCount <= 0)
            {
                // No CD drives found
                return;
            }

            int drive = 0; // Default to first drive, might need drive selector for multiple
            if (!BassCd.IsReady(drive))
            {
                return;
            }

            int trackCount = BassCd.GetTracks(drive);
            if (trackCount < 0) return;

            // Fetch CDDB or local CD-TEXT
            string album = "CD Audio";
            string artist = "Unknown Artist";
            var trackNames = new string[trackCount];
            
            // Try CD-TEXT first
            string[] textLines = BassCd.GetIDText(drive);
            if (textLines != null && textLines.Length > 0)
            {
                foreach (string line in textLines)
                {
                    if (line.StartsWith("TITLE=") && album == "CD Audio") album = line.Substring(6);
                    if (line.StartsWith("PERFORMER=") && artist == "Unknown Artist") artist = line.Substring(10);
                }
            }

            var tracks = new List<TrackModel>();
            for (int i = 0; i < trackCount; i++)
            {
                string title = $"Track {(i + 1):00}";
                if (textLines != null && textLines.Length > 0)
                {
                    // Search for TITLE= per track
                    string trackPrefix = $"TRACK{(i + 1):00}";
                    foreach (string line in textLines)
                    {
                        if (line.StartsWith("TITLE=") && line.Contains(title)) // simple heuristics, usually basscd returns it formatted if requested properly
                        {
                            // A better way is using BassCd.GetIDText for the track or parsing the returned array 
                            // but in ManagedBass, GetIDText just returns array of strings. We can use a simple naming.
                        }
                    }
                }
                
                int lenBytes = BassCd.GetTrackLength(drive, i);
                double duration = -1;
                if (lenBytes != -1)
                {
                    // BASS_CD uses 44100Hz, 16bit, stereo = 176400 bytes/sec
                    duration = lenBytes / 176400.0;
                }

                tracks.Add(new TrackModel
                {
                    FilePath = $"cda://{drive}/{i}",
                    Title = title,
                    Album = album,
                    Artist = artist,
                    Format = "CDA",
                    BitDepth = 16,
                    SampleRate = 44100,
                    Bitrate = 1411,
                    Duration = TimeSpan.FromSeconds(duration > 0 ? duration : 0)
                });
            }

            if (tracks.Count > 0)
                LoadPlaylist(tracks, autoPlay: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CD Error]: {ex.Message}");
        }
    }

    private void BtnClearPlaylist_Click(object? sender, RoutedEventArgs e)
    {
        _playlist.Clear();
        _allItems.Clear();
        _filteredItems.Clear();
        _currentIndex = -1;
        _audio.Detener();
        _timer.Stop();
        ResetPlayerUI();
    }

    // ─── Playlist management ──────────────────────────────────────────────────

    private void LoadPlaylist(List<TrackModel> tracks, bool autoPlay = false)
    {
        _playlist.Clear();
        _playlist.AddRange(tracks);
        _allItems.Clear();

        foreach (var t in tracks)
        {
            t.IsFavorite = _prefs.Settings.Favorites.Contains(t.FilePath);
            _allItems.Add(new PlaylistItemViewModel(t));
        }

        ApplySearchFilter(_searchText);
        UpdatePlaylistCount();

        if (autoPlay && _playlist.Count > 0)
            ReproducirIndice(0);
    }

    private void ApplySearchFilter(string query)
    {
        _filteredItems.Clear();
        foreach (var item in _allItems)
        {
            if (string.IsNullOrWhiteSpace(query) ||
                item.Track.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Track.Artist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Track.Album.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                _filteredItems.Add(item);
            }
        }
    }

    private void UpdatePlaylistCount()
    {
        TxtPlaylistCount.Text = _playlist.Count > 0 ? $"{_filteredItems.Count} tracks" : string.Empty;
    }

    // ─── Playback ─────────────────────────────────────────────────────────────

    private void ReproducirIndice(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;

        // Update playing indicator on old item
        if (_currentIndex >= 0 && _currentIndex < _allItems.Count)
            _allItems[_currentIndex].IsPlaying = false;

        _currentIndex = index;

        if (index < _allItems.Count)
            _allItems[index].IsPlaying = true;

        // Scroll to selected in filtered list
        var vm = _allItems.Count > index ? _allItems[index] : null;
        if (vm != null && _filteredItems.Contains(vm))
        {
            ListaReproduccion.SelectedItem = vm;
            ListaReproduccion.ScrollIntoView(vm);
        }

        var track = _playlist[index];
        bool ramMode = _prefs.Settings.RamMode;

        // Check if we have a preloaded gapless stream for this track
        int preloaded = 0;
        if (_pendingNextFile == track.FilePath)
        {
            preloaded = _audio.GetPreloadedStream();
            _pendingNextFile = null;
        }

        _audio.Reproducir(
            track.FilePath,
            ramMode,
            track.CueStartSeconds,
            track.CueEndSeconds,
            preloaded);

        _audio.Volumen = SliderVolumen.Value;
        _timer.Start();

        // ── Update UI metadata ────────────────────────────────────────────
        UpdatePlayerUI(track);

        // ── History ───────────────────────────────────────────────────────
        _history.RecordPlay(track.FilePath, track.DisplayTitle);

        // ── Media keys now playing ────────────────────────────────────────
        _mediaKeys.UpdateNowPlaying(track);

        // ── Play button icon ──────────────────────────────────────────────
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

        // Cover art (async)
        UpdateCoverArt(track.FilePath);
    }

    private void UpdateCoverArt(string filePath)
    {
        Bitmap? bmp = _coverArt.GetCover(filePath);
        if (bmp != null)
        {
            ImgCoverArt.Source  = bmp;
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
        TxtTitle.Text  = "No track selected";
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

    // ─── Navigation ───────────────────────────────────────────────────────────

    private void NextTrack()
    {
        if (_playlist.Count == 0) return;

        if (_repeatMode == RepeatMode.One)
        {
            ReproducirIndice(_currentIndex);
            return;
        }

        if (_shuffleEnabled)
        {
            _shufflePos = (_shufflePos + 1) % _shuffleOrder.Length;
            ReproducirIndice(_shuffleOrder[_shufflePos]);
        }
        else if (_currentIndex + 1 < _playlist.Count)
        {
            ReproducirIndice(_currentIndex + 1);
        }
        else if (_repeatMode == RepeatMode.All)
        {
            ReproducirIndice(0);
        }
        else
        {
            _timer.Stop();
            BtnReproducir.Content = "▶";
        }
    }

    private void PrevTrack()
    {
        if (_audio.PosicionSegundos > 3)
        {
            _audio.PosicionSegundos = 0;
            return;
        }
        if (_shuffleEnabled && _shufflePos > 0)
        {
            _shufflePos--;
            ReproducirIndice(_shuffleOrder[_shufflePos]);
        }
        else if (_currentIndex > 0)
        {
            ReproducirIndice(_currentIndex - 1);
        }
        else
        {
            _audio.PosicionSegundos = 0;
        }
    }

    private void BuildShuffleOrder()
    {
        var order = Enumerable.Range(0, _playlist.Count).ToList();
        var rng = new Random();
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        // Put current index first if playing
        if (_currentIndex >= 0)
        {
            order.Remove(_currentIndex);
            order.Insert(0, _currentIndex);
        }
        _shuffleOrder = order.ToArray();
        _shufflePos = 0;
    }

    // ─── Event handlers – playback ────────────────────────────────────────────

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
        // Fire gapless preload on a background thread to not block the audio callback
        if (!_prefs.Settings.GaplessEnabled) return;
        int nextIdx = GetNextIndex();
        if (nextIdx < 0) return;

        var nextTrack = _playlist[nextIdx];
        _pendingNextFile = nextTrack.FilePath;
        _audio.PrecargarStream(nextTrack.FilePath, _prefs.Settings.RamMode);
    }

    private int GetNextIndex()
    {
        if (_playlist.Count == 0) return -1;
        if (_shuffleEnabled && _shuffleOrder.Length > 0)
        {
            int nextShufflePos = (_shufflePos + 1) % _shuffleOrder.Length;
            return _shuffleOrder[nextShufflePos];
        }
        if (_currentIndex + 1 < _playlist.Count) return _currentIndex + 1;
        if (_repeatMode == RepeatMode.All) return 0;
        return -1;
    }

    // ─── Transport button handlers ────────────────────────────────────────────

    private void BtnReproducir_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex < 0 && _playlist.Count > 0)
        {
            ReproducirIndice(0);
            return;
        }
        _audio.AlternarPausa();
        BtnReproducir.Content = _audio.EstaReproduciendo ? "⏸" : "▶";
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
        _shuffleEnabled = !_shuffleEnabled;
        if (_shuffleEnabled) BuildShuffleOrder();
        UpdateShuffleButton();
        _prefs.Set(s => s.IsShuffleEnabled = _shuffleEnabled);
    }

    private void BtnRepeat_Click(object? sender, RoutedEventArgs e)
    {
        _repeatMode = _repeatMode switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _              => RepeatMode.Off
        };
        UpdateRepeatButton();
        _prefs.Set(s => s.RepeatMode = _repeatMode.ToString());
    }

    private void UpdateShuffleButton()
    {
        BtnShuffle.Classes.Clear();
        BtnShuffle.Classes.Add(_shuffleEnabled ? "toggle-active" : "toggle-inactive");
    }

    private void UpdateRepeatButton()
    {
        BtnRepeat.Classes.Clear();
        BtnRepeat.Content = _repeatMode switch
        {
            RepeatMode.One => "↺¹",
            RepeatMode.All => "↺",
            _              => "↺"
        };
        BtnRepeat.Classes.Add(_repeatMode != RepeatMode.Off ? "toggle-active" : "toggle-inactive");
        if (_repeatMode != RepeatMode.Off)
            ToolTip.SetTip(BtnRepeat, _repeatMode == RepeatMode.One ? "Repeat: One" : "Repeat: All");
        else
            ToolTip.SetTip(BtnRepeat, "Repeat (R)");
    }

    // ─── Favorite ─────────────────────────────────────────────────────────────

    private void BtnFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex < 0) return;
        ToggleFavorite(_currentIndex);
    }

    private void FavButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PlaylistItemViewModel vm)
        {
            int idx = _playlist.IndexOf(vm.Track);
            if (idx >= 0) ToggleFavorite(idx);
        }
    }

    private void ToggleFavorite(int idx)
    {
        var track = _playlist[idx];
        track.IsFavorite = !track.IsFavorite;

        if (track.IsFavorite)
            _prefs.Settings.Favorites.Add(track.FilePath);
        else
            _prefs.Settings.Favorites.Remove(track.FilePath);

        _prefs.Save();

        // Update VMs
        foreach (var vm in _allItems.Where(v => v.Track == track))
            vm.RefreshFav();

        // Update main star button
        if (idx == _currentIndex)
            BtnFavorite.Content = track.IsFavorite ? "★" : "☆";
    }

    // ─── Settings ────────────────────────────────────────────────────────────

    private async void BtnSettings_Click(object? sender, RoutedEventArgs e)
    {
        var devices = _audio.ObtenerDispositivos();
        var win = new SettingsWindow(_prefs.Settings, devices);
        await win.ShowDialog(this);

        if (win.Saved)
        {
            // Apply changed settings
            _audio.CambiarDispositivo(_prefs.Settings.LastDeviceIndex);

            if (_prefs.Settings.HttpApiEnabled && !_httpRemote.IsRunning)
                _httpRemote.Start(_prefs.Settings.HttpApiPort);
            else if (!_prefs.Settings.HttpApiEnabled && _httpRemote.IsRunning)
                _httpRemote.Stop();

            if (_prefs.Settings.SpectrumEnabled)
                SpectrumViz.Resume();
            else
                SpectrumViz.Stop();
                
            // Update CD button immediately based on settings change if a CD is ready
            bool isCdSupported = (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) 
                                 && RuntimeInformation.OSArchitecture == Architecture.X64;
            if (isCdSupported)
            {
                if (!_prefs.Settings.CdEnabled)
                {
                    BtnCargarCd.IsVisible = false;
                }
                else
                {
                    try { BtnCargarCd.IsVisible = BassCd.IsReady(0); } catch { }
                }
            }
        }
    }

    // ─── Seekbar ─────────────────────────────────────────────────────────────

    private void SliderProgreso_PointerPressed(object? sender, PointerPressedEventArgs e)
        => _isDraggingSlider = true;

    private void SliderProgreso_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDraggingSlider = false;
        if (_currentIndex >= 0)
            _audio.PosicionSegundos = SliderProgreso.Value;
    }

    private void SliderProgreso_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e) { }

    // ─── Volume ───────────────────────────────────────────────────────────────

    private void SliderVolumen_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_audio != null)
        {
            _audio.Volumen = SliderVolumen.Value;
            TxtVolumePercent.Text = $"{(int)(SliderVolumen.Value * 100)}%";
        }
    }

    // ─── Search ───────────────────────────────────────────────────────────────

    private void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchText = TxtSearch.Text ?? string.Empty;
        ApplySearchFilter(_searchText);
        UpdatePlaylistCount();
    }

    // ─── Playlist selection ───────────────────────────────────────────────────

    private void ListaReproduccion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ListaReproduccion.SelectedItem is PlaylistItemViewModel vm)
        {
            int idx = _playlist.IndexOf(vm.Track);
            if (idx >= 0 && idx != _currentIndex)
                ReproducirIndice(idx);
        }
    }

    // ─── Keyboard shortcuts ───────────────────────────────────────────────────

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
            case Key.Right:
                e.Handled = true;
                NextTrack();
                break;
            case Key.Left:
                e.Handled = true;
                PrevTrack();
                break;
            case Key.Up:
                e.Handled = true;
                SliderVolumen.Value = Math.Min(1.0, SliderVolumen.Value + 0.1);
                break;
            case Key.Down:
                e.Handled = true;
                SliderVolumen.Value = Math.Max(0.0, SliderVolumen.Value - 0.1);
                break;
            case Key.M:
                BtnMute_Click(null, new RoutedEventArgs());
                break;
            case Key.S:
                BtnShuffle_Click(null, new RoutedEventArgs());
                break;
            case Key.R:
                BtnRepeat_Click(null, new RoutedEventArgs());
                break;
            case Key.F:
                BtnFavorite_Click(null, new RoutedEventArgs());
                break;
            case Key.O when e.KeyModifiers == KeyModifiers.Control:
                e.Handled = true;
                BtnCargarArchivo_Click(null, new RoutedEventArgs());
                break;
            case Key.O when e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Shift):
                e.Handled = true;
                BtnCargarCarpeta_Click(null, new RoutedEventArgs());
                break;
        }
    }

    // ─── Mouse wheel = volume ─────────────────────────────────────────────────

    private void Window_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ListaReproduccion.IsPointerOver) return; // Let playlist scroll naturally
        double delta = e.Delta.Y * 0.05;
        SliderVolumen.Value = Math.Clamp(SliderVolumen.Value + delta, 0, 1);
        e.Handled = true;
    }

    // ─── Menu handlers ────────────────────────────────────────────────────────

    private async void AcercaDe_Click(object? sender, EventArgs e)
    {
        var w = new AboutWindow();
        await w.ShowDialog(this);
    }

    private void Salir_Click(object? sender, EventArgs e) => Close();

    // ─── Window close ─────────────────────────────────────────────────────────

    protected override void OnClosed(EventArgs e)
    {
        // Save preferences
        _prefs.Settings.Volume         = SliderVolumen.Value;
        _prefs.Settings.IsMuted        = _audio.IsMuted;
        _prefs.Settings.WindowWidth    = Width;
        _prefs.Settings.WindowHeight   = Height;
        _prefs.Settings.WindowLeft     = Position.X;
        _prefs.Settings.WindowTop      = Position.Y;
        _prefs.Settings.IsShuffleEnabled = _shuffleEnabled;
        _prefs.Settings.RepeatMode     = _repeatMode.ToString();
        _prefs.Save();
        _history.Save();

        // Cleanup
        _timer.Stop();
        _cdTimer?.Stop();
        SpectrumViz.Stop();
        _httpRemote.Dispose();
        _mediaKeys.Dispose();
        _coverArt.ClearCache();
        _audio.Liberar();

        base.OnClosed(e);
    }
}