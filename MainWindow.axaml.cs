using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using TagLib;

namespace Ultraudio;

public partial class MainWindow : Window
{
    // Instanciamos nuestro motor Hi-Fi aquí para controlarlo directamente
    private AudioEngine _audio;
    private string _rutaActual = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        
        _audio = new AudioEngine();
        _audio.InicializarDispositivo();

        // Enlazamos los eventos de los botones a sus métodos
        BtnCargar.Click += BtnCargar_Click;
        BtnReproducir.Click += BtnReproducir_Click;
        BtnDetener.Click += BtnDetener_Click;
    }

    private async void BtnCargar_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var archivos = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Selecciona tu música pura",
            AllowMultiple = false,
            FileTypeFilter = new[] 
            { 
                new FilePickerFileType("Audio FLAC") { Patterns = new[] { "*.flac" } } 
            }
        });

        if (archivos.Count >= 1)
        {
            _rutaActual = archivos[0].Path.LocalPath;
            
            // Leemos los metadatos técnicos con TagLib
            try
            {
                using var file = TagLib.File.Create(_rutaActual);
                var prop = file.Properties;
                
                // Extraemos los datos
                int bits = prop.BitsPerSample;
                double sampleRateKhz = prop.AudioSampleRate / 1000.0;
                int bitrate = prop.AudioBitrate;
                int channels = prop.AudioChannels;
                
                // Formateamos el texto y lo mostramos
                TxtRuta.Text = archivos[0].Name;
                TxtTechInfo.Text = $"FLAC • {bits}-bit • {sampleRateKhz:0.#} kHz • {bitrate} kbps • {channels} ch";
                BorderTechInfo.IsVisible = true; // Mostramos la etiqueta verde
                
                // Autoplay opcional: si quieres que suene en cuanto lo cargas, descomenta la siguiente línea
                // _audio.ReproducirFlac(_rutaActual);
            }
            catch (Exception ex)
            {
                TxtRuta.Text = "Error leyendo info: " + archivos[0].Name;
                BorderTechInfo.IsVisible = false;
            }
        }
    }

    private void BtnReproducir_Click(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_rutaActual))
        {
            _audio.ReproducirFlac(_rutaActual);
        }
    }

    private void BtnDetener_Click(object? sender, RoutedEventArgs e)
    {
        _audio.Detener();
    }
    
    // Al cerrar la ventana, soltamos el hardware de audio
    protected override void OnClosed(EventArgs e)
    {
        _audio.Liberar();
        base.OnClosed(e);
    }
}