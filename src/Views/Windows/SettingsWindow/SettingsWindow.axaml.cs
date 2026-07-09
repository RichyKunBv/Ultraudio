using Avalonia.Controls;
using Avalonia.Interactivity;
using System.Linq;
using Ultraudio.Core;
using Ultraudio.Models;
using Avalonia.Media;

namespace Ultraudio.Views.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly System.Collections.Generic.List<DeviceModel> _devices;

    public bool Saved { get; private set; } = false;

    // Required by XAML runtime loader (AVLN3001). Not used directly.
    public SettingsWindow() : this(new AppSettings(), new System.Collections.Generic.List<DeviceModel>()) { }

    public SettingsWindow(
        AppSettings settings,
        System.Collections.Generic.List<DeviceModel> devices)
    {
        InitializeComponent();
        _settings = settings;
        _devices = devices;
        PopulateForm();
    }

    private void PopulateForm()
    {
        // Devices
        ComboDevices.ItemsSource = _devices;
        var selected = _devices.FirstOrDefault(d => d.Index == _settings.LastDeviceIndex)
                    ?? _devices.FirstOrDefault(d => d.IsDefault)
                    ?? _devices.FirstOrDefault();
        if (selected != null)
            ComboDevices.SelectedItem = selected;

        // Toggles
        ToggleExclusive.IsChecked = _settings.ExclusiveMode;
        ToggleRamMode.IsChecked   = _settings.RamMode;
        ToggleGapless.IsChecked   = _settings.GaplessEnabled;
        ToggleSpectrum.IsChecked  = _settings.SpectrumEnabled;
        ToggleHttpApi.IsChecked   = _settings.HttpApiEnabled;
        ToggleCd.IsChecked        = _settings.CdEnabled;

        bool isCdSupported = UltraudioConstants.IsCdSupported;
        ToggleCd.IsEnabled = isCdSupported;
        if (!isCdSupported) ToggleCd.IsChecked = false;

        // Port
        NumPort.Value = _settings.HttpApiPort;
        UpdateApiUrlLabel();

        // Version
        TxtVersion.Text = AppInfo.AboutText;

        // Wire port change
        NumPort.ValueChanged += (_, _) => UpdateApiUrlLabel();
    }

    private void UpdateApiUrlLabel()
    {
        int port = (int)(NumPort.Value ?? 7654);
        TxtApiUrl.Text = $"http://127.0.0.1:{port}/status";
    }

    private void BtnSave_Click(object? sender, RoutedEventArgs e)
    {
        // Write back to settings
        if (ComboDevices.SelectedItem is DeviceModel dev)
        {
            _settings.LastDeviceIndex = dev.Index;
            _settings.LastDeviceName  = dev.Name;
        }

        _settings.ExclusiveMode  = ToggleExclusive.IsChecked ?? false;
        _settings.RamMode        = ToggleRamMode.IsChecked   ?? false;
        _settings.GaplessEnabled = ToggleGapless.IsChecked   ?? true;
        _settings.SpectrumEnabled = ToggleSpectrum.IsChecked ?? true;
        _settings.HttpApiEnabled = ToggleHttpApi.IsChecked   ?? false;
        _settings.CdEnabled      = ToggleCd.IsChecked        ?? false;
        _settings.HttpApiPort    = (int)(NumPort.Value ?? 7654);

        Saved = true;
        Close();
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Saved = false;
        Close();
    }

    private async void BtnOpenUpdateWindow_Click(object? sender, RoutedEventArgs e)
    {
        var win = new UpdateWindow();
        await win.ShowDialog(this);
    }

    private async void BtnOpenAboutWindow_Click(object? sender, RoutedEventArgs e)
    {
        var w = new AboutWindow();
        await w.ShowDialog(this);
    }

    private async void BtnOpenHistoryWindow_Click(object? sender, RoutedEventArgs e)
    {
        var w = new HistoryWindow();
        await w.ShowDialog(this);
    }
}
