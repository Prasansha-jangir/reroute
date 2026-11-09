using System.Text;
using System.Text.RegularExpressions;

namespace Reroute.Copilot.Api.Rag;

public sealed record SourceDocument(string Id, string Title, string Airline, string Language, string Version, string Content);

public sealed record Chunk(string DocumentId, string DocumentTitle, string Section, int Index, string Text, string Airline, string Language, string Version);

/// <summary>
/// Structure-aware chunking for legal/policy text: split on headings first (an article is the semantic unit),
/// then pack paragraphs up to a size budget with overlap. Fixed-size chunking would separate "€400" from its condition.
/// </summary>
public static partial class DocumentChunker
{
    public static IReadOnlyList<Chunk> Chunk(SourceDocument document, int maxChars = 1800, int overlapChars = 250)
    {
        ArgumentNullException.ThrowIfNull(document);
        var chunks = new List<Chunk>();
        var sections = Heading().Split(document.Content);
        var headings = Heading().Matches(document.Content).Select(m => m.Value.TrimStart('#', ' ').Trim()).ToList();

        // sections[0] is the preamble before the first heading.
        for (var s = 0; s < sections.Length; s++)
        {
            var section = s == 0 ? document.Title : headings[s - 1];
            var paragraphs = sections[s].Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var buffer = new StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                if (buffer.Length > 0 && buffer.Length + paragraph.Length > maxChars)
                {
                    Emit(buffer.ToString());
                    var tail = buffer.ToString();
                    buffer.Clear().Append(tail[Math.Max(0, tail.Length - overlapChars)..]).Append("\n\n");
                }

                buffer.Append(paragraph).Append("\n\n");
            }

            if (buffer.ToString().Trim().Length > 0)
            {
                Emit(buffer.ToString());
            }

            void Emit(string text) => chunks.Add(new Chunk(
                document.Id, document.Title, section, chunks.Count, $"{section}\n{text.Trim()}", document.Airline, document.Language, document.Version));
        }

        return chunks;
    }

    [GeneratedRegex(@"^#{1,3} .+$", RegexOptions.Multiline)]
    private static partial Regex Heading();
}
