// Windows プラットフォーム用の WinUI アプリケーション。MauiProgram でアプリを構築する。
using Microsoft.UI.Xaml;

namespace FileSearch.Blazor.WinUI;

/// <summary>
/// Windows 用の MAUI アプリケーション。WinUI の初期化後、MauiProgram.CreateMauiApp() で Blazor アプリを構築する。
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>シングルトンのアプリケーションオブジェクトを初期化する。</summary>
	public App()
	{
		this.InitializeComponent();
	}

	/// <summary>MAUI アプリ（DI 登録済み）を構築する。</summary>
	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}

