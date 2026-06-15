// Outlook .msg から件名・差出人・宛先・本文を抽出。
using System.Text;
using System.Text.RegularExpressions;
using FullTextSearch.Core.Extractors;
using MsgReader.Outlook;
using OutlookMessage = MsgReader.Outlook.Storage.Message;
using OutlookAttachment = MsgReader.Outlook.Storage.Attachment;
using OutlookSender = MsgReader.Outlook.Storage.Sender;
using RtfPipe;

namespace FullTextSearch.Infrastructure.Extractors;

/// <summary>
/// Outlook メール（.msg）用のテキスト抽出器。MsgReader を使用（Outlook インストール不要）。
/// </summary>
public class MsgExtractor : ITextExtractor
{
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        var attachmentNames = CollectAttachmentNames(message, cancellationToken);
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

    private static string GetBodyText(OutlookMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.BodyText))
            return message.BodyText;

        if (!string.IsNullOrWhiteSpace(message.BodyHtml))
            return StripHtml(message.BodyHtml);

        if (!string.IsNullOrWhiteSpace(message.BodyRtf))
        {
            var html = Rtf.ToHtml(message.BodyRtf);
            if (!string.IsNullOrWhiteSpace(html))
                return StripHtml(html);
        }

        return string.Empty;
    }

    private static string StripHtml(string html) =>
        HtmlTagRegex.Replace(html, " ")
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

    private static List<string> CollectAttachmentNames(OutlookMessage message, CancellationToken cancellationToken)
    {
        var names = new List<string>();
        if (message.Attachments == null)
            return names;

        foreach (var attachment in message.Attachments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (attachment)
            {
                case OutlookAttachment file when !string.IsNullOrWhiteSpace(file.FileName):
                    names.Add(file.FileName.Trim());
                    break;
                case OutlookMessage embedded when !string.IsNullOrWhiteSpace(embedded.Subject):
                    names.Add(embedded.Subject.Trim());
                    break;
            }
        }

        return names;
    }
}
