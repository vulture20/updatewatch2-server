using System.Net.Mail;
using UpdateWatch2.Server.Notifications;

namespace UpdateWatch2.Server.Tests.Notifications;

public class EmailNotificationServiceTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] FakeLogoBytes = [.. PngSignature, 1, 2, 3];

    [Fact]
    public void BuildMessage_sets_the_plain_text_body_with_both_languages_for_clients_with_no_HTML_support()
    {
        using var message = EmailNotificationService.BuildMessage(
            "uw2@example.com", "UpdateWatch2", "admin@example.com",
            "UpdateWatch2: something happened", "Line one.\n\nLine two.",
            "UpdateWatch2: etwas ist passiert", "Zeile eins.\n\nZeile zwei.",
            null, FakeLogoBytes);

        Assert.Contains("Line one.", message.Body);
        Assert.Contains("Line two.", message.Body);
        Assert.Contains("Zeile eins.", message.Body);
        Assert.Contains("Zeile zwei.", message.Body);
        Assert.False(message.IsBodyHtml);
        Assert.Equal("UpdateWatch2: something happened", message.Subject);
        Assert.Equal("admin@example.com", Assert.Single(message.To).Address);
        Assert.Equal("uw2@example.com", message.From!.Address);
    }

    [Fact]
    public void BuildMessage_appends_the_instance_URL_to_the_plain_text_body_when_configured()
    {
        using var message = EmailNotificationService.BuildMessage(
            "uw2@example.com", "UpdateWatch2", "admin@example.com",
            "UpdateWatch2: something happened", "Body.", "UpdateWatch2: etwas ist passiert", "Text.",
            "https://updatewatch2.example.com", FakeLogoBytes);

        Assert.Contains("https://updatewatch2.example.com", message.Body);
    }

    [Fact]
    public void BuildMessage_adds_exactly_one_HTML_alternate_view_with_the_logo_as_a_linked_resource()
    {
        using var message = EmailNotificationService.BuildMessage(
            "uw2@example.com", "UpdateWatch2", "admin@example.com",
            "UpdateWatch2: something happened", "Body text.", "UpdateWatch2: etwas ist passiert", "Text.",
            null, FakeLogoBytes);

        var view = Assert.Single(message.AlternateViews);
        Assert.Equal("text/html", view.ContentType.MediaType);

        var resource = Assert.Single(view.LinkedResources);
        Assert.Equal(EmailTemplate.LogoContentId, resource.ContentId);
        Assert.Equal("image/png", resource.ContentType.MediaType);
    }

    [Fact]
    public void BuildMessage_embeds_both_languages_in_the_HTML_view_with_the_UpdateWatch2_prefix_stripped()
    {
        using var message = EmailNotificationService.BuildMessage(
            "uw2@example.com", "UpdateWatch2", "admin@example.com",
            "UpdateWatch2: CA root certificate expiring soon", "The root expires soon.",
            "UpdateWatch2: CA-Wurzelzertifikat läuft bald ab", "Das Wurzelzertifikat läuft bald ab.",
            null, FakeLogoBytes);

        var html = ReadAlternateViewContent(Assert.Single(message.AlternateViews));
        Assert.Contains("CA root certificate expiring soon", html);
        Assert.DoesNotContain("UpdateWatch2: CA root certificate expiring soon", html);
        Assert.Contains("The root expires soon.", html);
        Assert.Contains("CA-Wurzelzertifikat läuft bald ab", html);
        Assert.DoesNotContain("UpdateWatch2: CA-Wurzelzertifikat läuft bald ab", html);
        Assert.Contains("Das Wurzelzertifikat läuft bald ab.", html);
        Assert.Contains($"cid:{EmailTemplate.LogoContentId}", html);
    }

    [Fact]
    public void BuildMessage_HTML_encodes_body_text_so_it_cannot_break_out_of_the_template_markup()
    {
        using var message = EmailNotificationService.BuildMessage(
            "uw2@example.com", "UpdateWatch2", "admin@example.com",
            "UpdateWatch2: test", "<script>alert(1)</script>", "UpdateWatch2: Test", "<script>alert(2)</script>",
            null, FakeLogoBytes);

        var html = ReadAlternateViewContent(Assert.Single(message.AlternateViews));
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void BuildMessage_passes_the_instance_URL_through_to_the_HTML_link_button()
    {
        using var message = EmailNotificationService.BuildMessage(
            "uw2@example.com", "UpdateWatch2", "admin@example.com",
            "UpdateWatch2: test", "Body.", "UpdateWatch2: Test", "Text.",
            "https://updatewatch2.example.com", FakeLogoBytes);

        var html = ReadAlternateViewContent(Assert.Single(message.AlternateViews));
        Assert.Contains("href=\"https://updatewatch2.example.com\"", html);
    }

    [Theory]
    [InlineData("UpdateWatch2: CA root certificate expiring soon", "CA root certificate expiring soon")]
    [InlineData("UpdateWatch2 test email", "UpdateWatch2 test email")]
    public void StripSubjectPrefix_removes_the_fixed_prefix_only_when_present(string subject, string expected)
    {
        Assert.Equal(expected, EmailNotificationService.StripSubjectPrefix(subject));
    }

    [Fact]
    public void The_embedded_logo_resource_exists_and_is_a_real_PNG()
    {
        var assembly = typeof(EmailNotificationService).Assembly;
        using var stream = assembly.GetManifestResourceStream("UpdateWatch2.Server.Resources.Email.logo.png");
        Assert.NotNull(stream);

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();

        Assert.True(bytes.Length > 100, "The embedded logo should be a real, non-trivial image, not an empty/placeholder file.");
        Assert.Equal(PngSignature, bytes.Take(PngSignature.Length));
    }

    private static string ReadAlternateViewContent(AlternateView view)
    {
        using var reader = new StreamReader(view.ContentStream);
        return reader.ReadToEnd();
    }
}
