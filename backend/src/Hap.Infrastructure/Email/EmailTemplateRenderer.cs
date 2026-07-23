using System.Reflection;
using System.Text;

namespace Hap.Infrastructure.Email;

/// <summary>
/// Renders a plain-text email from an embedded-resource template (FR-067 spirit: content
/// externalised, not inline C# strings — see <c>Email/EmailTemplates/*.txt</c> and this project's
/// csproj <c>EmbeddedResource</c> item). Embedded (not copy-to-output) so the compiled assembly is
/// the whole artefact — no risk of a missing file in the Docker image.
///
/// <para>Template file shape: the FIRST line is the subject (with <c>{{Token}}</c>-style
/// placeholders), then a blank line, then the body (same placeholder syntax, may span many lines).
/// Substitution is a plain string replace — no escaping, no conditionals; callers supply exactly the
/// tokens each template needs.</para>
/// </summary>
public sealed class EmailTemplateRenderer
{
    /// <summary>Renders <paramref name="templateFileName"/> (e.g. "weekly-update-owner-nag.txt") with
    /// <paramref name="tokens"/> substituted into both the subject and body.</summary>
    public (string Subject, string Body) Render(string templateFileName, IReadOnlyDictionary<string, string> tokens)
    {
        var assembly = typeof(EmailTemplateRenderer).Assembly;

        // Matched by suffix rather than an assumed exact manifest name: embedded-resource logical
        // names are derived from RootNamespace + folder path by the SDK and are not worth pinning
        // exactly here — this is robust to that naming regardless of the precise scheme in play.
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(n => n.EndsWith("." + templateFileName, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new InvalidOperationException(
                $"Email template embedded resource not found for '{templateFileName}'. " +
                $"Known embedded resources: {string.Join(", ", assembly.GetManifestResourceNames())}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var raw = reader.ReadToEnd();

        var newlineIndex = raw.IndexOf('\n');
        var subjectLine = newlineIndex >= 0 ? raw[..newlineIndex] : raw;
        var rest = newlineIndex >= 0 ? raw[(newlineIndex + 1)..] : string.Empty;
        var body = rest.TrimStart('\r', '\n');

        return (Substitute(subjectLine.TrimEnd('\r'), tokens), Substitute(body, tokens));
    }

    private static string Substitute(string template, IReadOnlyDictionary<string, string> tokens)
    {
        var result = template;
        foreach (var (key, value) in tokens)
        {
            result = result.Replace("{{" + key + "}}", value, StringComparison.Ordinal);
        }
        return result;
    }
}
