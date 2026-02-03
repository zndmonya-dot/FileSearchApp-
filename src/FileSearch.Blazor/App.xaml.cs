// MAUI アプリケーションのエントリ。MainPage（Blazor WebView）をルートに設定する。
namespace FileSearch.Blazor;

/// <summary>
/// MAUI アプリケーション。起動時に MainPage を設定する。
/// </summary>
public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		MainPage = new MainPage();
	}
}
