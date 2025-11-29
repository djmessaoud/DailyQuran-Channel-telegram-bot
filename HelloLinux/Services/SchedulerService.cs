using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;
using HelloLinux.Models;

namespace HelloLinux.Services
{
    public class SchedulerService
    {
        private readonly StorageService _storageService;
        private readonly PrayerTimeService _prayerTimeService;
        private readonly BotService _botService;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private const string BaseUrl = "https://quran.ksu.edu.sa/ayat/safahat1/";
        private const int PagesPerPrayer = 5;
        private const int NumberPages = 604;

        public SchedulerService(StorageService storageService, PrayerTimeService prayerTimeService, BotService botService)
        {
            _storageService = storageService;
            _prayerTimeService = prayerTimeService;
            _botService = botService;
        }

        public void Start()
        {
            Task.Run(async () => await LoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        private async Task LoopAsync(CancellationToken token)
        {
            Console.WriteLine("Scheduler started...");
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var now = DateTime.Now;
                    var groups = _storageService.GetGroups();

                    foreach (var group in groups)
                    {
                        if (!group.IsActive) continue;

                        // Update prayer times if needed (new day)
                        if (group.LastUpdatedDate.Date != now.Date)
                        {
                            var times = await _prayerTimeService.GetPrayerTimesAsync(group.City, group.Country);
                            if (times != null)
                            {
                                group.TodayPrayerTimes = times;
                                group.LastUpdatedDate = now.Date;
                                _storageService.UpdateGroup(group);
                                Console.WriteLine($"Updated prayer times for group {group.ChatId} ({group.City})");
                            }
                        }

                        // Check if we need to send wird
                        foreach (var prayer in group.TodayPrayerTimes)
                        {
                            var prayerTime = now.Date + prayer.Value;
                            
                            // Check if it's time (within last minute) and haven't sent yet
                            // We use a simple logic: if now is past prayer time AND (LastPrayerTime is null OR LastPrayerTime is before this prayer time)
                            // But we need to be careful not to send immediately if we just started the bot and the prayer was 2 hours ago.
                            // So we only send if now is within, say, 5 minutes of the prayer time.
                            
                            if (now >= prayerTime && now < prayerTime.AddMinutes(5))
                            {
                                // Check if we already sent for this specific prayer instance
                                // We can use LastPrayerTime. If LastPrayerTime is close to this prayerTime, we skip.
                                bool alreadySent = group.LastPrayerTime.HasValue && 
                                                   Math.Abs((group.LastPrayerTime.Value - prayerTime).TotalMinutes) < 10;

                                if (!alreadySent)
                                {
                                    Console.WriteLine($"Sending wird for {prayer.Key} to group {group.ChatId}");
                                    await SendWirdAsync(group, prayer.Key);
                                    group.LastPrayerTime = prayerTime;
                                    _storageService.UpdateGroup(group);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Scheduler error: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromMinutes(1), token);
            }
        }

        private async Task SendWirdAsync(GroupConfig group, string prayerName)
        {
            var bot = _botService.GetClient();
            var media = new IAlbumInputMedia[PagesPerPrayer];
            
            for (int i = 0; i < PagesPerPrayer; i++)
            {
                int pageNum = group.CurrentPage + i;
                if (pageNum > NumberPages) pageNum = 1; // Reset or handle khatma

                var inputMedia = new InputMediaPhoto(InputFile.FromUri($"{BaseUrl}{pageNum}.png"));
                if (i == 0) inputMedia.Caption = $"ورد صلاة {prayerName} 📖";
                media[i] = inputMedia;
            }

            try
            {
                await bot.SendMediaGroupAsync(group.ChatId, media);
                
                // Update group state
                group.CurrentPage += PagesPerPrayer;
                if (group.CurrentPage > NumberPages) group.CurrentPage = 1; // Simple loop for now
                group.MessagesSentCount++;
                
                // Update prayer count if needed, but we rely on the loop for next prayer
                _storageService.UpdateGroup(group);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send to {group.ChatId}: {ex.Message}");
            }
        }
    }
}
