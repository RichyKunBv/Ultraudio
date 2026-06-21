using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TagLib;

namespace Ultraudio;

public partial class MainWindow : Window
{
    private AudioEngine _audio;
    private List<string> _playlist = new();
    private int _currentIndex = -1;
    private DispatcherTimer _timer;
    private bool _isDraggingSlider = false;

    public MainWindow()
    {
        InitializeComponent();
        
        _audio = new AudioEngine();
        _audio.InicializarDispositivo();
        _audio.TrackEnded += Audio_TrackEnded;

        // Cargar dispositivos
        var devices = _audio.ObtenerDispositivos();
        ComboDispositivos.ItemsSource = devices;
        var defaultDevice = devices.FirstOrDefault(d => d.IsDefault) ?? devices.FirstOrDefault();
        if (defaultDevice != null)
        {
            ComboDispositivos.SelectedItem = defaultDevice;
        }

        // Configurar Timer
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _timer.Tick += Timer_Tick;
    }

    private void ComboDispositivos_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ComboDispositivos.SelectedItem is DeviceModel device)
        {
            _audio.CambiarDispositivo(device.Index);
        }
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (!_isDraggingSlider && _currentIndex >= 0)
        {
            double pos = _audio.PosicionSegundos;
            double len = _audio.DuracionSegundos;

            SliderProgreso.Maximum = len > 0 ? len : 100;
            SliderProgreso.Value = pos;

            TxtTiempoActual.Text = TimeSpan.FromSeconds(pos).ToString(@"mm\:ss");
            TxtTiempoTotal.Text = TimeSpan.FromSeconds(len).ToString(@"mm\:ss");
        }
    }

    private async void BtnCargarArchivo_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var archivos = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecciona tu música pura",
            AllowMultiple = true,
            FileTypeFilter = new[] 
            { 
                new FilePickerFileType("Audio Lossless") { Patterns = new[] { "*.flac", "*.wav", "*.aiff", "*.aif", "*.dsf", "*.dff" } } 
            }
        });

        if (archivos.Count > 0)
        {
            _playlist.Clear();
            var names = new List<string>();
            foreach (var a in archivos)
            {
                _playlist.Add(a.Path.LocalPath);
                names.Add(a.Name);
            }
            ListaReproduccion.ItemsSource = names;
            ReproducirIndice(0);
        }
    }

    private async void BtnCargarCarpeta_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var carpetas = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Selecciona carpeta de música",
            AllowMultiple = false
        });

        if (carpetas.Count > 0)
        {
            string ruta = carpetas[0].Path.LocalPath;
            var archivos = Directory.GetFiles(ruta, "*.*", SearchOption.TopDirectoryOnly)
                .Where(s => s.EndsWith(".flac", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".aif", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".dsf", StringComparison.OrdinalIgnoreCase) ||
                            s.EndsWith(".dff", StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s)
                .ToList();

            if (archivos.Count > 0)
            {
                _playlist = archivos;
                ListaReproduccion.ItemsSource = archivos.Select(Path.GetFileName).ToList();
                ReproducirIndice(0);
            }
        }
    }

    private void ListaReproduccion_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ListaReproduccion.SelectedIndex >= 0 && ListaReproduccion.SelectedIndex != _currentIndex)
        {
            ReproducirIndice(ListaReproduccion.SelectedIndex);
        }
    }

    private void ReproducirIndice(int index)
    {
        if (index >= 0 && index < _playlist.Count)
        {
            _currentIndex = index;
            ListaReproduccion.SelectedIndex = index;
            
            string ruta = _playlist[index];
            bool mem = BtnRamMode.IsChecked ?? false;
            
            _audio.Reproducir(ruta, mem);
            _audio.Volumen = SliderVolumen.Value; // Reaplicar volumen
            _timer.Start();

            TxtRuta.Text = Path.GetFileName(ruta);
            BorderTechInfo.IsVisible = true;
            
            try
            {
                using var file = TagLib.File.Create(ruta);
                var prop = file.Properties;
                
                int bits = prop.BitsPerSample;
                double sampleRateKhz = prop.AudioSampleRate / 1000.0;
                int bitrate = prop.AudioBitrate;
                int channels = prop.AudioChannels;
                
                string format = Path.GetExtension(ruta).ToUpper().Replace(".", "");
                TxtTechInfo.Text = $"{format} • {bits}-bit • {sampleRateKhz:0.#} kHz • {bitrate} kbps • {channels} ch";
            }
            catch (Exception)
            {
                TxtTechInfo.Text = Path.GetExtension(ruta).ToUpper().Replace(".", "");
            }
        }
    }

    private void Audio_TrackEnded(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_currentIndex + 1 < _playlist.Count)
            {
                ReproducirIndice(_currentIndex + 1);
            }
            else
            {
                _timer.Stop();
            }
        });
    }

    private void BtnReproducir_Click(object? sender, RoutedEventArgs e)
    {
        _audio.AlternarPausa();
    }

    private void BtnAnterior_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex > 0)
            ReproducirIndice(_currentIndex - 1);
        else if (_currentIndex == 0)
            _audio.PosicionSegundos = 0; // Reiniciar si es el primero
    }

    private void BtnSiguiente_Click(object? sender, RoutedEventArgs e)
    {
        if (_currentIndex + 1 < _playlist.Count)
            ReproducirIndice(_currentIndex + 1);
    }

    private void SliderProgreso_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _isDraggingSlider = true;
    }

    private void SliderProgreso_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        _isDraggingSlider = false;
        if (_currentIndex >= 0)
        {
            _audio.PosicionSegundos = SliderProgreso.Value;
        }
    }

    private void SliderProgreso_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        // Actualización manual ya manejada en Timer_Tick o al soltar
    }

    private void SliderVolumen_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_audio != null)
        {
            _audio.Volumen = SliderVolumen.Value;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _audio.Liberar();
        base.OnClosed(e);
    }

    private void AcercaDe_Click(object? sender, EventArgs e)
    {
        TxtRuta.Text = "Ultraudio v" + AppInfo.VersionDisplay;
    }

    private void Salir_Click(object? sender, EventArgs e)
    {
        Close();
    }
}