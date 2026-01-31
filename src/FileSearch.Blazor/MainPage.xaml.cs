namespace FileSearch.Blazor;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();
		// WebView にフォーカスを当ててキーボード入力を有効にする
		blazorWebView.Focus();
	}
}
