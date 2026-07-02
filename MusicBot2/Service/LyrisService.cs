using MusicBot2.Helpers;
using MusicBot2.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace MusicBot2.Service
{
    public class LyrisService
    {
        private readonly HttpClient _httpClient;
        private const string API_BASE_URL = "https://lrclib.net/api";

        public LyrisService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MusicBot2/1.0");
        }

        /// <summary>
        /// Search for lyrics by track name and artist name
        /// </summary>
        /// <param name="trackName">The name of the track</param>
        /// <param name="artistName">The name of the artist</param>
        /// <returns>List of matching lyrics results</returns>
        public async Task<List<LyricsResponse>> SearchLyricsAsync(string trackName, string artistName = null)
        {
            try
            {
                var encodedTrack = HttpUtility.UrlEncode(trackName);
                var url = $"{API_BASE_URL}/search?track_name={encodedTrack}";

                if (!string.IsNullOrWhiteSpace(artistName))
                {
                    var encodedArtist = HttpUtility.UrlEncode(artistName);
                    url += $"&artist_name={encodedArtist}";
                }

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                var results = JsonConvert.DeserializeObject<List<LyricsResponse>>(content);

                if (results != null)
                {
                    foreach (var result in results)
                    {
                        if (!string.IsNullOrWhiteSpace(result.plainLyrics) &&
                            JapaneseTextHelper.IsLikelyJapanese(result.plainLyrics))
                        {
                            result.plainLyrics = JapaneseTextHelper.AddFuriganaToLyrics(result.plainLyrics);
                        }
                    }
                }

                return results;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error searching lyrics: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get lyrics by track name, artist name, album name and duration
        /// </summary>
        /// <param name="trackName">The name of the track</param>
        /// <param name="artistName">The name of the artist</param>
        /// <param name="albumName">The name of the album (optional)</param>
        /// <param name="duration">Duration in seconds (optional)</param>
        /// <returns>The best matching lyrics result</returns>
        public async Task<LyricsResponse> GetLyricsAsync(string trackName, string artistName, string albumName = null, double? duration = null)
        {
            try
            {
                var encodedTrack = HttpUtility.UrlEncode(trackName);
                var encodedArtist = HttpUtility.UrlEncode(artistName);

                string url = string.Empty;
                bool useSearchEndpoint = string.IsNullOrEmpty(encodedArtist);

                if(useSearchEndpoint)
                {
                    url = $"{API_BASE_URL}/search?track_name={encodedTrack}";
                }
                else
                {
                    url = $"{API_BASE_URL}/search?track_name={encodedTrack}&artist_name={encodedArtist}";
                }


                if (!string.IsNullOrWhiteSpace(albumName))
                {
                    var encodedAlbum = HttpUtility.UrlEncode(albumName);
                    url += $"&album_name={encodedAlbum}";
                }

                if (duration.HasValue)
                {
                    url += $"&duration={duration.Value}";
                }

                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();

                // Handle different response types based on endpoint used
                if (useSearchEndpoint)
                {
                    // /search endpoint returns an array
                    var results = JsonConvert.DeserializeObject<List<LyricsResponse>>(content);
                    // 優先選擇有同步歌詞的結果，如果沒有則選擇有純文字歌詞的
                    return results?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.syncedLyrics))
                        ?? results?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.plainLyrics))
                        ?? results?.FirstOrDefault();
                }
                else
                {
                    // /get endpoint returns a single object
                    var results = JsonConvert.DeserializeObject<List<LyricsResponse>>(content);
                    // search endpoint 也會返回陣列，同樣優先選擇有同步歌詞的
                    return results?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.syncedLyrics))
                        ?? results?.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.plainLyrics))
                        ?? results?.FirstOrDefault();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting lyrics: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Format plain lyrics for display (truncate if too long)
        /// </summary>
        /// <param name="lyrics">The lyrics to format</param>
        /// <param name="maxLength">Maximum length (default 2000 for Discord embed limit)</param>
        /// <returns>Formatted lyrics string</returns>
        public string FormatLyrics(string lyrics, int maxLength = 2000)
        {
            if (string.IsNullOrWhiteSpace(lyrics))
            {
                return "No lyrics available";
            }

            if (lyrics.Length <= maxLength)
            {
                return lyrics;
            }

            return lyrics.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Parse synced lyrics into timestamped lines
        /// </summary>
        /// <param name="syncedLyrics">The synced lyrics string with timestamps</param>
        /// <returns>List of tuples containing timestamp and lyric line</returns>
        public List<(TimeSpan timestamp, string line)> ParseSyncedLyrics(string syncedLyrics)
        {
            var result = new List<(TimeSpan timestamp, string line)>();

            if (string.IsNullOrWhiteSpace(syncedLyrics))
            {
                return result;
            }

            var lines = syncedLyrics.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Parse format: [mm:ss.xx] lyrics
                if (line.StartsWith("[") && line.Contains("]"))
                {
                    var endIndex = line.IndexOf("]");
                    var timeString = line.Substring(1, endIndex - 1);
                    var lyricLine = line.Substring(endIndex + 1).Trim();

                    try
                    {
                        var parts = timeString.Split(':');
                        if (parts.Length == 2)
                        {
                            var minutes = int.Parse(parts[0]);
                            var secondsParts = parts[1].Split('.');
                            var seconds = int.Parse(secondsParts[0]);
                            var milliseconds = secondsParts.Length > 1 ? int.Parse(secondsParts[1]) * 10 : 0;

                            var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                            result.Add((timestamp, lyricLine));
                        }
                    }
                    catch
                    {
                        // Skip invalid timestamp format
                        continue;
                    }
                }
            }

            return result;
        }
    }
}
