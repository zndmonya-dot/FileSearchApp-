using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FullTextSearch.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace FullTextSearch.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        await _viewModel.InitializeCommand.ExecuteAsync(null);
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await _viewModel.SearchCommand.ExecuteAsync(null);
        }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            // 設定が変更された場合、再読み込み
            _ = _viewModel.InitializeCommand.ExecuteAsync(null);
        }
    }

    private void FileTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FileTreeNode node)
        {
            _viewModel.SelectFileNode(node);
        }
    }
}
