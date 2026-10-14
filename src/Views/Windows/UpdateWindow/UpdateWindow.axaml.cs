using Ultraudio.Core;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ultraudio.Services;

namespace Ultraudio.Views.Windows;

public partial class UpdateWindow : Window
{
    private string? _latestVersion;
    private string? _downloadUrl;
    private CancellationTokenSource? _downloadCancellation;

    public UpdateWindow()
    {
        InitializeComponent();
        CheckForUpdatesAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _downloadCancellation?.Cancel();
        base.OnClosed(e);
    }

    private async void CheckForUpdatesAsync()
    {
        var version = UpdateService.GetCurrentVersion();
        var (status, latestVersion, downloadUrl) = await UpdateService.CheckForUpdatesAsync(version);
        _latestVersion = latestVersion;
        _downloadUrl = downloadUrl;

        Dispatcher.UIThread.Post(() =>
        {
            switch (status)
            {
                case UpdateStatus.Outdated:
                    TxtStatus.Text = $"Tienes una versión desactualizada. Última versión: {latestVersion}";
                    TxtStatus.Foreground = Brushes.Yellow;
                    BtnLatestRelease.Content = "Descargar actualización";
                    BtnLatestRelease.IsVisible = true;
                    break;
                case UpdateStatus.MicrosoftStoreUpdateAvailable:
                    TxtStatus.Text = "Hay una actualización disponible en Microsoft Store.";
                    TxtStatus.Foreground = Brushes.Yellow;
                    BtnLatestRelease.Content = "Actualizar desde Microsoft Store";
                    BtnLatestRelease.IsVisible = true;
                    break;
                case UpdateStatus.UpToDate:
                    TxtStatus.Text = "Tienes la última versión.";
                    TxtStatus.Foreground = Brushes.LightGreen;
                    break;
                case UpdateStatus.AssetUnavailable:
                    TxtStatus.Text = $"La versión {latestVersion} está publicada, pero no hay un instalador para esta plataforma.";
                    TxtStatus.Foreground = Brushes.IndianRed;
                    break;
                case UpdateStatus.MicrosoftStoreUpToDate:
                    TxtStatus.Text = "Tienes la última versión de Microsoft Store.";
                    TxtStatus.Foreground = Brushes.LightGreen;
                    break;
                case UpdateStatus.Newer:
                    TxtStatus.Text = $"Estás usando una versión de desarrollo (Local: {version}, Remota: {latestVersion}).";
                    TxtStatus.Foreground = Brushes.LightBlue;
                    break;
                case UpdateStatus.Error:
                default:
                    TxtStatus.Text = "No se pudo verificar la actualización.";
                    TxtStatus.Foreground = Brushes.IndianRed;
                    break;
            }
        });
    }

    private async void BtnLatestRelease_Click(object? sender, RoutedEventArgs e)
    {
        if (UpdateService.IsMicrosoftStoreInstallation)
        {
            await UpdateFromMicrosoftStoreAsync();
            return;
        }

        if (string.IsNullOrWhiteSpace(_downloadUrl) || string.IsNullOrWhiteSpace(_latestVersion))
        {
            TxtStatus.Text = "No hay una descarga válida disponible para esta plataforma.";
            TxtStatus.Foreground = Brushes.IndianRed;
            return;
        }

        _downloadCancellation = new CancellationTokenSource();
        BtnLatestRelease.IsEnabled = false;
        BtnCancelDownload.IsVisible = true;
        ProgressDownload.IsVisible = true;
        ProgressDownload.IsIndeterminate = true;
        TxtProgress.IsVisible = true;
        TxtStatus.Text = $"Preparando la descarga de v{_latestVersion}...";
        TxtStatus.Foreground = Brushes.White;

        string? installerPath = null;
        try
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "UltraudioUpdates");
            Directory.CreateDirectory(tempDir);
            installerPath = Path.Combine(tempDir, $"Ultraudio-{_latestVersion}-{UpdateService.GetCurrentAssetName()}");

            var progress = new Progress<(long bytesRead, long? totalBytes)>(value =>
            {
                double mbRead = value.bytesRead / (1024.0 * 1024.0);
                if (value.totalBytes is > 0)
                {
                    double percent = value.bytesRead * 100.0 / value.totalBytes.Value;
                    double mbTotal = value.totalBytes.Value / (1024.0 * 1024.0);
                    ProgressDownload.IsIndeterminate = false;
                    ProgressDownload.Value = percent;
                    TxtProgress.Text = $"{percent:F0}% ({mbRead:F1}/{mbTotal:F1} MB)";
                }
                else
                {
                    TxtProgress.Text = $"{mbRead:F1} MB descargados";
                }

                TxtStatus.Text = $"Descargando v{_latestVersion}...";
            });

            await UpdateService.DownloadFileAsync(
                _downloadUrl,
                installerPath,
                progress,
                _downloadCancellation.Token);

            TxtStatus.Text = "Descarga completa. Iniciando el instalador...";
            TxtProgress.Text = "Sigue las instrucciones del instalador para completar la actualización.";
            UpdateService.LaunchInstaller(installerPath);
            TxtStatus.Text = RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                ? "Se abrió el disco de instalación. Arrastra Ultraudio.app a Aplicaciones para completar la actualización."
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    ? "Se inició la nueva AppImage. No reemplaza automáticamente el archivo anterior."
                    : "Instalador iniciado. Sigue sus instrucciones para actualizar.";
            BtnCancelDownload.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            TxtStatus.Text = "Descarga cancelada.";
            TxtStatus.Foreground = Brushes.Yellow;
            if (installerPath is not null && File.Exists(installerPath))
            {
                try
                {
                    File.Delete(installerPath);
                }
                catch (Exception cleanupException)
                {
                    Log.Warn("Update", $"Could not remove the partial update download: {cleanupException.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Update", "Could not download or start the update installer", ex);
            TxtStatus.Text = "No se pudo descargar o iniciar el instalador.";
            TxtStatus.Foreground = Brushes.IndianRed;
        }
        finally
        {
            BtnLatestRelease.IsEnabled = true;
            BtnCancelDownload.IsVisible = false;
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
        }
    }

    private async Task UpdateFromMicrosoftStoreAsync()
    {
        BtnLatestRelease.IsEnabled = false;
        try
        {
            var windowHandle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            var result = await UpdateService.InstallMicrosoftStoreUpdatesAsync(windowHandle);
            TxtStatus.Text = result switch
            {
                StoreUpdateInstallStatus.Completed => "Microsoft Store descargó e instaló la actualización. Es posible que debas reiniciar Ultraudio.",
                StoreUpdateInstallStatus.NoUpdates => "Microsoft Store no encontró actualizaciones pendientes.",
                StoreUpdateInstallStatus.Canceled => "Se canceló la actualización de Microsoft Store.",
                _ => "Microsoft Store no pudo completar la actualización."
            };
            TxtStatus.Foreground = result == StoreUpdateInstallStatus.Completed ? Brushes.LightGreen : Brushes.Yellow;
        }
        catch (Exception ex)
        {
            Log.Error("Update", "Could not install the Microsoft Store update", ex);
            TxtStatus.Text = "No se pudo iniciar la actualización de Microsoft Store.";
            TxtStatus.Foreground = Brushes.IndianRed;
        }
        finally
        {
            BtnLatestRelease.IsEnabled = true;
        }
    }

    private void BtnCancelDownload_Click(object? sender, RoutedEventArgs e)
    {
        _downloadCancellation?.Cancel();
        TxtStatus.Text = "Cancelando descarga...";
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
