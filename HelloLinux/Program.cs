using HtmlAgilityPack;
using System.Diagnostics;
using System.Globalization;
using Telegram.Bot;
using Telegram.Bot.Types;

// TODO: Add Adkar sending!!!!!!!
namespace HelloLinux
{
    class Program
    {
        //Channel id => POST =>  https://api.telegram.org/botXXX:YYY/sendMessage?chat_id=@ChannelUSERNAME&text=test  => channel id = result > chat > id [JSON]

        static string _botToken = ""; //Bot ID
        static string _channelID = "-1002102410457"; // Channel ID
        private static readonly HttpClient httpClient = new HttpClient();
        private const string baseUrl = "https://quran.ksu.edu.sa/ayat/safahat1/"; // Quran pages base url
        private const int numberPages = 604;
        private static int currentPage = 331;
        private const int pagesPerPrayer = 5;
        private static int currentPrayer = 1;
        private static Dictionary<string, TimeSpan> prayerTimes;
        static TelegramBotClient botClient;
        private const string prayerTimesUrl = "https://www.islamicfinder.org/prayer-widget/2507480/shafi/15/0/18.0/17.0"; //Algiers, Algeria prayer times of today
        private const string linkAdkarMasa2 = "http://alphagyms.tn/masa2.png";
        private const string linkAdkarSabah = "http://alphagyms.tn/sabah.png";
        static async Task Main(string[] args)
        {
            Console.WriteLine("********* Welcome to Daily Quran bot ************");
            Console.WriteLine("========> Starting bot ....");
            Debug.WriteLine($"(start) Current time: {DateTime.Now}");
            Console.WriteLine($"(start) Current time: {DateTime.Now}");
            //Initializations
            botClient = new TelegramBotClient(_botToken);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/58.0.3029.110 Safari/537");
            httpClient.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.5");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("http://www.google.com");


            Console.WriteLine("========> Sending Test message ....");
            try
            {
                await botClient.SendTextMessageAsync(_channelID, $"Hello World! {DateTime.Now.ToString()} ");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
            Console.WriteLine("========> Test message sent successfully ....");
            //   Console.WriteLine("========> Sending Quran pics....");
            //    await SendQuranPagesAsync();
            Console.WriteLine("=======> Getting prayer times for today..");
            await UpdatePrayerTimesAsync();
           // debugMsg("Starting the bot ( نسخة بعد العيد )");

            Console.WriteLine("=======> Scheduling daily tasks..");
            await ScheduleDailyTimer();
            Console.WriteLine("\n \n .. \t Working .. Press any key to cancel");

            //  Console.ReadKey(); //Test on windows only

            // Run the bot forever
            await Task.Delay(-1);

        }
        //Debug msg
        static void debugMsg(string message)
        {
            string msg = $"*** تجربة البوت *** \n {DateTime.Today} أوقات اليوم : \n \n";
            foreach (var (prayer, time) in prayerTimes)
            {
                msg += $"{prayer}: {time} \n";
                Console.WriteLine($"{prayer}: {time}");
            }
            msg += $"\n Debug Message:  {message}";
            botClient.SendTextMessageAsync(_channelID, msg);
        }

        //Send quran pages to the channel 
        private static async Task SendQuranPagesAsync()
        {
            int pagesCounter = 0;
            IAlbumInputMedia[] media;
            if (numberPages - currentPage >= pagesPerPrayer) media = new IAlbumInputMedia[pagesPerPrayer];
            else media = new IAlbumInputMedia[numberPages - currentPage + 1];

            bool ended_khatma = false;
            //Send 5 pages of Quran
            for (int pageNumber = currentPage; pageNumber < currentPage + pagesPerPrayer; pageNumber++)
            {
                if (pageNumber > numberPages)
                {
                    currentPage = 1;
                    ended_khatma = true;
                    break;
                }

                InputMediaPhoto inputOnlineFile = new InputMediaPhoto(InputFile.FromUri($"{baseUrl}{pageNumber}.png"));

                if (pagesCounter == 0) inputOnlineFile.Caption = $"ورد صلاة {PrayerNumberToName(currentPrayer)}  ";
                // if (pageNumber == currentPage) inputOnlineFile.Caption = $"ورد صلاة {PrayerNumberToName(currentPrayer).Result} ❤️ ";

                media[pagesCounter] = inputOnlineFile;
                //   media[(pageNumber-1)%pagesPerPrayer] = inputOnlineFile;
                pagesCounter++;
            }
            dbgConsole($"Sending Quran pages!");
            //Sending the pages and handling the rate limit exception
            try
            {
                await botClient.SendMediaGroupAsync(chatId: _channelID, media: media);
            }
            catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("Too Many Requests"))
            {
                var retryAfter = ex.Parameters?.RetryAfter ?? 5;
                Console.WriteLine($"Rate limited by Telegram API. Retrying after {retryAfter+1} seconds...");
                await Task.Delay(retryAfter+1 * 1000);
                await botClient.SendMediaGroupAsync(chatId: _channelID, media: media);
            }
            // Send Adkar picture if current prayer is 1 or 3
            if (currentPrayer == 1)
            {
                InputFile adkar = InputFile.FromUri(linkAdkarSabah);
                try
                {
                    await botClient.SendPhotoAsync(_channelID, adkar, caption: "أذكار الصباح");
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("Too Many Requests"))
                {
                    var retryAfter = ex.Parameters?.RetryAfter ?? 5;
                    Console.WriteLine($"Rate limited by Telegram API. Retrying after {retryAfter + 1} seconds...");
                    await Task.Delay(retryAfter + 1 * 1000);
                    await botClient.SendPhotoAsync(_channelID, adkar, caption: "أذكار الصباح");
                }

            }
            else if (currentPrayer == 3)
            {
                InputFile adkar = InputFile.FromUri(linkAdkarMasa2);
                try
                {
                    await botClient.SendPhotoAsync(_channelID, adkar,caption: "أذكار المساء");
                }
                catch (Telegram.Bot.Exceptions.ApiRequestException ex) when (ex.Message.Contains("Too Many Requests"))
                {
                    var retryAfter = ex.Parameters?.RetryAfter ?? 5;
                    Console.WriteLine($"Rate limited by Telegram API. Retrying after {retryAfter + 1} seconds...");
                    await Task.Delay(retryAfter + 1 * 1000);
                    await botClient.SendPhotoAsync(_channelID, adkar, caption: "أذكار المساء");
                }

            }

            // If the khatma is ended, send the ending message and the duaa
            if (ended_khatma)
            {
                currentPage = 1;
                currentPrayer = 1;
                await botClient.SendTextMessageAsync(chatId: _channelID, text: "تم بحمد الله و فضله الختم! ✅ ");
                using (var stream = System.IO.File.OpenRead("duaa.png")) //MAKE SURE duaa.png is in the same directory as the program!!
                {
                    var inputMediaPhoto = new InputFileStream(stream, "duaa.png");
                    await botClient.SendPhotoAsync(chatId: _channelID, photo: inputMediaPhoto, caption: "دعاء ختم القرآن الكريم 🤲 ");
                }
                await botClient.SendTextMessageAsync(chatId: _channelID, text: " ☪️ فلنبدأ ختمة جديدة على بركة الله ☪️ ");
                ended_khatma = false;
            } // If khatma is not ended, we update the current page and prayer to the next ones 
            else
            {
                currentPage += pagesPerPrayer;
                if (currentPrayer < 5) currentPrayer++;
                else currentPrayer = 1;
            }
        }


        //Convert prayer number to its name for the caption
        static string PrayerNumberToName(int n)
        {
            switch (n)
            {
                case 1:
                    return "الفجر";
                case 2:
                    return "الظهر";
                case 3:
                    return "العصر";
                case 4:
                    return "المغرب";
                case 5:
                    return "العشاء";
                default:
                    return "(هناك خطأ في البوت، يرجى التواصل مع المطور)";
            }
        }

        //Get the prayer times of today from the website and update the prayerTimes dictionary
        private static async Task UpdatePrayerTimesAsync()
        {
            //Using XPath to get the prayer times from the website, if website changes, the XPath should be updated, however, location changing is not a problem.
            prayerTimes = new Dictionary<string, TimeSpan>();

            HttpResponseMessage response = await httpClient.GetAsync(prayerTimesUrl);
            var html = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            //Getting Fajr node cuz the XPath is different for it
            var FajrNode = doc.DocumentNode.SelectNodes("//div[@class='d-flex flex-direction-row flex-justify-sb pad-top-sm pad-left-sm pad-right-sm  ']");
            //Getting the rest of the prayers
            var nodes = doc.DocumentNode.SelectNodes("//div[@class='d-flex flex-direction-row flex-justify-sb pad-top-sm pad-left-sm pad-right-sm ']");
            nodes.Insert(0, FajrNode[0]); //Fajr Node is the node 0 by default sinced there is only one element of such div class
            foreach (var node in nodes)
            {
                var prayerNameNode = node.SelectSingleNode(".//p[1]"); //Prayer name is the first p element
                var timeNode = node.SelectSingleNode(".//p[3]"); // Time is the third p element

                if (prayerNameNode != null && timeNode != null)
                {
                    string prayerName = prayerNameNode.InnerText.Trim();
                    string time = timeNode.InnerText.Trim();
                    // We only need the 5 prayers, not the sunrise
                    if (prayerName != "Sunrise" && (prayerName == "Fajr" || prayerName == "Dhuhr" || prayerName == "Asr" || prayerName == "Maghrib" || prayerName == "Isha"))
                    {
                        // Parse the time in AM/PM format to TimeSpan
                        if (DateTime.TryParseExact(time, "hh:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedTime))
                        {
                            prayerTimes.Add(prayerName, parsedTime.TimeOfDay);
                        }
                        else
                        {
                            Console.WriteLine($"Failed to parse time: {time}");
                        }
                    }
                }
            }

        }

        //send message to console fnc
       static void dbgConsole(string msg)
        {
            Console.WriteLine(msg);
        }
        // Schedule daily prayer timers 
        private static async Task ScheduleTodayTimers()
        {
            var now = DateTime.Now;
            dbgConsole($"**************************** Scheduling Prayer Timers for today ****************************");
            dbgConsole($"Current time: {now}");
            int i = 1;
            foreach (var (prayerName, time) in prayerTimes)
            {
                
                var prayerTime = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, time.Seconds);
                dbgConsole($" {i}- Prayer: {prayerName}, Time: {prayerTime}");
                if (now > prayerTime)
                {
                    prayerTime = prayerTime.AddDays(1);
                }

                var timer = new System.Timers.Timer()
                {
                    Interval = prayerTime.Subtract(DateTime.Now).TotalMilliseconds,
                    AutoReset = false
                };
                dbgConsole($"Timer {i} set to be triggered after {timer.Interval/1000} seconds || {timer.Interval/(1000*60)} hours");
                //To do after the time has elapsed!
                timer.Elapsed += async (sender, args) =>
                {
                    dbgConsole($"Timer {i} triggered!");
                    dbgConsole($"Sending Quran pages for prayer {prayerName} || {currentPrayer}!");
                    await SendQuranPagesAsync();
                    timer.Stop();
                    timer.Dispose();
                    dbgConsole($"Timer {i} stopped and disposed!");
                };
                timer.Start();
                i++;
            }
            dbgConsole($"******************************** Schedule timers ended!*************************************************************");
        }
        //Schedule the daily timer (00:30) to update the prayer times and reset the counters
        private static async Task ScheduleDailyTimer()
        {
            /*  var now = DateTime.Now;
              Debug.WriteLine($"(schedule)Current time: {now}");
              Console.WriteLine($"(schedule)Current time: {now}");
              foreach (var (prayerName, time) in prayerTimes)
              {
                  var prayerTime = new DateTime(now.Year, now.Month, now.Day, time.Hours, time.Minutes, time.Seconds);
                  if (now > prayerTime)
                  {
                      prayerTime = prayerTime.AddDays(1);
                  }

                  var timer = new System.Timers.Timer()
                  {
                      Interval = prayerTime.Subtract(DateTime.Now).TotalMilliseconds,
                      AutoReset = false
                  };
                  timer.Elapsed += async (sender, args) =>
                  {
                      await SendQuranPagesAsync();
                      timer.Stop();
                      timer.Dispose();
                  };
                  timer.Start();
              }
            I commented this code above because it might be the one causing the pages to be sent multiple times, 
            Now I will try to schedule the tasks in a different way, by scheduling the tasks at the beginning of the day and at 00:30 (Starting directly from Fajr)
            */
            // Schedule task to update prayer times and reset counters at 00:30
            var now = DateTime.Now;
            var resetTime = new DateTime(now.Year, now.Month, now.Day, 0, 30, 0).AddDays(1);
            var resetTimer = new System.Timers.Timer()
            {

                AutoReset = true,
                Interval = resetTime.Subtract(DateTime.Now).TotalMilliseconds
            };
            resetTimer.Elapsed += async (sender, args) =>
            {
                resetTimer.Interval = 24 * 60 * 60 * 1000; // 24 hours
                await UpdatePrayerTimesAsync();
                await ScheduleTodayTimers();
               // No need to do it again since the AutoReset is true await ScheduleDailyTimer();
                // debugMsg("Re-scheduling message!");
                //New CODE TRY
               // resetTimer.Stop();
              //  resetTimer.Dispose();
            };
            resetTimer.Start();
        }
    }
}
