using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SpotifyTaskbarWidget;

public record LyricLine(TimeSpan Time, string Text);

public class SongLyrics
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public bool IsInstrumental { get; set; }
    public List<LyricLine> Lines { get; set; } = new();

    public string GetLineAt(TimeSpan position)
    {
        if (Lines.Count == 0) return "";
        
        // Find the latest line with timestamp <= position
        int low = 0, high = Lines.Count - 1;
        int match = -1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            if (Lines[mid].Time <= position)
            {
                match = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        if (match >= 0)
        {
            return Lines[match].Text;
        }

        return "";
    }
}

public sealed class LyricsService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(6)
    };

    static LyricsService()
    {
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyTaskbarWidget/1.3.0 (https://github.com/mechanicwb2-hub/spotify-taskbar-widget)");
    }

    private static readonly Dictionary<string, SongLyrics?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object CacheLock = new();

    private static readonly Regex LrcRegex = new(@"^\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\](.*)$", RegexOptions.Compiled);

    private class LrclibResponse
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("trackName")]
        public string? TrackName { get; set; }
        [JsonPropertyName("artistName")]
        public string? ArtistName { get; set; }
        [JsonPropertyName("albumName")]
        public string? AlbumName { get; set; }
        [JsonPropertyName("duration")]
        public double Duration { get; set; }
        [JsonPropertyName("instrumental")]
        public bool Instrumental { get; set; }
        [JsonPropertyName("plainLyrics")]
        public string? PlainLyrics { get; set; }
        [JsonPropertyName("syncedLyrics")]
        public string? SyncedLyrics { get; set; }
    }

    public static async Task<SongLyrics?> GetLyricsAsync(string title, string artist, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        // Clean up title (remove "- Remastered", "(feat. ...)", etc. for fallback if needed)
        string cleanTitle = CleanTrackTitle(title);
        string cleanArtist = CleanArtistName(artist);
        string cacheKey = $"{cleanArtist} - {cleanTitle}".Trim();

        lock (CacheLock)
        {
            if (Cache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        SongLyrics? result = null;
        LrclibResponse? plainFallback = null;

        try
        {
            // 1. Try exact get
            string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (duration > TimeSpan.Zero)
            {
                url += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
            }

            var resp = await Http.GetAsync(url);
            if (resp.IsSuccessStatusCode)
            {
                var data = await resp.Content.ReadFromJsonAsync<LrclibResponse>();
                if (data != null)
                {
                    if (!string.IsNullOrWhiteSpace(data.SyncedLyrics) || data.Instrumental)
                    {
                        result = ParseLrclibResponse(data, duration);
                    }
                    else
                    {
                        plainFallback = data;
                    }
                }
            }

            // 2. Try normalized diacritics
            string noDiaTitle = RemoveDiacritics(cleanTitle);
            string noDiaArtist = RemoveDiacritics(cleanArtist);

            if (result == null && (cleanTitle != title || cleanArtist != artist || noDiaArtist != cleanArtist))
            {
                string fallbackUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(noDiaTitle)}&artist_name={Uri.EscapeDataString(noDiaArtist)}";
                if (duration > TimeSpan.Zero)
                {
                    fallbackUrl += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
                }
                var fallbackResp = await Http.GetAsync(fallbackUrl);
                if (fallbackResp.IsSuccessStatusCode)
                {
                    var data = await fallbackResp.Content.ReadFromJsonAsync<LrclibResponse>();
                    if (data != null)
                    {
                        if (!string.IsNullOrWhiteSpace(data.SyncedLyrics) || data.Instrumental)
                        {
                            result = ParseLrclibResponse(data, duration);
                        }
                        else if (plainFallback == null)
                        {
                            plainFallback = data;
                        }
                    }
                }
            }

            // 3. Search queries prioritizing synced lyrics (fixes songs where synced lyrics are under alternate artist spelling)
            if (result == null)
            {
                var searchQueries = new List<string>
                {
                    $"{cleanArtist} {cleanTitle}",
                    $"{noDiaArtist} {noDiaTitle}",
                    cleanTitle,
                    noDiaTitle
                }.Distinct(StringComparer.OrdinalIgnoreCase).Where(q => !string.IsNullOrWhiteSpace(q));

                foreach (var q in searchQueries)
                {
                    string searchUrl = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(q)}";
                    var searchResp = await Http.GetAsync(searchUrl);
                    if (searchResp.IsSuccessStatusCode)
                    {
                        var list = await searchResp.Content.ReadFromJsonAsync<List<LrclibResponse>>();
                        if (list != null && list.Count > 0)
                        {
                            var syncedItems = list.Where(x => !string.IsNullOrWhiteSpace(x.SyncedLyrics) || x.Instrumental).ToList();
                            if (syncedItems.Count > 0)
                            {
                                LrclibResponse best;
                                if (duration > TimeSpan.Zero)
                                {
                                    double targetSec = duration.TotalSeconds;
                                    best = syncedItems.OrderBy(x => Math.Abs(x.Duration - targetSec)).First();
                                }
                                else
                                {
                                    best = syncedItems[0];
                                }
                                result = ParseLrclibResponse(best, duration);
                                break;
                            }
                            else if (plainFallback == null)
                            {
                                plainFallback = list[0];
                            }
                        }
                    }
                }
            }

            // 4. Fallback to plain lyrics if no synced lyrics found anywhere
            if (result == null && plainFallback != null)
            {
                result = ParseLrclibResponse(plainFallback, duration);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"Lyrics fetch failed for '{artist} - {title}': {ex.Message}");
        }

        lock (CacheLock)
        {
            Cache[cacheKey] = result;
        }

        return result;
    }

    private static SongLyrics ParseLrclibResponse(LrclibResponse data, TimeSpan duration)
    {
        var lyrics = new SongLyrics
        {
            Title = data.TrackName ?? "",
            Artist = data.ArtistName ?? "",
            IsInstrumental = data.Instrumental
        };

        if (data.Instrumental)
        {
            return lyrics;
        }

        if (!string.IsNullOrWhiteSpace(data.SyncedLyrics))
        {
            lyrics.Lines = ParseLrc(data.SyncedLyrics);
        }
        else if (!string.IsNullOrWhiteSpace(data.PlainLyrics))
        {
            var plainLines = data.PlainLyrics.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (plainLines.Length > 0)
            {
                double totalSec = duration > TimeSpan.Zero ? duration.TotalSeconds : (data.Duration > 0 ? data.Duration : plainLines.Length * 4.0);
                double intervalSec = totalSec / plainLines.Length;
                for (int i = 0; i < plainLines.Length; i++)
                {
                    lyrics.Lines.Add(new LyricLine(TimeSpan.FromSeconds(i * intervalSec), plainLines[i]));
                }
            }
        }

        return lyrics;
    }

    public static List<LyricLine> ParseLrc(string lrcContent)
    {
        var list = new List<LyricLine>();
        var rawLines = lrcContent.Split('\n');

        foreach (var raw in rawLines)
        {
            string line = raw.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var match = LrcRegex.Match(line);
            if (match.Success)
            {
                int min = int.Parse(match.Groups[1].Value);
                int sec = int.Parse(match.Groups[2].Value);
                int ms = 0;
                if (match.Groups[3].Success)
                {
                    string msStr = match.Groups[3].Value;
                    if (msStr.Length == 2) ms = int.Parse(msStr) * 10;
                    else if (msStr.Length == 3) ms = int.Parse(msStr);
                }

                var time = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(ms);
                string text = match.Groups[4].Value.Trim();
                list.Add(new LyricLine(time, text));
            }
        }

        list.Sort((a, b) => a.Time.CompareTo(b.Time));
        return list;
    }

    private static string CleanTrackTitle(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        // Remove "(feat. ...)", "[feat. ...]", "- Remastered...", "- Live...", "(Remastered...)"
        string clean = Regex.Replace(title, @"\s*[\(\[](?:feat|with|remastered|live|deluxe|bonus|anniversary).*?[\)\]]", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"\s*-\s*(?:feat|remastered|live|deluxe|bonus|radio edit).*$", "", RegexOptions.IgnoreCase);
        return clean.Trim();
    }

    private static string CleanArtistName(string artist)
    {
        if (string.IsNullOrEmpty(artist)) return "";
        // Split on comma, semicolon, feat, etc.
        string clean = Regex.Replace(artist, @"\s*(?:,|&|feat\.|ft\.).*$", "", RegexOptions.IgnoreCase);
        return clean.Trim();
    }

    public static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        string normalized = text.Replace('đ', 'd').Replace('Đ', 'D').Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (char c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }
}
