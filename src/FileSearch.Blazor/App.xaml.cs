// MAUI アプリケーションのエントリ。MainPage（Blazor WebView）をルートに設定する。
using FileSearch.Messages;

namespace FileSearch.Blazor;

/// <summary>
/// MAUI アプリケーション。起動時に MainPage を設定する。
/// </summary>
public partial class App : Application
{
	private bool _initialWindowLayoutApplied;

	/// <summary>XAML を初期化し、ルートに <see cref="MainPage"/> を設定する。</summary>
	public App()
	{
		InitializeComponent();
		MainPage = new MainPage();
	}

	/// <summary>ウィンドウ作成時にタイトルと初期サイズを設定する。</summary>
	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = base.CreateWindow(activationState);
		window.Title = UserMessages.AppTitle;
		window.MinimumWidth = AppWindowLayout.MinimumWidth;
		window.MinimumHeight = AppWindowLayout.MinimumHeight;
		window.Width = AppWindowLayout.FallbackWidth;
		window.Height = AppWindowLayout.FallbackHeight;

		window.HandlerChanged += (_, _) =>
		{
			if (_initialWindowLayoutApplied || window.Handler is null)
				return;
			if (AppWindowLayout.TryApplyPlatformLayout(window))
				_initialWindowLayoutApplied = true;
		};

		return window;
	}
}
