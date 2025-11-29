using System;
using System.Threading.Tasks;
using HelloLinux.Services;

namespace HelloLinux
{
    class Program
    {
        // TODO: Replace with your actual bot token
        private static string _botToken = "YOUR_BOT_TOKEN_HERE"; 

        static async Task Main(string[] args)
        {
            Console.WriteLine("Starting DailyQuran Bot...");

            // Check for token in args or env var for better security
            var envToken = Environment.GetEnvironmentVariable("BOT_TOKEN");
            if (!string.IsNullOrEmpty(envToken)) _botToken = envToken;

            if (_botToken == "YOUR_BOT_TOKEN_HERE")
            {
                Console.WriteLine("Please set the bot token in Program.cs or BOT_TOKEN env variable.");
                // We continue for now so the user can see it running, but it will fail to connect
            }

            // Initialize Services
            var storageService = new StorageService();
            var prayerTimeService = new PrayerTimeService();
            var botService = new BotService(_botToken, storageService, prayerTimeService);
            var schedulerService = new SchedulerService(storageService, prayerTimeService, botService);

            // Start Bot
            try 
            {
                await botService.StartReceiving();
                Console.WriteLine("Bot started receiving.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to start bot: {ex.Message}");
            }

            // Start Scheduler
            schedulerService.Start();
            Console.WriteLine("Scheduler started.");

            Console.WriteLine("Press any key to exit...");
            // Keep the app running
            await Task.Delay(-1);
        }
    }
}
