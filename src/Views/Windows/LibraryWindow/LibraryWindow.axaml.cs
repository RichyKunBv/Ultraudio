using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Ultraudio.Core;
using Ultraudio.Models;
using Ultraudio.Services;

namespace Ultraudio.Views.Windows;

public partial class LibraryWindow : Window
{
    private readonly LibraryService _libraryService;
    private readonly Action<List<TrackModel>, bool>? _loadTracksAction;
    private List<TrackModel> _currentTracks = new();

    // Required by Avalonia XAML loader
    public LibraryWindow() : this(new LibraryService(new PreferencesManager()), null) { }

    public LibraryWindow(LibraryService libraryService, Action<List<TrackModel>, bool>? loadTracksAction)
    {
        InitializeComponent();
        _libraryService = libraryService;
        _loadTracksAction = loadTracksAction;

        bool hasPlaybackAction = _loadTracksAction != null;
        BtnPlayAll.IsEnabled = hasPlaybackAction;
        BtnQueueAll.IsEnabled = hasPlaybackAction;

        _libraryService.ScanStarted += OnScanStarted;
        _libraryService.ProgressChanged += OnProgressChanged;
        _libraryService.ScanCompleted += OnScanCompleted;

        if (!hasPlaybackAction)
        {
            TxtTrackCount.Text = "Abre esta biblioteca desde la ventana principal para cargar la cola.";
            TxtDbInfo.Text = "Modo vista: sin reproductor asociado";
        }

        RefreshFoldersList();
        _ = LoadTracksAsync();
    }

    private void RefreshFoldersList()
    {
        ListFolders.ItemsSource = null;
        ListFolders.ItemsSource = _libraryService.GetLibraryFolders().ToList();
    }

    private async Task LoadTracksAsync(string? query = null)
    {
        try
        {
            var tracks = await _libraryService.Database.GetAllTracksAsync(query);
            _currentTracks = tracks;

            Dispatcher.UIThread.Post(() =>
            {
                ListTracks.ItemsSource = tracks;
                TxtTrackCount.Text = $"{tracks.Count} canción{(tracks.Count == 1 ? "" : "es")}";
                TxtDbInfo.Text = $"SQLite DB: {System.IO.Path.GetFileName(_libraryService.Database.DatabasePath)} ({tracks.Count} indexadas)";
            });
        }
        catch (Exception ex)
        {
            Log.Error("LibraryWindow", "Error loading library tracks", ex);
        }
    }

    private void OnScanStarted()
    {
        Dispatcher.UIThread.Post(() =>
        {
            PanelScanning.IsVisible = true;
            ProgressScan.IsIndeterminate = true;
            TxtScanStatus.Text = "Buscando archivos...";
        });
    }

    private void OnProgressChanged(int current, int total)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PanelScanning.IsVisible = true;
            ProgressScan.IsIndeterminate = false;
            ProgressScan.Maximum = total > 0 ? total : 100;
            ProgressScan.Value = current;
            TxtScanStatus.Text = $"Indexando {current} de {total}...";
        });
    }

    private void OnScanCompleted(int totalIndexed)
    {
        Dispatcher.UIThread.Post(() =>
        {
            PanelScanning.IsVisible = false;
            TxtScanStatus.Text = "Completado";
            _ = LoadTracksAsync(TxtSearch.Text);
        });
    }

    private async void TxtSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        string query = TxtSearch.Text?.Trim() ?? string.Empty;
        await LoadTracksAsync(query);
    }

    private void BtnToggleFolders_Click(object? sender, RoutedEventArgs e)
    {
        PanelFolders.IsVisible = !PanelFolders.IsVisible;
        if (PanelFolders.IsVisible)
        {
            RefreshFoldersList();
        }
    }

    private async void BtnAddFolder_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Seleccionar carpeta para la biblioteca musical",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string selectedPath = folders[0].Path.LocalPath;
            await _libraryService.AddFolderAsync(selectedPath);
            RefreshFoldersList();
        }
    }

    private async void BtnRemoveFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (ListFolders.SelectedItem is string selectedFolder)
        {
            await _libraryService.RemoveFolderAsync(selectedFolder);
            RefreshFoldersList();
            await LoadTracksAsync(TxtSearch.Text);
        }
    }

    private async void BtnSync_Click(object? sender, RoutedEventArgs e)
    {
        await _libraryService.ScanLibraryAsync(forceRescan: false);
    }

    private void BtnPlayAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_loadTracksAction == null)
        {
            TxtTrackCount.Text = "Esta biblioteca se abrió en modo de vista. Abre desde la ventana principal para reproducir.";
            return;
        }

        if (_currentTracks.Count > 0)
        {
            _loadTracksAction.Invoke(_currentTracks.ToList(), false);
            Close();
        }
    }

    private void BtnQueueAll_Click(object? sender, RoutedEventArgs e)
    {
        if (_loadTracksAction == null)
        {
            TxtTrackCount.Text = "Esta biblioteca se abrió en modo de vista. Abre desde la ventana principal para añadir a la lista.";
            return;
        }

        if (_currentTracks.Count > 0)
        {
            _loadTracksAction.Invoke(_currentTracks.ToList(), true);
            TxtTrackCount.Text = $"¡{_currentTracks.Count} añadidas a la cola!";
        }
    }

    private void ListTracks_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_loadTracksAction == null)
        {
            TxtTrackCount.Text = "No hay reproductor asociado. Abre la biblioteca desde la ventana principal.";
            return;
        }

        if (ListTracks.SelectedItem is TrackModel selectedTrack)
        {
            // Play this track and queue rest
            int index = _currentTracks.IndexOf(selectedTrack);
            var queue = _currentTracks.Skip(index).Concat(_currentTracks.Take(index)).ToList();
            _loadTracksAction.Invoke(queue, false);
            Close();
        }
    }

    private void BtnCerrar_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        _libraryService.ScanStarted -= OnScanStarted;
        _libraryService.ProgressChanged -= OnProgressChanged;
        _libraryService.ScanCompleted -= OnScanCompleted;
        base.OnClosed(e);
    }
}
