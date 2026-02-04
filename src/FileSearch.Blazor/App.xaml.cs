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

	/// <summary>ウィンドウ作成時にタイトルを設定する。Windows では ContentPage.Title がタイトルバーに反映されないためここで設定する。</summary>
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = base.CreateWindow(activationState);
		window.Title = "全文検索システム";
		return window;
	}
}
