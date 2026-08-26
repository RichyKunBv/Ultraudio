using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Collections.Generic;

namespace Ultraudio.Views.Windows;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        TxtVersion.Text = "Versión " + AppInfo.VersionDisplay;

        // Lista de versiones (más reciente primero)
        var versions = new List<string>
        {
            "V1.0.0 - Lanzamiento oficial v1.0.0 (Release). Integración nativa de teclas multimedia en macOS, Windows y Linux (MPRIS2/playerctl). Nuevas ventanas de Manual de Usuario e Historial de Versiones. Reproducción en RAM, Gapless, CUE sheets, Audio CD, API HTTP remota y visualizador FFT.",
            "V0.9.0 - Pulido masivo",
            "V0.8.0 - Soporte para M3U8.",
            "V0.7.0 - La aplicación detecta si existe una actualización disponible para descargarla automáticamente.",
            "V0.6.0 - Solución de errores de la interfaz y se agregó soporte de CDs.",
            "V0.5.0 - Interfaz de usuario rediseñada.",
            "V0.4.0 - Reorganización del código (Nunca vio la luz, estaba llena de bugs).",
            "V0.3.0 - Automatizaciones en .sh y .bat para las builds. También se agregaron las librerías BASS.",
            "V0.2.0 - Pulido del código base.",
            "V0.1.0 - Creación del proyecto y código base."
        };

        LstVersions.ItemsSource = versions;
    }

    private void BtnCerrar_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}