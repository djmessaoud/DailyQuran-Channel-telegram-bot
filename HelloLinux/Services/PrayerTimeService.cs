using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace HelloLinux.Services
{
    public class PrayerTimeService
    {
        private readonly HttpClient _httpClient;

        public PrayerTimeService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<(Dictionary<string, TimeSpan> Times, string TimeZoneId)?> GetPrayerTimesAsync(string city, string country)
        {
            try
            {
                string url = $"http://api.aladhan.com/v1/timingsByCity?city={Uri.EscapeDataString(city)}&country={Uri.EscapeDataString(country)}&method=2"; // Method 2 is ISNA, usually a safe default or make it configurable
                var response = await _httpClient.GetStringAsync(url);

                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.GetProperty("code").GetInt32() == 200)
                {
                    var data = root.GetProperty("data");

                    var timings = data.GetProperty("timings");
                    var result = new Dictionary<string, TimeSpan>();

                    // Map API keys to our internal keys
                    result["Fajr"] = TimeSpan.Parse(timings.GetProperty("Fajr").GetString());
                    result["Dhuhr"] = TimeSpan.Parse(timings.GetProperty("Dhuhr").GetString());
                    result["Asr"] = TimeSpan.Parse(timings.GetProperty("Asr").GetString());
                    result["Maghrib"] = TimeSpan.Parse(timings.GetProperty("Maghrib").GetString());
                    result["Isha"] = TimeSpan.Parse(timings.GetProperty("Isha").GetString());

                    // Extract timezone from API response (e.g., "Asia/Riyadh", "Africa/Cairo")
                    var timeZoneId = data.GetProperty("meta").GetProperty("timezone").GetString() ?? "UTC";

                    return (result, timeZoneId);
                }
                else
                {
                     Console.WriteLine($"API returned non-200 code: {root.GetProperty("code").GetInt32()} for {city}, {country}");
                     return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching prayer times for {city}, {country}: {ex.Message}");
            }

            return null;
        }
    }
}
