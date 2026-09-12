using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Notifications;

public class EmailTemplateTests
{
    [Fact]
    public void BuildHtml_opts_out_of_client_side_dark_mode_re_coloring()
    {
        // The design is deliberately dark (the app's own "Nocturne" theme,
        // at the user's explicit request) with every color set explicitly
        // — without these two meta tags, Gmail's/Outlook.com's automatic
        // dark-mode transformation can re-color an already-dark email in
        // ways that leave text unreadable against its own background.
        var html = EmailTemplate.BuildHtml("Heading", "Body.", "Überschrift", "Text.", null);

        Assert.Contains("""<meta name="color-scheme" content="dark light">""", html);
        Assert.Contains("""<meta name="supported-color-schemes" content="dark light">""", html);
    }

    [Fact]
    public void BuildHtml_renders_each_blank_line_separated_paragraph_as_its_own_paragraph()
    {
        var html = EmailTemplate.BuildHtml("Heading", "First paragraph.\n\nSecond paragraph.", "Überschrift", "Text.", null);

        Assert.Contains("<p style=\"margin:0 0 14px;\">First paragraph.</p>", html);
        Assert.Contains("<p style=\"margin:0 0 14px;\">Second paragraph.</p>", html);
    }

    [Fact]
    public void BuildHtml_converts_a_single_newline_within_one_paragraph_to_a_line_break()
    {
        var html = EmailTemplate.BuildHtml("Heading", "Line one.\nLine two.", "Überschrift", "Text.", null);

        Assert.Contains("Line one.<br>Line two.", html);
    }

    [Fact]
    public void BuildHtml_references_the_logo_by_content_id_not_a_remote_URL()
    {
        // Deliberately a cid: reference, not a remote https:// image — a
        // recipient's mail client blocking remote images by default (the
        // common default for a first-time sender) must not hide the logo.
        var html = EmailTemplate.BuildHtml("Heading", "Body.", "Überschrift", "Text.", null);

        Assert.Contains($"src=\"cid:{EmailTemplate.LogoContentId}\"", html);
    }

    [Fact]
    public void BuildHtml_HTML_encodes_the_heading()
    {
        var html = EmailTemplate.BuildHtml("<b>Heading</b>", "Body.", "Überschrift", "Text.", null);

        Assert.DoesNotContain("<b>Heading</b>", html);
        Assert.Contains("&lt;b&gt;Heading&lt;/b&gt;", html);
    }

    [Fact]
    public void BuildHtml_renders_both_the_English_and_German_sections()
    {
        var html = EmailTemplate.BuildHtml("English heading", "English body.", "Deutsche Überschrift", "Deutscher Text.", null);

        Assert.Contains("English heading", html);
        Assert.Contains("English body.", html);
        Assert.Contains("Deutsche Überschrift", html);
        Assert.Contains("Deutscher Text.", html);
        Assert.Contains(">EN<", html);
        Assert.Contains(">DE<", html);
    }

    [Fact]
    public void BuildHtml_renders_an_instance_link_button_when_a_URL_is_configured()
    {
        var html = EmailTemplate.BuildHtml("Heading", "Body.", "Überschrift", "Text.", "https://updatewatch2.example.com");

        Assert.Contains("href=\"https://updatewatch2.example.com\"", html);
    }

    [Fact]
    public void BuildHtml_omits_the_link_button_entirely_when_no_URL_is_configured()
    {
        var html = EmailTemplate.BuildHtml("Heading", "Body.", "Überschrift", "Text.", null);

        Assert.DoesNotContain("<a href=", html);
    }

    [Fact]
    public void BuildHtml_HTML_encodes_the_instance_URL()
    {
        var html = EmailTemplate.BuildHtml("Heading", "Body.", "Überschrift", "Text.", "https://example.com/?a=1&b=2");

        Assert.Contains("href=\"https://example.com/?a=1&amp;b=2\"", html);
    }
}
