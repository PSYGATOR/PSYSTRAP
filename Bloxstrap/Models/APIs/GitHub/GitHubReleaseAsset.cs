public class GithubReleaseAsset
{
    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = null!;

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    /// <summary>PSYstrap: bytes, to tell a finished download from a half-written one.</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }
}