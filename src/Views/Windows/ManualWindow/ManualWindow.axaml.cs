using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Ultraudio.Views.Windows;

public partial class ManualWindow : Window
{
    public ManualWindow()
    {
        InitializeComponent();
    }

    private void BtnClose_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
