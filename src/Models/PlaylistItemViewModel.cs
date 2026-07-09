using System.ComponentModel;
using System.Runtime.CompilerServices;
using Ultraudio.Core;
using Ultraudio.Models;

namespace Ultraudio;

/// <summary>
/// View model wrapper for <see cref="TrackModel"/> in the playlist ListBox.
/// Provides playing state indicator, display title with color, and favorite status.
/// </summary>
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
    public string TitleColor   => _isPlaying ? UltraudioConstants.AccentGreen : UltraudioConstants.TextPrimary;
    public string FavStar      => Track.IsFavorite ? "★" : "☆";
    public string FavColor     => Track.IsFavorite ? UltraudioConstants.FavoriteActive : UltraudioConstants.FavoriteMuted;

    public void RefreshFav() { OnPropertyChanged(nameof(FavStar)); OnPropertyChanged(nameof(FavColor)); }

    public PlaylistItemViewModel(TrackModel track) => Track = track;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
