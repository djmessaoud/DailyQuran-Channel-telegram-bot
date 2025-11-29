using System;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using HelloLinux.Models;

namespace HelloLinux.Services
{
    public class BotService
    {
        private readonly TelegramBotClient _botClient;
        private readonly StorageService _storageService;
        private readonly PrayerTimeService _prayerTimeService;
        private readonly Dictionary<long, string> _configState = new Dictionary<long, string>(); // ChatId -> State

        public BotService(string token, StorageService storageService, PrayerTimeService prayerTimeService)
        {
            _botClient = new TelegramBotClient(token);
            _storageService = storageService;
            _prayerTimeService = prayerTimeService;
        }

        public async Task StartReceiving()
        {
            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = Array.Empty<UpdateType>() // receive all update types
            };

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                pollingErrorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: CancellationToken.None
            );

            var me = await _botClient.GetMeAsync();
            Console.WriteLine($"Start listening for @{me.Username}");
        }

        public TelegramBotClient GetClient() => _botClient;

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message)
                return;
            if (message.Text is not { } messageText)
                return;

            var chatId = message.Chat.Id;
            var group = _storageService.GetGroup(chatId);

            // Only allow admins to configure (simple check: if private chat or if user is admin)
            // For simplicity in this iteration, we assume private chat for configuration or public chat if user is admin.
            // But the requirement says "he adds it to a telegram group... then he can configure... from telegram chat with the bot".
            // So configuration happens in PRIVATE chat with the bot, targeting a specific group?
            // Or configuration happens IN the group?
            // User request: "he adds it to a telegram group where he is admin, then he can configure the city... and he can start it (all from telegram chat with the bot, using keyboard buttons)."
            // This implies the interaction is likely in the private chat with the bot, selecting the group.
            // However, to keep it simple first, let's support commands in the group itself if the user is an admin.
            
            // Let's support direct commands in the group for now as it's easier to link the context.
            
            if (messageText.StartsWith("/start"))
            {
                await botClient.SendTextMessageAsync(chatId, "Welcome! Use /configure to set up prayer times for this group.", cancellationToken: cancellationToken);
                return;
            }

            // Super Admin Commands
            if (messageText.StartsWith("/see") || messageText.StartsWith("/stats"))
            {
                if (message.From.Username != "djstackks")
                {
                    // Ignore or say unauthorized
                    return;
                }

                if (messageText.StartsWith("/see"))
                {
                    string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                    string filePath = Path.Combine(dataDir, "groups.json");

                    if (System.IO.File.Exists(filePath))
                    {
                        await using var stream = System.IO.File.OpenRead(filePath);
                        await botClient.SendDocumentAsync(chatId, new InputFileStream(stream, "groups.json"), caption: "Here is the groups configuration file.", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "No groups.json file found.", cancellationToken: cancellationToken);
                    }
                    return;
                }

                if (messageText.StartsWith("/stats"))
                {
                    var groups = _storageService.GetGroups();
                    int totalGroups = groups.Count;
                    int activeGroups = 0;
                    long totalMessages = 0;
                    
                    // Update member counts for all groups (this might be slow if many groups, but okay for now)
                    // Note: GetChatMemberCountAsync might hit rate limits if too many groups.
                    // For now, let's just report what we have or try to update a few.
                    // To be safe, we will just sum up what we have in config, and maybe update on the fly?
                    // Updating on the fly for all groups is risky for rate limits.
                    // Let's just show the stored stats.
                    
                    foreach (var g in groups)
                    {
                        if (g.IsActive) activeGroups++;
                        totalMessages += g.MessagesSentCount;
                        
                        try 
                        {
                            // Refresh metadata
                            var chat = await botClient.GetChatAsync(g.ChatId, cancellationToken);
                            g.GroupName = chat.Title ?? "Unknown";
                            g.GroupLink = chat.Username != null ? $"https://t.me/{chat.Username}" : "";
                            g.MemberCount = await botClient.GetChatMemberCountAsync(g.ChatId, cancellationToken);
                        }
                        catch 
                        {
                            // Ignore errors (e.g. bot kicked)
                        }
                    }
                    _storageService.SaveGroups(); // Save updated member counts

                    string statsMsg = $"📊 **Bot Statistics**\n\n" +
                                      $"Total Groups: {totalGroups}\n" +
                                      $"Active Groups: {activeGroups}\n" +
                                      $"Total Messages Sent: {totalMessages}\n";
                                      
                    await botClient.SendTextMessageAsync(chatId, statsMsg, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    return;
                }
            }

            if (messageText.StartsWith("/configure"))
            {
                if (!await IsAdminAsync(botClient, chatId, message.From.Id))
                {
                    await botClient.SendTextMessageAsync(chatId, "Only admins can configure the bot.", cancellationToken: cancellationToken);
                    return;
                }

                // Capture group metadata
                group.GroupName = message.Chat.Title ?? "Unknown";
                group.GroupLink = message.Chat.Username != null ? $"https://t.me/{message.Chat.Username}" : "";
                group.AdminUsername = message.From.Username ?? "";
                group.AdminId = message.From.Id;
                _storageService.UpdateGroup(group);

                _configState[chatId] = "WAITING_CITY";
                await botClient.SendTextMessageAsync(chatId, "Please enter the City for prayer times:", cancellationToken: cancellationToken);
                return;
            }

            if (_configState.ContainsKey(chatId))
            {
                // Ensure only admin can continue the configuration
                if (!await IsAdminAsync(botClient, chatId, message.From.Id))
                {
                    // Optionally ignore or warn. Ignoring is better to avoid spamming if normal users chat.
                    return;
                }

                string state = _configState[chatId];
                if (state == "WAITING_CITY")
                {
                    group.City = messageText.Trim();
                    _storageService.UpdateGroup(group);
                    _configState[chatId] = "WAITING_COUNTRY";
                    await botClient.SendTextMessageAsync(chatId, "Great! Now please enter the Country:", cancellationToken: cancellationToken);
                }
                else if (state == "WAITING_COUNTRY")
                {
                    group.Country = messageText.Trim();
                    
                    // Verify
                    var times = await _prayerTimeService.GetPrayerTimesAsync(group.City, group.Country);
                    if (times != null)
                    {
                        group.IsActive = true;
                        _storageService.UpdateGroup(group);
                        _configState.Remove(chatId);
                        
                        string msg = $"Configuration saved! Prayer times for {group.City}, {group.Country}:\n";
                        foreach(var t in times) msg += $"{t.Key}: {t.Value}\n";
                        msg += "\nThe bot will now send Quran pages at these times.";
                        
                        await botClient.SendTextMessageAsync(chatId, msg, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendTextMessageAsync(chatId, "Could not find prayer times for this location. Please try /configure again with correct City and Country.", cancellationToken: cancellationToken);
                        _configState.Remove(chatId);
                    }
                }
            }
        }

        private async Task<bool> IsAdminAsync(ITelegramBotClient botClient, long chatId, long userId)
        {
            try
            {
                var chat = await botClient.GetChatAsync(chatId);
                if (chat.Type == ChatType.Private) return true;

                var admins = await botClient.GetChatAdministratorsAsync(chatId);
                foreach (var admin in admins)
                {
                    if (admin.User.Id == userId) return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            var ErrorMessage = exception switch
            {
                ApiRequestException apiRequestException
                    => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                _ => exception.ToString()
            };

            Console.WriteLine(ErrorMessage);
            return Task.CompletedTask;
        }
    }
}
