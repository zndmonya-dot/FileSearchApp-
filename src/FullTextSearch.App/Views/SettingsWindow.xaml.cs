using System.Windows;
using FullTextSearch.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FullTextSearch.App.Views;

/// <summary>
/// SettingsWindow.xaml の相互作用ロジック
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();

        DataContext = App.Services.GetRequiredService<SettingsViewModel>();
    }
}

