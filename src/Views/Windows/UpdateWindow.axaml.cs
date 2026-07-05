using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Runtime.InteropServices;
using Ultraudio.Services;

namespace Ultraudio.Views.Windows;

public partial class UpdateWindow : Window
{
    public UpdateWindow()
    {
        InitializeComponent();
        CheckForUpdatesAsync();
    }

    private async void CheckForUpdatesAsync()
    {
        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.7.0";
        var (status, latestVersion) = await UpdateService.CheckForUpdatesAsync(version);

        Dispatcher.UIThread.Post(() =>
        {
            switch (status)
            {
                case UpdateStatus.Outdated:
                    TxtStatus.Text = $"Tienes una versión desactualizada. Última versión: {latestVersion}";
                    TxtStatus.Foreground = Brushes.Yellow;
                    BtnLatestRelease.IsVisible = true;
                    break;
                case UpdateStatus.UpToDate:
                    TxtStatus.Text = "Tienes la última versión.";
                    TxtStatus.Foreground = Brushes.LightGreen;
                    break;
                case UpdateStatus.Newer:
                    TxtStatus.Text = $"Tienes una versión más reciente (Local: {version}, Remota: {latestVersion}).";
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

    private void BtnLatestRelease_Click(object? sender, RoutedEventArgs e)
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
            Console.WriteLine($"Could not open update URL: {ex.Message}");
        }
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
