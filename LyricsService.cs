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

public record LyricLineInfo(string Text, TimeSpan StartTime, TimeSpan EndTime, double Progress, bool HasMatch);

public class SongLyrics
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public bool IsInstrumental { get; set; }
    public List<LyricLine> Lines { get; set; } = new();

    public LyricLineInfo GetLineInfoAt(TimeSpan position)
    {
        if (Lines.Count == 0)
            return new LyricLineInfo("", TimeSpan.Zero, TimeSpan.Zero, 0, false);

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
            var cur = Lines[match];
            TimeSpan start = cur.Time;
            TimeSpan end = (match + 1 < Lines.Count) ? Lines[match + 1].Time : (start + TimeSpan.FromSeconds(5));
            TimeSpan duration = end - start;
            double progress = 0.0;
            if (duration.TotalMilliseconds > 100)
            {
                progress = Math.Clamp((position - start).TotalMilliseconds / duration.TotalMilliseconds, 0.0, 1.0);
            }

            return new LyricLineInfo(cur.Text, start, end, progress, true);
        }

        TimeSpan nextStart = Lines[0].Time;
        double introProgress = nextStart.TotalMilliseconds > 0
            ? Math.Clamp(position.TotalMilliseconds / nextStart.TotalMilliseconds, 0.0, 1.0)
            : 0;
        return new LyricLineInfo("♪", TimeSpan.Zero, nextStart, introProgress, false);
    }

    public string GetLineAt(TimeSpan position)
    {
        return GetLineInfoAt(position).Text;
    }
}

public sealed class LyricsService
{
    private static readonly HttpClient Http;

    static LyricsService()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            ConnectTimeout = TimeSpan.FromSeconds(5),
            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13
            }
        };

        Http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("SpotifyTaskbarWidget/1.3.0");
    }

    private static readonly Dictionary<string, SongLyrics> Cache = new(StringComparer.OrdinalIgnoreCase);
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

    private class KugouSearchResponse
    {
        [JsonPropertyName("data")]
        public KugouSearchData? Data { get; set; }
    }

    private class KugouSearchData
    {
        [JsonPropertyName("info")]
        public List<KugouSongInfo>? Info { get; set; }
    }

    private class KugouSongInfo
    {
        [JsonPropertyName("songname")]
        public string? SongName { get; set; }
        [JsonPropertyName("singername")]
        public string? SingerName { get; set; }
        [JsonPropertyName("hash")]
        public string? Hash { get; set; }
        [JsonPropertyName("duration")]
        public int Duration { get; set; }
    }

    private class KugouCandidatesResponse
    {
        [JsonPropertyName("candidates")]
        public List<KugouCandidate>? Candidates { get; set; }
    }

    private class KugouCandidate
    {
        [JsonPropertyName("id")]
        public object? Id { get; set; }
        [JsonPropertyName("accesskey")]
        public string? AccessKey { get; set; }
        [JsonPropertyName("song")]
        public string? Song { get; set; }
        [JsonPropertyName("singer")]
        public string? Singer { get; set; }
    }

    private class KugouDownloadResponse
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class QqSearchResponse
    {
        [JsonPropertyName("data")]
        public QqSearchData? Data { get; set; }
    }

    private class QqSearchData
    {
        [JsonPropertyName("song")]
        public QqSongContainer? Song { get; set; }
    }

    private class QqSongContainer
    {
        [JsonPropertyName("list")]
        public List<QqSongItem>? List { get; set; }
    }

    private class QqSongItem
    {
        [JsonPropertyName("songname")]
        public string? SongName { get; set; }
        [JsonPropertyName("songmid")]
        public string? SongMid { get; set; }
        [JsonPropertyName("interval")]
        public int Interval { get; set; }
        [JsonPropertyName("singer")]
        public List<QqSingerItem>? Singer { get; set; }
    }

    private class QqSingerItem
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class QqLyricResponse
    {
        [JsonPropertyName("retcode")]
        public int RetCode { get; set; }
        [JsonPropertyName("lyric")]
        public string? Lyric { get; set; }
    }

    private static async Task<HttpResponseMessage?> HttpGetWithRetryAsync(string url, int maxRetries = 2)
    {
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var resp = await Http.GetAsync(url);
                return resp;
            }
            catch (Exception ex)
            {
                if (attempt == maxRetries)
                {
                    Diag.Log($"[LyricsService] HTTP GET failed for '{url}': {ex.Message}");
                    return null;
                }
                await Task.Delay(250);
            }
        }
        return null;
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
            if (Cache.TryGetValue(cacheKey, out var cached) && cached != null)
                return cached;
        }

        SongLyrics? result = null;
        LrclibResponse? plainFallback = null;
        LrclibResponse? instrumentalFallback = null;

        try
        {
            // 1. Try exact get
            string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (duration > TimeSpan.Zero)
            {
                url += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
            }

            var resp = await HttpGetWithRetryAsync(url);
            if (resp != null && resp.IsSuccessStatusCode)
            {
                var data = await resp.Content.ReadFromJsonAsync<LrclibResponse>();
                if (data != null)
                {
                    if (!string.IsNullOrWhiteSpace(data.SyncedLyrics))
                    {
                        result = ParseLrclibResponse(data, duration);
                    }
                    else if (data.Instrumental)
                    {
                        instrumentalFallback = data;
                    }
                    else if (!string.IsNullOrWhiteSpace(data.PlainLyrics))
                    {
                        plainFallback = data;
                    }
                }
            }

            // 1b. Try clean title & clean artist (fixes multi-artist Spotify strings like "PiaLinh, Lâm Bảo Ngọc, Minh Cà Ri")
            if (result == null && (cleanTitle != title || cleanArtist != artist))
            {
                string cleanUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
                if (duration > TimeSpan.Zero)
                {
                    cleanUrl += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
                }
                var cleanResp = await HttpGetWithRetryAsync(cleanUrl);
                if (cleanResp != null && cleanResp.IsSuccessStatusCode)
                {
                    var data = await cleanResp.Content.ReadFromJsonAsync<LrclibResponse>();
                    if (data != null)
                    {
                        if (!string.IsNullOrWhiteSpace(data.SyncedLyrics))
                        {
                            result = ParseLrclibResponse(data, duration);
                        }
                        else if (data.Instrumental && instrumentalFallback == null)
                        {
                            instrumentalFallback = data;
                        }
                        else if (plainFallback == null && !string.IsNullOrWhiteSpace(data.PlainLyrics))
                        {
                            plainFallback = data;
                        }
                    }
                }
            }

            // 2. Try normalized diacritics
            string noDiaTitle = RemoveDiacritics(cleanTitle);
            string noDiaArtist = RemoveDiacritics(cleanArtist);

            if (result == null && (noDiaTitle != cleanTitle || noDiaArtist != cleanArtist))
            {
                string fallbackUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(noDiaTitle)}&artist_name={Uri.EscapeDataString(noDiaArtist)}";
                if (duration > TimeSpan.Zero)
                {
                    fallbackUrl += $"&duration={(int)Math.Round(duration.TotalSeconds)}";
                }
                var fallbackResp = await HttpGetWithRetryAsync(fallbackUrl);
                if (fallbackResp != null && fallbackResp.IsSuccessStatusCode)
                {
                    var data = await fallbackResp.Content.ReadFromJsonAsync<LrclibResponse>();
                    if (data != null)
                    {
                        if (!string.IsNullOrWhiteSpace(data.SyncedLyrics))
                        {
                            result = ParseLrclibResponse(data, duration);
                        }
                        else if (data.Instrumental && instrumentalFallback == null)
                        {
                            instrumentalFallback = data;
                        }
                        else if (plainFallback == null && !string.IsNullOrWhiteSpace(data.PlainLyrics))
                        {
                            plainFallback = data;
                        }
                    }
                }
            }

            // 3. Search queries prioritizing synced lyrics (with STRICT title & artist validation)
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
                    var searchResp = await HttpGetWithRetryAsync(searchUrl);
                    if (searchResp != null && searchResp.IsSuccessStatusCode)
                    {
                        var list = await searchResp.Content.ReadFromJsonAsync<List<LrclibResponse>>();
                        if (list != null && list.Count > 0)
                        {
                            // STRICT: Only consider items whose title and artist actually match!
                            var syncedItems = list.Where(x =>
                                !string.IsNullOrWhiteSpace(x.SyncedLyrics) &&
                                IsTitleMatch(x.TrackName ?? "", cleanTitle) &&
                                IsArtistMatch(x.ArtistName ?? "", artist, cleanArtist)
                            ).ToList();

                            if (syncedItems.Count > 0)
                            {
                                double targetSec = duration.TotalSeconds;
                                var best = syncedItems
                                    .OrderBy(x => x.TrackName?.Contains("Instrumental", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0)
                                    .ThenBy(x => string.Equals(CleanTrackTitle(x.TrackName ?? ""), cleanTitle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                    .ThenBy(x => duration > TimeSpan.Zero ? Math.Abs(x.Duration - targetSec) : 0)
                                    .First();

                                result = ParseLrclibResponse(best, duration);
                                break;
                            }

                            if (instrumentalFallback == null)
                            {
                                instrumentalFallback = list.FirstOrDefault(x =>
                                    x.Instrumental &&
                                    IsTitleMatch(x.TrackName ?? "", cleanTitle) &&
                                    IsArtistMatch(x.ArtistName ?? "", artist, cleanArtist)
                                );
                            }
                            if (plainFallback == null)
                            {
                                plainFallback = list.FirstOrDefault(x =>
                                    !string.IsNullOrWhiteSpace(x.PlainLyrics) &&
                                    IsTitleMatch(x.TrackName ?? "", cleanTitle) &&
                                    IsArtistMatch(x.ArtistName ?? "", artist, cleanArtist)
                                );
                            }
                        }
                    }
                }
            }

            // 4. Fallback to QQ Music synced lyrics (great coverage for Vietnamese, Asian, and international songs)
            if (result == null)
            {
                result = await FetchQqMusicLyricsAsync(cleanTitle, cleanArtist, artist, duration);
                if (result == null && (noDiaTitle != cleanTitle || noDiaArtist != cleanArtist))
                {
                    result = await FetchQqMusicLyricsAsync(noDiaTitle, noDiaArtist, artist, duration);
                }
            }

            // 5. Fallback to Kugou synced lyrics if no synced lyrics found yet
            if (result == null)
            {
                result = await FetchKugouLyricsAsync(cleanTitle, cleanArtist, artist, duration);
                if (result == null && (noDiaTitle != cleanTitle || noDiaArtist != cleanArtist))
                {
                    result = await FetchKugouLyricsAsync(noDiaTitle, noDiaArtist, artist, duration);
                }
            }

            // 6. Fallback to confirmed instrumental only (never fake linear timestamps for plain text)
            if (result == null && instrumentalFallback != null && instrumentalFallback.Instrumental)
            {
                result = ParseLrclibResponse(instrumentalFallback, duration);
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"Lyrics fetch failed for '{artist} - {title}': {ex.Message}");
        }

        if (result != null)
        {
            lock (CacheLock)
            {
                Cache[cacheKey] = result;
            }
        }

        return result;
    }

    private static async Task<SongLyrics?> FetchQqMusicLyricsAsync(string title, string artist, string fullArtist, TimeSpan duration)
    {
        try
        {
            string query = $"{title} {artist}".Trim();
            if (string.IsNullOrWhiteSpace(query)) return null;

            string searchUrl = $"https://c.y.qq.com/soso/fcgi-bin/client_search_cp?p=1&n=5&w={Uri.EscapeDataString(query)}&format=json";
            var searchResp = await HttpGetWithRetryAsync(searchUrl, 1);
            if (searchResp == null || !searchResp.IsSuccessStatusCode) return null;

            var searchData = await searchResp.Content.ReadFromJsonAsync<QqSearchResponse>();
            var songs = searchData?.Data?.Song?.List;
            if (songs == null || songs.Count == 0) return null;

            double targetSec = duration.TotalSeconds;

            foreach (var song in songs)
            {
                if (string.IsNullOrEmpty(song.SongMid)) continue;
                if (!IsTitleMatch(song.SongName ?? "", title)) continue;

                string candArtist = song.Singer != null && song.Singer.Count > 0
                    ? string.Join(", ", song.Singer.Select(s => s.Name ?? ""))
                    : "";

                if (!IsArtistMatch(candArtist, fullArtist, artist)) continue;

                if (duration > TimeSpan.Zero && song.Interval > 0)
                {
                    if (Math.Abs(song.Interval - targetSec) > 15) continue;
                }

                string lyricUrl = $"https://c.y.qq.com/lyric/fcgi-bin/fcg_query_lyric_new.fcg?songmid={song.SongMid}&format=json&nobase64=1";
                var req = new HttpRequestMessage(HttpMethod.Get, lyricUrl);
                req.Headers.Referrer = new Uri("https://y.qq.com");
                var lyricResp = await Http.SendAsync(req);
                if (!lyricResp.IsSuccessStatusCode) continue;

                var lyricData = await lyricResp.Content.ReadFromJsonAsync<QqLyricResponse>();
                string? rawLrc = lyricData?.Lyric;
                if (string.IsNullOrWhiteSpace(rawLrc)) continue;

                rawLrc = System.Net.WebUtility.HtmlDecode(rawLrc);
                var lines = ParseLrc(rawLrc);
                if (lines.Count == 0) continue;

                // Clean metadata / intro header lines
                lines.RemoveAll(line =>
                    (line.Time < TimeSpan.FromSeconds(5) && (line.Text.Contains(title, StringComparison.OrdinalIgnoreCase) || line.Text.Contains(artist, StringComparison.OrdinalIgnoreCase))) ||
                    line.Text.StartsWith("Lyrics by", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Composed by", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Written by", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Arranged by", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Produced by", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Nhạc sĩ", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Sáng tác", StringComparison.OrdinalIgnoreCase) ||
                    line.Text.StartsWith("Lời:", StringComparison.OrdinalIgnoreCase));

                if (lines.Count > 0)
                {
                    return new SongLyrics
                    {
                        Title = title,
                        Artist = artist,
                        IsInstrumental = false,
                        Lines = lines
                    };
                }
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"[LyricsService] QQ Music fetch failed for '{artist} - {title}': {ex.Message}");
        }

        return null;
    }

    private static async Task<SongLyrics?> FetchKugouLyricsAsync(string title, string artist, string fullArtist, TimeSpan duration)
    {
        try
        {
            string qTitle = Regex.Replace(title, @"[?!\(\)\[\]•·\-_]", " ").Trim();
            string qArtist = Regex.Replace(artist, @"[?!\(\)\[\]•·\-_]", " ").Trim();
            string query = $"{qTitle} {qArtist}".Trim();
            if (string.IsNullOrWhiteSpace(query)) return null;

            string searchUrl = $"http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword={Uri.EscapeDataString(query)}&page=1&pagesize=5";
            var searchResp = await HttpGetWithRetryAsync(searchUrl, 1);
            if (searchResp == null || !searchResp.IsSuccessStatusCode) return null;

            var searchData = await searchResp.Content.ReadFromJsonAsync<KugouSearchResponse>();
            var songs = searchData?.Data?.Info;
            if (songs == null || songs.Count == 0) return null;

            foreach (var song in songs)
            {
                if (string.IsNullOrEmpty(song.Hash)) continue;
                if (!IsTitleMatch(song.SongName ?? "", title)) continue;
                if (!IsArtistMatch(song.SingerName ?? "", fullArtist, artist)) continue;
                if (duration > TimeSpan.Zero && song.Duration > 0)
                {
                    if (Math.Abs(song.Duration - duration.TotalSeconds) > 15) continue;
                }

                string candUrl = $"http://krcs.kugou.com/search?ver=1&man=yes&client=mobi&keyword=&duration=&hash={song.Hash}";
                var candResp = await HttpGetWithRetryAsync(candUrl, 1);
                if (candResp == null || !candResp.IsSuccessStatusCode) continue;

                var candData = await candResp.Content.ReadFromJsonAsync<KugouCandidatesResponse>();
                var candidates = candData?.Candidates;
                if (candidates == null || candidates.Count == 0) continue;

                var cand = candidates[0];
                string cid = cand.Id?.ToString() ?? "";
                if (string.IsNullOrEmpty(cand.AccessKey) || string.IsNullOrEmpty(cid)) continue;

                string dlUrl = $"http://lyrics.kugou.com/download?ver=1&client=pc&id={cid}&accesskey={cand.AccessKey}&fmt=lrc&charset=utf8";
                var dlResp = await HttpGetWithRetryAsync(dlUrl, 1);
                if (dlResp == null || !dlResp.IsSuccessStatusCode) continue;

                var dlData = await dlResp.Content.ReadFromJsonAsync<KugouDownloadResponse>();
                if (string.IsNullOrEmpty(dlData?.Content)) continue;

                byte[] bytes = Convert.FromBase64String(dlData.Content);
                string lrc = System.Text.Encoding.UTF8.GetString(bytes);

                var lines = ParseLrc(lrc);
                if (lines.Count > 0)
                {
                    // Clean credit / intro header lines so intro is recognized as music "♪"
                    lines.RemoveAll(line =>
                        (line.Time < TimeSpan.FromSeconds(5) && (line.Text.Contains(title, StringComparison.OrdinalIgnoreCase) || line.Text.Contains(artist, StringComparison.OrdinalIgnoreCase))) ||
                        line.Text.StartsWith("Lyrics by", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Composed by", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Written by", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Arranged by", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Produced by", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Nhạc sĩ", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Sáng tác", StringComparison.OrdinalIgnoreCase) ||
                        line.Text.StartsWith("Lời:", StringComparison.OrdinalIgnoreCase));

                    if (lines.Count > 0)
                    {
                        return new SongLyrics
                        {
                            Title = title,
                            Artist = artist,
                            IsInstrumental = false,
                            Lines = lines
                        };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diag.Log($"[LyricsService] Kugou fetch failed for '{artist} - {title}': {ex.Message}");
        }

        return null;
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

    public static bool IsTitleMatch(string candTitle, string expectedTitle)
    {
        if (string.IsNullOrWhiteSpace(candTitle) || string.IsNullOrWhiteSpace(expectedTitle)) return false;
        string ca = CleanTrackTitle(candTitle);
        string cb = CleanTrackTitle(expectedTitle);
        if (string.Equals(ca, cb, StringComparison.OrdinalIgnoreCase)) return true;

        string na = RemoveDiacritics(ca).Trim();
        string nb = RemoveDiacritics(cb).Trim();
        if (string.Equals(na, nb, StringComparison.OrdinalIgnoreCase)) return true;

        string sa = Regex.Replace(na, @"[^\w\s]", "").Trim();
        string sb = Regex.Replace(nb, @"[^\w\s]", "").Trim();
        if (string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase)) return true;

        if (sa.Length > 3 && sb.Length > 3)
        {
            if (sa.Contains(sb, StringComparison.OrdinalIgnoreCase) || sb.Contains(sa, StringComparison.OrdinalIgnoreCase))
            {
                double ratio = (double)Math.Min(sa.Length, sb.Length) / Math.Max(sa.Length, sb.Length);
                if (ratio >= 0.70) return true;
            }
        }

        return false;
    }

    public static bool IsArtistMatch(string candArtist, string expectedFullArtist, string expectedCleanArtist)
    {
        if (string.IsNullOrWhiteSpace(candArtist)) return true;
        string nc = RemoveDiacritics(CleanArtistName(candArtist)).Trim();
        string nClean = RemoveDiacritics(expectedCleanArtist).Trim();
        string nFull = RemoveDiacritics(expectedFullArtist).Trim();

        if (nc.Length == 0) return true;
        if (nClean.Contains(nc, StringComparison.OrdinalIgnoreCase) || nc.Contains(nClean, StringComparison.OrdinalIgnoreCase))
            return true;
        if (nFull.Contains(nc, StringComparison.OrdinalIgnoreCase) || nc.Contains(nFull, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
