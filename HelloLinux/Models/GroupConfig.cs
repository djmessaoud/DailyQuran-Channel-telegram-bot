using System;

namespace HelloLinux.Models
{
    public class GroupConfig
    {
        public long ChatId { get; set; }
        public string City { get; set; } = string.Empty;
        public string Country { get; set; } = string.Empty;
        public int CurrentPage { get; set; } = 1;
        public int CurrentPrayer { get; set; } = 1; // 1: Fajr, 2: Dhuhr, 3: Asr, 4: Maghrib, 5: Isha
        public bool IsActive { get; set; } = false;
        public DateTime? LastPrayerTime { get; set; } // To track if we already sent for this time
        public DateTime LastUpdatedDate { get; set; } = DateTime.MinValue;
        public Dictionary<string, TimeSpan> TodayPrayerTimes { get; set; } = new Dictionary<string, TimeSpan>();
        public long MessagesSentCount { get; set; } = 0;
        public int MemberCount { get; set; } = 0;
        public string GroupName { get; set; } = string.Empty;
        public string GroupLink { get; set; } = string.Empty;
        public string AdminUsername { get; set; } = string.Empty;
        public long AdminId { get; set; } = 0;
        public DateTime SubscriptionDate { get; set; } = DateTime.MinValue;
        public long TotalReactions { get; set; } = 0;
    }
}
