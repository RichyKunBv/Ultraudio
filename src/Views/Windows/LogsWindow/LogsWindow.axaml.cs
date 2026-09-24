using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Ultraudio.Core;

namespace Ultraudio.Views.Windows;

public partial class LogsWindow : Window
{
    private string[] _allLines = Array.Empty<string>();

    public LogsWindow()
    {
        InitializeComponent();
        LoadLogs();
        Log.MessageLogged += OnMessageLogged;
    }

    private void LoadLogs()
    {
        _allLines = Log.GetRecentLogs();
        ApplyFilter();
    }

    private void OnMessageLogged(string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var list = _allLines.ToList();
            list.Add(line);
            _allLines = list.ToArray();
            ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        string filter = TxtFiltro.Text?.Trim() ?? string.Empty;
        var matching = string.IsNullOrEmpty(filter)
            ? _allLines
            : _allLines.Where(l => l.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        TxtLogs.Text = string.Join(Environment.NewLine, matching);
        TxtLogStats.Text = $"{matching.Length} de {_allLines.Length} líneas";
    }

    private void TxtFiltro_TextChanged(object? sender, TextChangedEventArgs e)
    {
        ApplyFilter();
    }

    private async void BtnCopiar_Click(object? sender, RoutedEventArgs e)
    {
        if (Clipboard != null && !string.IsNullOrEmpty(TxtLogs.Text))
        {
            await Clipboard.SetTextAsync(TxtLogs.Text);
            TxtLogStats.Text = "¡Copiado al portapapeles!";
        }
    }

    private void BtnAbrirCarpeta_Click(object? sender, RoutedEventArgs e)
    {
        string dir = Log.LogDirectory;
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
        {
            OpenFolderInFileManager(dir);
        }
    }

    private void BtnLimpiar_Click(object? sender, RoutedEventArgs e)
    {
        Log.Clear();
        _allLines = Array.Empty<string>();
        ApplyFilter();
    }

    private void BtnCerrar_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void BtnReportGit_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string systemInfo = $"**OS:** {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\n**App Version:** {AppInfo.VersionDisplay}\n**.NET:** {Environment.Version}";
            
            // Get last 20 lines or last errors
            var recentErrors = _allLines.Where(l => l.Contains("[ERROR]") || l.Contains("[WARN]")).TakeLast(10).ToList();
            string errorSection = recentErrors.Count > 0
                ? string.Join("\n", recentErrors)
                : string.Join("\n", _allLines.TakeLast(15));

            string issueBody = $"### Descripción del Problema\n<!-- Describe qué sucedió y los pasos para reproducir -->\n\n### Información del Sistema\n{systemInfo}\n\n### Extracto de Logs\n```text\n{errorSection}\n```";
            
            string url = $"https://github.com/RichyKunBv/Ultraudio/issues/new?title={WebUtility.UrlEncode("[Bug] ")}&body={WebUtility.UrlEncode(issueBody)}";
            
            OpenUrl(url);
        }
        catch (Exception ex)
        {
            Log.Error("LogsWindow", "No se pudo abrir GitHub Issues", ex);
        }
    }

    private void BtnReportEmail_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            string systemInfo = $"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})\nApp: {AppInfo.VersionDisplay}\n.NET: {Environment.Version}";
            var recent = string.Join("\n", _allLines.TakeLast(20));

            string body = $"Describe el problema aquí:\n\n---\nInformación del Sistema:\n{systemInfo}\n\nLogs recientes:\n{recent}";
            string mailto = $"mailto:?subject={WebUtility.UrlEncode($"Reporte de Error Ultraudio {AppInfo.VersionDisplay}")}&body={WebUtility.UrlEncode(body)}";

            OpenUrl(mailto);
        }
        catch (Exception ex)
        {
            Log.Error("LogsWindow", "No se pudo abrir cliente de correo", ex);
        }
    }

    private static void OpenUrl(string url)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{url.Replace("&", "^&")}\"") { CreateNoWindow = true });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("xdg-open", url);
        }
    }

    private static void OpenFolderInFileManager(string path)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Process.Start("explorer.exe", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", path);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", path);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("LogsWindow", $"Failed to open folder: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Log.MessageLogged -= OnMessageLogged;
        base.OnClosed(e);
    }
}
