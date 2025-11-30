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

        public async Task<Dictionary<string, TimeSpan>?> GetPrayerTimesAsync(string city, string country)
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
                    // Aladhan API might return 200 even if city is approximated. 
                    // However, usually if it completely fails to find it, it might return 404 or 400.
                    // We will trust the code 200 for now, but ensure we return null if code is not 200.
                    
                    var timings = data.GetProperty("timings");
                    var result = new Dictionary<string, TimeSpan>();

                    // Map API keys to our internal keys
                    result["Fajr"] = TimeSpan.Parse(timings.GetProperty("Fajr").GetString());
                    result["Dhuhr"] = TimeSpan.Parse(timings.GetProperty("Dhuhr").GetString());
                    result["Asr"] = TimeSpan.Parse(timings.GetProperty("Asr").GetString());
                    result["Maghrib"] = TimeSpan.Parse(timings.GetProperty("Maghrib").GetString());
                    result["Isha"] = TimeSpan.Parse(timings.GetProperty("Isha").GetString());

                    return result;
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
