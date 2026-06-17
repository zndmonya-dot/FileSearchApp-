// Outlook .msg から件名・差出人・宛先・本文を抽出。
using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using MsgReader;
using MsgReader.Outlook;
using OutlookMessage = MsgReader.Outlook.Storage.Message;
using OutlookSender = MsgReader.Outlook.Storage.Sender;
using RtfPipe;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// Outlook メール（.msg）用のテキスト抽出器。MsgReader を使用（Outlook インストール不要）。
/// </summary>
public class MsgExtractor : ITextExtractor
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Reader BodyReader = new();

    static MsgExtractor() => MsgEncodingBootstrap.EnsureRegistered();

    /// <inheritdoc />
    public IEnumerable<string> SupportedExtensions => SupportedExtensionSets.OutlookMsg;

    /// <inheritdoc />
    public bool CanExtract(string extension) => SupportedExtensionSets.OutlookMsg.Contains(extension);

    /// <inheritdoc />
    public Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        cancellationToken.ThrowIfCancellationRequested();

        using var message = new OutlookMessage(filePath);
        var sb = new StringBuilder();

        AppendHeader(sb, "件名", message.Subject);
        AppendHeader(sb, "差出人", FormatSender(message.Sender));
        AppendHeader(sb, "宛先", message.GetEmailRecipients(RecipientType.To, false, false));
        AppendHeader(sb, "Cc", message.GetEmailRecipients(RecipientType.Cc, false, false));

        var body = GetBodyText(message);
        if (!string.IsNullOrWhiteSpace(body))
        {
            sb.AppendLine();
            sb.AppendLine(body.Trim());
        }

        var attachmentNames = CollectAttachmentNames(message);
        if (attachmentNames.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("添付: " + string.Join(", ", attachmentNames));
        }

        return Task.FromResult(sb.ToString());
    }

    private static void AppendHeader(StringBuilder sb, string label, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        sb.Append(label);
        sb.Append(": ");
        sb.AppendLine(value.Trim());
    }

    private static string FormatSender(OutlookSender? sender)
    {
        if (sender == null)
            return string.Empty;

        var name = sender.DisplayName?.Trim();
        var email = sender.Email?.Trim();
        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(email))
            return $"{name} <{email}>";

        return name ?? email ?? string.Empty;
    }

    /// <summary>
    /// MsgReader の統合 API で RTF 内 HTML 等も含めて本文を取得する。
    /// 失敗時のみ個別プロパティへフォールバックする。
    /// </summary>
    private static string GetBodyText(OutlookMessage message)
    {
        try
        {
            var extracted = BodyReader.ExtractMsgEmailBody(message, ReaderHyperLinks.None, null, false, false);
            var fromReader = NormalizeBody(extracted);
            if (!string.IsNullOrWhiteSpace(fromReader))
                return fromReader;
        }
        catch
        {
            // フォールバックへ
        }

        var plain = NormalizeBody(message.BodyText);
        if (!string.IsNullOrWhiteSpace(plain))
            return plain;

        var html = NormalizeBody(message.BodyHtml, stripHtml: true);
        if (!string.IsNullOrWhiteSpace(html))
            return html;

        try
        {
            if (!string.IsNullOrWhiteSpace(message.BodyRtf))
            {
                var rtfHtml = Rtf.ToHtml(message.BodyRtf);
                var fromRtf = NormalizeBody(rtfHtml, stripHtml: true);
                if (!string.IsNullOrWhiteSpace(fromRtf))
                    return fromRtf;
            }
        }
        catch
        {
            // 本文なしとして扱う
        }

        return string.Empty;
    }

    private static string NormalizeBody(string? raw, bool stripHtml = false)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var text = stripHtml ? StripHtml(raw) : raw;
        return text.Trim();
    }

    private static string StripHtml(string html) =>
        HtmlTagRegex.Replace(html, " ")
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

    private static List<string> CollectAttachmentNames(OutlookMessage message)
    {
        try
        {
            var names = message.GetAttachmentNames();
            if (string.IsNullOrWhiteSpace(names))
                return [];

            return names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
