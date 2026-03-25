// MAUI アプリケーションのエントリ。MainPage（Blazor WebView）をルートに設定する。
using FileSearch.Messages;

namespace FileSearch.Blazor;

/// <summary>
/// MAUI アプリケーション。起動時に MainPage を設定する。
/// </summary>
public partial class App : Application
{
	/// <summary>XAML を初期化し、ルートに <see cref="MainPage"/> を設定する。</summary>
	public App()
	{
		InitializeComponent();
		MainPage = new MainPage();
	}

	/// <summary>ウィンドウ作成時にタイトルを設定する。Windows では ContentPage.Title がタイトルバーに反映されないためここで設定する。</summary>
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = base.CreateWindow(activationState);
		window.Title = UserMessages.AppTitle;
		return window;
	}
}
