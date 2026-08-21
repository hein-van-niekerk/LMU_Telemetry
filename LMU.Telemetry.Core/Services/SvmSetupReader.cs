using LMU.Telemetry.Core.Models;

namespace LMU.Telemetry.Core.Services;

/// <summary>
/// Parses LMU/rFactor2-family .svm car setup files (INI-style sections of
/// Key=Value settings) into a structured CarSetup.
/// </summary>
public static class SvmSetupReader
{
    public static CarSetup Parse(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var setup = ParseText(text);
        return new CarSetup
        {
            Sections = setup.Sections,
            RawText = text,
            FileName = Path.GetFileName(filePath)
        };
    }

    public static CarSetup ParseText(string text)
    {
        var sections = new Dictionary<string, Dictionary<string, string>>();
        var currentSectionName = string.Empty; // settings before the first [Section] header
        var currentSection = new Dictionary<string, string>();
        sections[currentSectionName] = currentSection;

        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0) continue;
            if (trimmed.StartsWith("//")) continue; // full-line comment

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSectionName = trimmed[1..^1];
                if (!sections.TryGetValue(currentSectionName, out currentSection!))
                {
                    currentSection = new Dictionary<string, string>();
                    sections[currentSectionName] = currentSection;
                }
                continue;
            }

            var eq = trimmed.IndexOf('=');
            if (eq < 0) continue; // not a Key=Value line, ignore rather than fail the whole parse

            var key = trimmed[..eq].Trim();
            var rest = trimmed[(eq + 1)..];

            // Strip a trailing //comment (e.g. "RearWingSetting=1//7.3 deg" -> "1").
            // Values in this format don't legitimately contain "//", so a plain
            // IndexOf is safe here.
            var commentIdx = rest.IndexOf("//", StringComparison.Ordinal);
            var value = (commentIdx >= 0 ? rest[..commentIdx] : rest).Trim();

            if (key.Length > 0)
            {
                currentSection[key] = value;
            }
        }

        return new CarSetup { Sections = sections };
    }
}
