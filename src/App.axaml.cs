using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Ultraudio;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            try 
            {
                var audio = new AudioEngine();
                audio.InicializarDispositivo();
                desktop.Exit += (s, e) => audio.Liberar();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR CRÍTICO AL CARGAR BASS]: {ex.Message}");
                Console.WriteLine($"[DETALLES]: {ex.StackTrace}\n");
            }

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}