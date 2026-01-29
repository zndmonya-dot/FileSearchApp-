using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FullTextSearch.Core.Models;

namespace FullTextSearch.App.Controls;

/// <summary>
/// マッチ箇所をハイライト表示するコントロール
/// </summary>
public partial class MatchHighlightList : UserControl
{
    public static readonly DependencyProperty HighlightsProperty =
        DependencyProperty.Register(
            nameof(Highlights),
            typeof(List<MatchHighlight>),
            typeof(MatchHighlightList),
            new PropertyMetadata(null, OnHighlightsChanged));

    public List<MatchHighlight>? Highlights
    {
        get => (List<MatchHighlight>?)GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    public MatchHighlightList()
    {
        InitializeComponent();
    }

    private static void OnHighlightsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MatchHighlightList control)
        {
            control.UpdateHighlights();
        }
    }

    private void UpdateHighlights()
    {
        HighlightItemsControl.Items.Clear();

        if (Highlights == null || Highlights.Count == 0)
        {
            return;
        }

        foreach (var highlight in Highlights)
        {
            var textBlock = CreateHighlightedTextBlock(highlight);
            var border = new Border
            {
                Background = (Brush)FindResource("MaterialDesignToolBarBackground"),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Child = textBlock
            };

            HighlightItemsControl.Items.Add(border);
        }
    }

    private static TextBlock CreateHighlightedTextBlock(MatchHighlight highlight)
    {
        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas, MS Gothic"),
            FontSize = 12
        };

        var text = highlight.Text;
        var start = highlight.HighlightStart;
        var end = highlight.HighlightEnd;

        if (start >= 0 && end > start && end <= text.Length)
        {
            // ハイライト前のテキスト
            if (start > 0)
            {
                textBlock.Inlines.Add(new Run(text[..start]));
            }

            // ハイライトされたテキスト
            var highlightedRun = new Run(text[start..end])
            {
                Background = Brushes.Yellow,
                FontWeight = FontWeights.Bold
            };
            textBlock.Inlines.Add(highlightedRun);

            // ハイライト後のテキスト
            if (end < text.Length)
            {
                textBlock.Inlines.Add(new Run(text[end..]));
            }
        }
        else
        {
            // ハイライト位置が無効な場合はそのまま表示
            textBlock.Text = text;
        }

        return textBlock;
    }
}

