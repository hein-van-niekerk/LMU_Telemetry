namespace LMU.Telemetry.Core.Models;

/// <summary>
/// A parsed LMU/rFactor2-family .svm car setup file - INI-style sections of
/// Key=Value settings (values sometimes followed by a //comment describing the
/// human-readable meaning, e.g. "RearWingSetting=1//7.3 deg"). Settings that
/// appear before the first [Section] header (vehicle class, upgrade string) are
/// stored under the empty-string section key.
///
/// For the future coaching agent: this is what pairs with a telemetry recording
/// to eventually correlate setup choices with driving performance.
/// </summary>
public sealed class CarSetup
{
    /// <summary>Section name -> (setting key -> raw value, comment stripped).</summary>
    public Dictionary<string, Dictionary<string, string>> Sections { get; init; } = new();

    /// <summary>The untouched file contents, kept as a lossless fallback.</summary>
    public string RawText { get; init; } = string.Empty;

    /// <summary>Source file name (not full path) this was parsed from, if known.</summary>
    public string FileName { get; init; } = string.Empty;

    public string? Get(string section, string key) =>
        Sections.TryGetValue(section, out var kv) && kv.TryGetValue(key, out var value) ? value : null;
}
