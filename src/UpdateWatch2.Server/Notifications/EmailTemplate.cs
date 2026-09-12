namespace UpdateWatch2.Server.Notifications;

/// <summary>
/// Builds the branded HTML body every automated notification email uses —
/// at the user's explicit request ("Sie sollen als HTML-Mails und im
/// aktuellen App-Design verschickt werden. Auch das App-Logo sollte für
/// den Wiedererkennungswert zu sehen sein."), replacing the plain-text-only
/// emails <see cref="Certificates.CertificateExpiryWorker"/> and
/// <see cref="UpdateThresholdNotificationWorker"/> sent before. Colors and
/// spacing are hand-mirrored from <c>web/src/theme/tokens.css</c>'s
/// Nocturne design system (not shared code — an email has to be a single,
/// table-based, all-inline-styles HTML document; there's no way to
/// `@import` the app's real stylesheet into an inbox), and the logo image
/// is the exact same <c>web/src/assets/logo.svg</c> the in-app header uses,
/// rendered once to a PNG at build time (email clients' SVG support is far
/// too inconsistent to embed the vector source directly) and embedded as
/// a `cid:` inline attachment by <see cref="EmailNotificationService"/> —
/// see <see cref="LogoContentId"/>.
///
/// This dark-background design is a deliberate choice, not a default that
/// happened to survive: most bulletproof-HTML-email advice favors a light
/// background specifically to dodge "email client force-inverts dark mode
/// and leaves you with unreadable text," but the user asked for the
/// current (dark, "Nocturne") app design specifically, so both
/// `color-scheme`/`supported-color-schemes` meta tags are set to opt the
/// message out of Gmail's/Outlook.com's automatic dark-mode
/// re-coloring — every color here is set explicitly and is meant to render
/// exactly as authored, not to be left to a client's own dark-mode
/// heuristics.
///
/// Every message is bilingual (English then German), at the user's
/// explicit follow-up request ("Außerdem sollten die Mails auf Deutsch und
/// Englisch sein.") — there's no per-recipient language preference to read
/// here (a single configured mailbox, not a browser locale), so rather
/// than guessing, both languages are shown stacked, each under a small
/// "EN"/"DE" label, exactly like the app's own bilingual UI shows both
/// languages are supported rather than picking one. The optional instance
/// link (<paramref name="instanceUrl"/> parameter on <see cref="BuildHtml"/>)
/// is rendered once, below both language sections, as a single
/// language-neutral button — an outlined accent button, matching the
/// in-app <c>.btn-accent</c> style (a transparent fill with an accent
/// border, not a filled button — Nocturne is deliberately "no saturated
/// flood outside the accent").
/// </summary>
public static class EmailTemplate
{
    /// <summary>The Content-ID <see cref="EmailNotificationService"/> attaches the embedded logo PNG under — referenced here as `cid:{LogoContentId}`.</summary>
    public const string LogoContentId = "uw2-logo";

    // Hand-mirrored from web/src/theme/tokens.css's Nocturne tokens
    // (--color-bg/--color-surface/--color-text/--color-accent/--color-divider/
    // --color-neutral-400/--color-neutral-600) — kept as plain hex literals
    // here rather than shared with the frontend build, since an email has
    // no CSS custom property support worth relying on across clients.
    private const string ColorPageBg = "#0e0f18";
    private const string ColorBg = "#161826";
    private const string ColorSurface = "#232532";
    private const string ColorText = "#e9e9ed";
    private const string ColorBodyText = "#cfd3e5";
    private const string ColorMuted = "#75798c";
    private const string ColorAccent = "#9184d9";
    private const string ColorDivider = "#33364a";

    /// <summary>
    /// Renders the full branded, bilingual HTML document. Each of
    /// <paramref name="headingEn"/>/<paramref name="headingDe"/> is shown
    /// as that language section's own title, separate from the mail's
    /// Subject header (always the English text — see
    /// <see cref="EmailNotificationService.StripSubjectPrefix"/>) so it
    /// doesn't look redundant next to the logo/wordmark above it.
    /// <paramref name="bodyEn"/>/<paramref name="bodyDe"/> follow this
    /// codebase's existing plain-text body convention — blank-line-
    /// separated paragraphs, single newlines within one. A null/empty
    /// <paramref name="instanceUrl"/> omits the "open UpdateWatch2" button
    /// entirely rather than rendering a dead link.
    /// </summary>
    public static string BuildHtml(string headingEn, string bodyEn, string headingDe, string bodyDe, string? instanceUrl)
    {
        var languageSections = string.Join(
            $"\n<tr><td style=\"padding:0 28px;\"><div style=\"height:1px; background-color:{ColorDivider}; margin:20px 0;\"></div></td></tr>\n",
            [
                RenderLanguageSection("EN", headingEn, bodyEn),
                RenderLanguageSection("DE", headingDe, bodyDe),
            ]);

        var button = string.IsNullOrWhiteSpace(instanceUrl) ? "" : $$"""
            <tr><td style="padding:4px 28px 24px; background-color:{{ColorSurface}};">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
            <td style="border-radius:8px; border:1px solid {{ColorAccent}};">
            <a href="{{Encode(instanceUrl)}}" style="display:inline-block; padding:9px 18px; font-size:14px; font-weight:600; color:{{ColorAccent}}; text-decoration:none;">Open UpdateWatch2 · UpdateWatch2 öffnen →</a>
            </td>
            </tr></table>
            </td></tr>
            """;

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="color-scheme" content="dark light">
            <meta name="supported-color-schemes" content="dark light">
            <title>{{Encode(headingEn)}}</title>
            </head>
            <body style="margin:0; padding:0; background-color:{{ColorPageBg}};">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color:{{ColorPageBg}};">
            <tr><td align="center" style="padding:32px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:520px; background-color:{{ColorBg}}; border:1px solid {{ColorDivider}}; border-radius:14px; font-family:'Inter',-apple-system,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
            <tr>
            <td style="padding:22px 28px; border-bottom:1px solid {{ColorDivider}};">
            <table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr>
            <td style="padding-right:12px; vertical-align:middle;"><img src="cid:{{LogoContentId}}" width="32" height="32" alt="UpdateWatch2" style="display:block; border:0;"></td>
            <td style="vertical-align:middle; font-size:16px; font-weight:600; color:{{ColorText}}; letter-spacing:0.2px;">UpdateWatch2</td>
            </tr></table>
            </td>
            </tr>
            <tr><td style="background-color:{{ColorSurface}}; padding-top:28px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">
            {{languageSections}}
            </table>
            </td></tr>
            {{button}}
            <tr>
            <td style="padding:14px 28px; border-top:1px solid {{ColorDivider}}; font-size:12px; color:{{ColorMuted}};">
            This is an automated message from <span style="color:{{ColorAccent}};">UpdateWatch2</span>.<br>
            Dies ist eine automatische Nachricht von <span style="color:{{ColorAccent}};">UpdateWatch2</span>.
            </td>
            </tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    private static string RenderLanguageSection(string languageLabel, string heading, string plainTextBody)
    {
        var bodyHtml = string.Join(
            "\n",
            plainTextBody
                .Replace("\r\n", "\n")
                .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                .Select(paragraph => $"<p style=\"margin:0 0 14px;\">{Encode(paragraph).Replace("\n", "<br>")}</p>"));

        return $$"""
            <tr><td style="padding:0 28px;">
            <div style="font-size:11px; font-weight:600; letter-spacing:0.6px; color:{{ColorMuted}}; margin-bottom:6px;">{{languageLabel}}</div>
            <h1 style="margin:0 0 14px; font-size:18px; line-height:1.4; font-weight:600; color:{{ColorText}};">{{Encode(heading)}}</h1>
            <div style="font-size:14px; line-height:1.6; color:{{ColorBodyText}};">
            {{bodyHtml}}
            </div>
            </td></tr>
            """;
    }

    /// <summary>
    /// A minimal, HTML-significant-characters-only encoder — deliberately
    /// NOT <see cref="System.Net.WebUtility.HtmlEncode"/>, which also
    /// converts every non-ASCII character (confirmed by hand: "Ü" becomes
    /// "&amp;#220;") into a numeric character reference. That's harmless
    /// to a real mail client, but pointless here: the document already
    /// declares <c>&lt;meta charset="utf-8"&gt;</c> and the HTML view is
    /// sent as UTF-8 (see <see cref="EmailNotificationService.BuildMessage"/>),
    /// so German
    /// text with umlauts/ß can just be UTF-8 bytes like everything else in
    /// the document — encoding it anyway would only make the generated
    /// HTML source needlessly harder to read for zero rendering benefit.
    /// </summary>
    private static string Encode(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
