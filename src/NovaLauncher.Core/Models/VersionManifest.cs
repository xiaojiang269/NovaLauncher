namespace NovaLauncher.Core.Models;

/// <summary>官方版本清单（version_manifest_v2.json）。</summary>
public sealed class VersionManifest
{
    public LatestEntry Latest { get; set; } = new();
    public List<VersionEntry> Versions { get; set; } = new();

    public sealed class LatestEntry
    {
        public string Release { get; set; } = "";
        public string Snapshot { get; set; } = "";
    }

    public sealed class VersionEntry
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string Url { get; set; } = "";
        public string ReleaseTime { get; set; } = "";
    }
}
