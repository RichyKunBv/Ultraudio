using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ultraudio;

public partial class HistoryWindow : Window
{
    public HistoryWindow()
    {
        InitializeComponent();
        TxtVersion.Text = "Versión " + AppInfo.VersionDisplay;
    }

    private void BtnCerrar_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
