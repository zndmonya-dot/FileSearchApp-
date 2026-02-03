// メインの ContentPage。Blazor WebView をホストし、表示時に WebView にフォーカスしてキー入力を有効にする。
namespace FileSearch.Blazor;

/// <summary>
/// アプリのメインページ。Blazor WebView で UI を表示する。
/// </summary>
public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	/// <summary>表示時に Blazor WebView にフォーカスし、キーボード入力を有効にする。</summary>
	protected override void OnAppearing()
	{
		base.OnAppearing();
		blazorWebView.Focus();
	}
}
