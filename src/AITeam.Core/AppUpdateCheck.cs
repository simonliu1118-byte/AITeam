using System.Text.Json;
using System.Text.RegularExpressions;

namespace AITeam.Services;

/// <summary>GitHub 上一個比目前新的正式 Release：版號，以及 zip 和 SHA-256 檔的下載位置。</summary>
public sealed record ReleaseOffer(Version Version, string Tag, string ZipName, string PackageFolder, string ZipUrl, string ChecksumUrl);

/// <summary>
/// 自動更新：去 GitHub 找比自己新的正式 Release（跟 CYERPAutoInput 同一套做法）。
/// 只認 AITeam 自己的 tag（vX.Y.Z）、不認草稿與預先發布、zip 和 .sha256 兩個檔都要在。
/// Actions 的工程測試包不算。這裡不碰網路，可以單獨測試。
/// </summary>
public static class AppUpdateCheck
{
    public const string Repository = "simonliu1118-byte/AITeam";

    private static readonly Regex TagPattern = new(@"^v(\d+)\.(\d+)\.(\d+)$", RegexOptions.CultureInvariant);

    public static string ReleasesUrl => $"https://api.github.com/repos/{Repository}/releases?per_page=50";
    public static string ReleasesPage => $"https://github.com/{Repository}/releases";

    /// <summary>
    /// Release zip 裡面那一層資料夾的名稱，跟 release-windows.yml 打包的方式一致。
    /// 固定叫 AITeam：解壓縮出來就是 AITeam 這個家，不帶版號。
    /// </summary>
    public const string PackageFolder = "AITeam";

    /// <summary>Release 的 zip 檔名，版號用使用者看到的格式（VX.Y.Z）。</summary>
    public static string ZipName(Version version) => $"AITeam-V{version.ToString(3)}.zip";

    /// <summary>
    /// 比 <paramref name="current"/> 新的版本裡最新的那個；沒有就回 null。
    /// 同一版的 Build（例如 V0.33.0 Build 2）排在該版正式 Release 之後，所以只有 X.Y.Z 比較大才算新。
    /// </summary>
    public static ReleaseOffer? Newest(string releasesJson, Version current)
    {
        using var document = JsonDocument.Parse(releasesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return null;

        ReleaseOffer? best = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft") || Flag(release, "prerelease")) continue;
            var tag = Text(release, "tag_name");
            var match = TagPattern.Match(tag);
            if (!match.Success) continue;

            var version = new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
            if (version <= current || (best is not null && version <= best.Version)) continue;

            var zipName = ZipName(version);
            string zipUrl = "", checksumUrl = "";
            if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    var url = Text(asset, "browser_download_url");
                    if (name == zipName) zipUrl = url;
                    else if (name == zipName + ".sha256") checksumUrl = url;
                }
            }

            if (!IsReleaseDownload(zipUrl, tag) || !IsReleaseDownload(checksumUrl, tag)) continue;
            best = new ReleaseOffer(version, tag, zipName, PackageFolder, zipUrl, checksumUrl);
        }

        return best;
    }

    /// <summary>只下載這個 Repo 自己、這個 tag 底下的檔案，不會被 Release 說明裡的其他網址帶走。</summary>
    private static bool IsReleaseDownload(string url, string tag) =>
        url.StartsWith($"https://github.com/{Repository}/releases/download/{tag}/", StringComparison.Ordinal);

    /// <summary>「雜湊  檔名」格式的 SHA-256 檔裡，<paramref name="zipName"/> 的雜湊；找不到回 null。</summary>
    public static string? ExpectedSha256(string checksumText, string zipName)
    {
        foreach (var line in checksumText.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[1].TrimStart('*') == zipName && Regex.IsMatch(parts[0], "^[0-9a-fA-F]{64}$"))
                return parts[0].ToLowerInvariant();
        }
        return null;
    }

    /// <summary>從建置時嵌入的版本字串（X.Y.Z+build.N）取出 X.Y.Z；讀不到就回 null（這時不檢查更新）。</summary>
    public static Version? CurrentVersion(string? informationalVersion)
    {
        var baseVersion = (informationalVersion ?? "").Split('+', 2)[0].Trim();
        return Version.TryParse(baseVersion, out var version) && version.Major >= 0
            ? new Version(version.Major, version.Minor, Math.Max(0, version.Build))
            : null;
    }

    private static bool Flag(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement element, string key) =>
        element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}

/// <summary>更新腳本留給下一次啟動的結果：有沒有換成功、換到哪一版、原因。</summary>
public sealed record UpdateOutcome(bool Ok, string Version, string Detail)
{
    /// <summary>第一行「ok｜failed」、第二行版號、其餘是說明；其他格式回 null。</summary>
    public static UpdateOutcome? Parse(string text)
    {
        var lines = text.Replace("\r", "").Split('\n');
        if (lines.Length < 2 || lines[1].Trim().Length == 0) return null;
        var state = lines[0].Trim().TrimStart('﻿');
        if (state != "ok" && state != "failed") return null;
        return new UpdateOutcome(
            state == "ok",
            lines[1].Trim(),
            string.Join(" ", lines.Skip(2).Select(l => l.Trim()).Where(l => l.Length > 0)));
    }
}
