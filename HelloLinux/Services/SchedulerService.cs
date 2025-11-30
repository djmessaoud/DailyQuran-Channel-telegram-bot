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

        private static readonly Dictionary<string, string> ArabicPrayerNames = new Dictionary<string, string>
        {
            { "Fajr", "الفجر" },
            { "Dhuhr", "الظهر" },
            { "Asr", "العصر" },
            { "Maghrib", "المغرب" },
            { "Isha", "العشاء" }
        };

        private const string KhatmaDua = @"الحمد لله اللَّهُمَّ ارْحَمْنِي بالقُرْءَانِ وَاجْعَلهُ لِي إِمَاماً وَنُوراً وَهُدًى وَرَحْمَةً ۞

اللَّهُمَّ ذَكِّرْنِي مِنْهُ مَانَسِيتُ وَعَلِّمْنِي مِنْهُ مَاجَهِلْتُ وَارْزُقْنِي تِلاَوَتَهُ آنَاءَ اللَّيْلِ وَأَطْرَافَ النَّهَارِ وَاجْعَلْهُ لِي حُجَّةً يَارَبَّ العَالَمِينَ ۞

اللَّهُمَّ أَصْلِحْ لِي دِينِي الَّذِي هُوَ عِصْمَةُ أَمْرِي وَأَصْلِحْ لِي دُنْيَايَ الَّتِي فِيهَا مَعَاشِي وَأَصْلِحْ لِي آخِرَتِي الَّتِي فِيهَا مَعَادِي وَاجْعَلِ الحَيَاةَ زِيَادَةً لِي فِي كُلِّ خَيْرٍ وَاجْعَلِ المَوْتَ رَاحَةً لِي مِنْ كُلِّ شَرٍّ ۞

اللَّهُمَّ اجْعَلْ خَيْرَ عُمْرِي آخِرَهُ وَخَيْرَ عَمَلِي خَوَاتِمَهُ وَخَيْرَ أَيَّامِي يَوْمَ أَلْقَاكَ فِيهِ ۞

اللَّهُمَّ إِنِّي أَسْأَلُكَ عِيشَةً هَنِيَّةً وَمِيتَةً سَوِيَّةً وَمَرَدًّا غَيْرَ مُخْزٍ وَلاَ فَاضِحٍ ۞

اللَّهُمَّ إِنِّي أَسْأَلُكَ خَيْرَ المَسْأَلةِ وَخَيْرَ الدُّعَاءِ وَخَيْرَ النَّجَاحِ وَخَيْرَ العِلْمِ وَخَيْرَ العَمَلِ وَخَيْرَ  الثَّوَابِ وَخَيْرَ الحَيَاةِ وَخيْرَ المَمَاتِ وَثَبِّتْنِي وَثَقِّلْ مَوَازِينِي وَحَقِّقْ إِيمَانِي وَارْفَعْ دَرَجَتِي وَتَقَبَّلْ صَلاَتِي وَاغْفِرْ خَطِيئَاتِي وَأَسْأَلُكَ العُلَا مِنَ الجَنَّةِ ۞

اللَّهُمَّ إِنِّي أَسْأَلُكَ مُوجِبَاتِ رَحْمَتِكَ وَعَزَائِمِ مَغْفِرَتِكَ وَالسَّلاَمَةَ مِنْ كُلِّ إِثْمٍ وَالغَنِيمَةَ مِنْ كُلِّ بِرٍّ وَالفَوْزَ بِالجَنَّةِ وَالنَّجَاةَ مِنَ النَّارِ ۞

اللَّهُمَّ أَحْسِنْ عَاقِبَتَنَا فِي الأُمُورِ كُلِّهَا وَأجِرْنَا مِنْ خِزْيِ الدُّنْيَا وَعَذَابِ الآخِرَةِ ۞

اللَّهُمَّ اقْسِمْ لَنَا مِنْ خَشْيَتِكَ مَاتَحُولُ بِهِ بَيْنَنَا وَبَيْنَ مَعْصِيَتِكَ وَمِنْ طَاعَتِكَ مَاتُبَلِّغُنَا بِهَا جَنَّتَكَ وَمِنَ اليَقِينِ مَاتُهَوِّنُ بِهِ عَلَيْنَا مَصَائِبَ الدُّنْيَا وَمَتِّعْنَا بِأَسْمَاعِنَا وَأَبْصَارِنَا وَقُوَّتِنَا مَاأَحْيَيْتَنَا وَاجْعَلْهُ الوَارِثَ مِنَّا وَاجْعَلْ ثَأْرَنَا عَلَى مَنْ ظَلَمَنَا وَانْصُرْنَا عَلَى مَنْ عَادَانَا وَلاَ تجْعَلْ مُصِيبَتَنَا فِي دِينِنَا وَلاَ تَجْعَلِ الدُّنْيَا أَكْبَرَ هَمِّنَا وَلَا مَبْلَغَ عِلْمِنَا وَلاَ تُسَلِّطْ عَلَيْنَا مَنْ لَا يَرْحَمُنَا ۞

اللَّهُمَّ لَا تَدَعْ لَنَا ذَنْبًا إِلَّا غَفَرْتَهُ وَلَا هَمَّا إِلَّا فَرَّجْتَهُ وَلَا دَيْنًا إِلَّا قَضَيْتَهُ وَلَا حَاجَةً مِنْ حَوَائِجِ الدُّنْيَا وَالآخِرَةِ إِلَّا قَضَيْتَهَا يَاأَرْحَمَ الرَّاحِمِينَ ۞

رَبَّنَا آتِنَا فِي الدُّنْيَا حَسَنَةً وَفِي الآخِرَةِ حَسَنَةً وَقِنَا عَذَابَ النَّارِ وَصَلَّى اللهُ عَلَى سَيِّدِنَا وَنَبِيِّنَا مُحَمَّدٍ وَعَلَى آلِهِ وَأَصْحَابِهِ الأَخْيَارِ وَسَلَّمَ تَسْلِيمًا كَثِيراً.";

        private async Task SendWirdAsync(GroupConfig group, string prayerName)
        {
            var bot = _botService.GetClient();

            // Send Adkhar if applicable
            if (prayerName == "Fajr")
            {
                await SendAdkharImageAsync(bot, group.ChatId, "morning");
            }
            else if (prayerName == "Asr")
            {
                await SendAdkharImageAsync(bot, group.ChatId, "evening");
            }

            var media = new IAlbumInputMedia[PagesPerPrayer];
            bool isKhatma = false;
            
            for (int i = 0; i < PagesPerPrayer; i++)
            {
                int pageNum = group.CurrentPage + i;
                if (pageNum > NumberPages) pageNum = 1; // Should not happen in loop if we handle reset correctly, but safety check

                // Check for Khtama (last page)
                if (pageNum == NumberPages)
                {
                    isKhatma = true;
                }

                var inputMedia = new InputMediaPhoto(InputFile.FromUri($"{BaseUrl}{pageNum}.png"));
                if (i == 0)
                {
                    string arabicPrayer = ArabicPrayerNames.ContainsKey(prayerName) ? ArabicPrayerNames[prayerName] : prayerName;
                    inputMedia.Caption = $"ورد صلاة {arabicPrayer} 📖";
                }
                media[i] = inputMedia;
            }

            try
            {
                await bot.SendMediaGroupAsync(group.ChatId, media);
                
                if (isKhatma)
                {
                    await bot.SendTextMessageAsync(group.ChatId, KhatmaDua);
                }

                // Update group state
                group.CurrentPage += PagesPerPrayer;
                if (group.CurrentPage > NumberPages) group.CurrentPage = 1; // Reset to 1
                group.MessagesSentCount++;
                
                _storageService.UpdateGroup(group);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to send to {group.ChatId}: {ex.Message}");
            }
        }

        private async Task SendAdkharImageAsync(ITelegramBotClient bot, long chatId, string imageName)
        {
            string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
            string[] extensions = { ".jpg", ".jpeg", ".png" };
            string filePath = null;

            foreach (var ext in extensions)
            {
                var path = Path.Combine(assetsDir, imageName + ext);
                if (System.IO.File.Exists(path))
                {
                    filePath = path;
                    break;
                }
            }

            if (filePath != null)
            {
                try
                {
                    await using var stream = System.IO.File.OpenRead(filePath);
                    string caption = imageName == "morning" ? "أذكار الصباح ☀️" : "أذكار المساء 🌙";
                    await bot.SendPhotoAsync(chatId, new InputFileStream(stream, Path.GetFileName(filePath)), caption: caption);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to send adkhar image to {chatId}: {ex.Message}");
                }
            }
        }
    }
}
