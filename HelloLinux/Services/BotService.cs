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
        private long _botId;

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
                AllowedUpdates = new [] { UpdateType.Message, UpdateType.MyChatMember, UpdateType.MessageReaction, UpdateType.ChannelPost } 
            };

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandlePollingErrorAsync,
                receiverOptions: receiverOptions,
                cancellationToken: CancellationToken.None
            );

            var me = await _botClient.GetMe();
            _botId = me.Id;
            Console.WriteLine($"Start listening for @{me.Username}");
        }

        public TelegramBotClient GetClient() => _botClient;

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            // Handle Message Reactions
            if (update.Type == UpdateType.MessageReaction && update.MessageReaction != null)
            {
                var reaction = update.MessageReaction;
                var g = _storageService.GetGroup(reaction.Chat.Id);
                g.TotalReactions++;
                _storageService.UpdateGroup(g);
                return;
            }

            // Handle Bot Added to Group or Channel
            if (update.Type == UpdateType.MyChatMember && update.MyChatMember != null)
            {
                var myChatMember = update.MyChatMember;
                
                // Check if bot is added/promoted in a Group OR Channel
                // In Channels, the bot is usually added as Administrator immediately.
                if (myChatMember.NewChatMember.Status == ChatMemberStatus.Administrator || 
                    myChatMember.NewChatMember.Status == ChatMemberStatus.Member)
                {
                    // Bot was added or promoted
                    // We use a try-catch block to prevent the bot from crashing if it lacks permission to send messages immediately
                    try 
                    {
                        await botClient.SendMessage(
                            myChatMember.Chat.Id, 
                            "السلام عليكم! 🤖\nأنا بوت الورد اليومي للقرآن الكريم.\n\nللبدء، يجب على المشرف إعداد البوت باستخدام الأمر:\n/configure", 
                            cancellationToken: cancellationToken);
                    }
                    catch
                    {
                        // Silent failure if we can't send the welcome message (e.g. restrictions)
                    }
                }
                return;
            }

            var message = update.Message ?? update.ChannelPost;
            if (message is not { } msg)
                return;
            if (msg.Text is not { } messageText)
                return;

            var chatId = msg.Chat.Id;
            var group = _storageService.GetGroup(chatId);

            // Only allow admins to configure
            
            if (messageText.StartsWith("/start"))
            {
                await botClient.SendMessage(chatId, "مرحباً! استخدم الأمر /configure لإعداد أوقات الصلاة لهذه المجموعة.", cancellationToken: cancellationToken);
                return;
            }

            // Super Admin Commands
            if (messageText.StartsWith("/see") || messageText.StartsWith("/stats") || messageText.StartsWith("/list"))
            {
                var username = message.From?.Username;
                if (username != "djstackks" && username != "moloko420")
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
                        await botClient.SendDocument(chatId, new InputFileStream(stream, "groups.json"), caption: "Here is the groups configuration file.", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "No groups.json file found.", cancellationToken: cancellationToken);
                    }
                    return;
                }

                if (messageText.StartsWith("/stats"))
                {
                    var groups = _storageService.GetGroups();
                    int totalGroups = groups.Count;
                    int activeGroups = 0;
                    long totalMessages = 0;
                    
                    foreach (var g in groups)
                    {
                        if (g.IsActive) activeGroups++;
                        totalMessages += g.MessagesSentCount;
                        
                        try 
                        {
                            // Refresh metadata
                            var chat = await botClient.GetChat(g.ChatId, cancellationToken);
                            g.GroupName = chat.Title ?? "Unknown";
                            g.GroupLink = chat.Username != null ? $"https://t.me/{chat.Username}" : "";
                            g.MemberCount = await botClient.GetChatMemberCount(g.ChatId, cancellationToken);
                        }
                        catch 
                        {
                            // Ignore errors (e.g. bot kicked)
                        }
                    }
                    _storageService.SaveGroups(); // Save updated member counts

                    string statsMsg = $"📊 **إحصائيات البوت**\n\n" +
                                      $"إجمالي المجموعات: {totalGroups}\n" +
                                      $"المجموعات النشطة: {activeGroups}\n" +
                                      $"إجمالي الرسائل المرسلة: {totalMessages}\n";
                                      
                    await botClient.SendMessage(chatId, statsMsg, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    return;
                }

                if (messageText.StartsWith("/list"))
                {
                    var groups = _storageService.GetGroups();
                    var report = new System.Text.StringBuilder();
                    report.AppendLine("📋 **Groups Report**\n");

                    foreach (var g in groups)
                    {
                        string subDate = g.SubscriptionDate == DateTime.MinValue ? "N/A" : g.SubscriptionDate.ToString("yyyy-MM-dd");
                        string link = string.IsNullOrEmpty(g.GroupLink) ? "No Link" : g.GroupLink;
                        string admin = string.IsNullOrEmpty(g.AdminUsername) ? $"ID: {g.AdminId}" : $"@{g.AdminUsername}";

                        report.AppendLine($"🔹 **{g.GroupName}**");
                        report.AppendLine($"   🔗 Link: {link}");
                        report.AppendLine($"   👥 Members: {g.MemberCount}");
                        report.AppendLine($"   📅 Sub Date: {subDate}");
                        report.AppendLine($"   📍 Location: {g.City}, {g.Country}");
                        report.AppendLine($"   👤 Admin: {admin}");
                        report.AppendLine($"   📨 Msgs Sent: {g.MessagesSentCount}");
                        report.AppendLine($"   👀 Views: N/A"); // Views not available for groups via API
                        report.AppendLine($"   ❤️ Reactions: {g.TotalReactions}");
                        report.AppendLine("-----------------------------------");
                    }

                    string finalMsg = report.ToString();
                    
                    if (finalMsg.Length > 4000)
                    {
                        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(finalMsg));
                        await botClient.SendDocument(chatId, new InputFileStream(stream, "groups_report.txt"), caption: "Groups Report (Too long for message)", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, finalMsg, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    }
                    return;
                }
            }

            if (messageText.StartsWith("/configure"))
            {
                long userId = message.From?.Id ?? 0;
                if (!await IsAdminAsync(botClient, chatId, userId))
                {
                    await botClient.SendMessage(chatId, "عذراً، يمكن للمشرفين فقط إعداد البوت.", cancellationToken: cancellationToken);
                    return;
                }

                // Capture group metadata
                group.GroupName = message.Chat.Title ?? "Unknown";
                group.GroupLink = message.Chat.Username != null ? $"https://t.me/{message.Chat.Username}" : "";
                
                // For Channels, From is null.
                if (message.From != null)
                {
                    group.AdminUsername = message.From.Username ?? "";
                    group.AdminId = userId;
                }
                else
                {
                    // Channel Post: We don't have a specific admin user ID, but we know it's an admin action.
                    if (group.AdminId == 0) group.AdminId = 0; 
                }
                
                _storageService.UpdateGroup(group);

                _configState[chatId] = "WAITING_CITY";
                await botClient.SendMessage(chatId, "الرجاء الرد على هذه الرسالة باسم المدينة (يفضل بالإنجليزية للدقة) لحساب أوقات الصلاة:", cancellationToken: cancellationToken);
                return;
            }

            if (_configState.ContainsKey(chatId))
            {
                long userId = message.From?.Id ?? 0;
                // Ensure only admin can continue the configuration
                if (!await IsAdminAsync(botClient, chatId, userId))
                {
                    return;
                }

                // Enforce reply to bot
                if (message.ReplyToMessage == null)
                {
                    return;
                }
                
                // If From is present (Group/Private), ensure it matches Bot ID
                // In Channels, ReplyToMessage.From is the Bot if replying to the Bot's message.
                if (message.ReplyToMessage.From != null && message.ReplyToMessage.From.Id != _botId)
                {
                   return;
                }

                string state = _configState[chatId];
                if (state == "WAITING_CITY")
                {
                    group.City = messageText.Trim();
                    _storageService.UpdateGroup(group);
                    _configState[chatId] = "WAITING_COUNTRY";
                    await botClient.SendMessage(chatId, "ممتاز! الآن الرجاء الرد على هذه الرسالة باسم الدولة (يفضل بالإنجليزية):", cancellationToken: cancellationToken);
                }
                else if (state == "WAITING_COUNTRY")
                {
                    group.Country = messageText.Trim();
                    
                    // Verify
                    var times = await _prayerTimeService.GetPrayerTimesAsync(group.City, group.Country);
                    if (times != null)
                    {
                        group.IsActive = true;
                        group.SubscriptionDate = DateTime.Now; // Set subscription date
                        _storageService.UpdateGroup(group);
                        _configState.Remove(chatId);
                        
                        string successMsg = $"تم حفظ الإعدادات! أوقات الصلاة لمدينة {group.City}, {group.Country}:\n";
                        foreach(var t in times) successMsg += $"{t.Key}: {t.Value}\n";
                        successMsg += "\nسيقوم البوت بإرسال صفحات القرآن في هذه الأوقات إن شاء الله.";
                        
                        await botClient.SendMessage(chatId, successMsg, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await botClient.SendMessage(chatId, "عذراً، لم يتم العثور على المدينة أو الدولة المحددة. الرجاء التأكد من صحة الإملاء (يفضل باللغة الإنجليزية) والمحاولة مرة أخرى باستخدام /configure.", cancellationToken: cancellationToken);
                        _configState.Remove(chatId);
                    }
                }
            }
        }

        private async Task<bool> IsAdminAsync(ITelegramBotClient botClient, long chatId, long userId)
        {
            // Check for Anonymous Admin (GroupAnonymousBot)
            if (userId == 1087968824) return true;
            
            // Channel Post (User ID is 0 or null source) - Only admins can post in channels
            if (userId == 0) return true;

            try
            {
                var chat = await botClient.GetChat(chatId);
                if (chat.Type == ChatType.Private) return true;

                // 1. Try GetChatMember
                try 
                {
                    var member = await botClient.GetChatMember(chatId, userId);
                    if (member.Status == ChatMemberStatus.Administrator || member.Status == ChatMemberStatus.Creator)
                        return true;
                }
                catch { /* Ignore and try fallback */ }

                // 2. Fallback to GetChatAdministrators (more reliable in some cases)
                var admins = await botClient.GetChatAdministrators(chatId);
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
