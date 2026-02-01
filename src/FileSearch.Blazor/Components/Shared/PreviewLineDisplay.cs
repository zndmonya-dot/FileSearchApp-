namespace FileSearch.Blazor.Components.Shared;

/// <summary>
/// プレビュー行の表示用（HTML 済みの Content とハイライト有無）
/// </summary>
public record PreviewLineDisplay(string Content, bool HasMatch);
