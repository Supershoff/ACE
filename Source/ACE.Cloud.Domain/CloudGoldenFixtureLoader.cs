using System.Text.Json;

namespace ACE.Cloud.Domain;

/// <summary>
/// Shared deserialization for every protected golden fixture directory this codebase reads (issue
/// #24/#25's appraisal captures, issue #28's icon corpus): each fixture is one JSON file, ordered
/// deterministically by filename so a corpus produces the same fixture order on every run. Extracted
/// once here rather than duplicated per fixture kind (AGENTS.md: "search adjacent ACE and Cloud code
/// for an existing helper... before accepting duplication").
/// </summary>
public static class CloudGoldenFixtureLoader
{
    public static IReadOnlyList<T> LoadFromDirectory<T>(string directory, string searchPattern)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("A fixture directory is required.", nameof(directory));
        }

        var files = Directory.GetFiles(directory, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal);

        return files
            .Select(path => JsonSerializer.Deserialize<T>(File.ReadAllText(path))
                ?? throw new InvalidDataException($"{path} did not deserialize to a {typeof(T).Name}."))
            .ToList();
    }
}
