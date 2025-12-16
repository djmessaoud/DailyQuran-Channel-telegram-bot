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
using Telegram.Bot.Types.ReplyMarkups;
using HelloLinux.Models;

namespace HelloLinux.Services
{
    public class BotService
    {
        private readonly TelegramBotClient _botClient;
        private readonly StorageService _storageService;
        private readonly PrayerTimeService _prayerTimeService;
        private readonly Dictionary<long, string> _configState = new Dictionary<long, string>(); // ChatId -> State
        private readonly HashSet<long> _superAdminIdsSetup = new HashSet<long>(); // Track which super admins have commands set
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
                AllowedUpdates = new [] { UpdateType.Message, UpdateType.MyChatMember, UpdateType.MessageReaction, UpdateType.ChannelPost, UpdateType.CallbackQuery }
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

            // Set up bot commands menu for all users
            await SetupBotCommands();
        }

        public TelegramBotClient GetClient() => _botClient;

        private async Task SetupBotCommands()
        {
            // Commands for all users (default scope)
            var defaultCommands = new[]
            {
                new BotCommand { Command = "start", Description = "بدء استخدام البوت - Start using the bot" },
                new BotCommand { Command = "configure", Description = "إعداد أوقات الصلاة - Configure prayer times" }
            };

            await _botClient.SetMyCommands(defaultCommands, scope: new BotCommandScopeDefault(), cancellationToken: CancellationToken.None);
            Console.WriteLine("✅ Bot commands menu configured for all users");
        }

        private async Task SetupSuperAdminCommands(long userId)
        {
            // Prevent setting up multiple times
            if (_superAdminIdsSetup.Contains(userId))
                return;

            try
            {
                // Super admin commands (shown in their personal chat menu)
                var adminCommands = new[]
                {
                    new BotCommand { Command = "start", Description = "بدء استخدام البوت - Start the bot" },
                    new BotCommand { Command = "configure", Description = "إعداد أوقات الصلاة - Configure prayer times" },
                    new BotCommand { Command = "admin", Description = "قائمة أوامر المشرف - Super admin menu" },
                    new BotCommand { Command = "stats", Description = "إحصائيات البوت - Bot statistics" },
                    new BotCommand { Command = "list", Description = "قائمة المجموعات - Groups list" },
                    new BotCommand { Command = "see", Description = "تحميل الملف - Download groups.json" },
                    new BotCommand { Command = "send", Description = "إرسال لجميع - Broadcast to all" },
                    new BotCommand { Command = "send_inactive", Description = "إرسال للغير نشطين - Send to inactive" }
                };

                await _botClient.SetMyCommands(
                    adminCommands,
                    scope: new BotCommandScopeChat { ChatId = userId },
                    cancellationToken: CancellationToken.None
                );

                _superAdminIdsSetup.Add(userId);
                Console.WriteLine($"✅ Super admin commands configured for user {userId}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set super admin commands: {ex.Message}");
            }
        }

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

            // Handle Callback Queries (Button Clicks)
            if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
            {
                var callbackQuery = update.CallbackQuery;
                var callbackChatId = callbackQuery.Message!.Chat.Id;
                var username = callbackQuery.From?.Username;

                if (callbackQuery.Data == "configure_city")
                {
                    // Answer callback to remove loading state
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

                    // Start configuration process
                    var callbackGroup = _storageService.GetGroup(callbackChatId);
                    var userId = callbackQuery.From.Id;

                    // Capture user metadata
                    callbackGroup.AdminUsername = callbackQuery.From.Username ?? "";
                    callbackGroup.AdminId = userId;
                    _storageService.UpdateGroup(callbackGroup);

                    _configState[callbackChatId] = "WAITING_CITY";
                    await botClient.SendMessage(callbackChatId, "الرجاء إرسال اسم المدينة (يفضل بالإنجليزية للدقة) لحساب أوقات الصلاة:", cancellationToken: cancellationToken);
                }
                // Handle super admin buttons
                else if (username == "djstackks" || username == "moloko420")
                {
                    await botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);

                    if (callbackQuery.Data == "admin_stats")
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

                        await botClient.SendMessage(callbackChatId, statsMsg, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                    }
                    else if (callbackQuery.Data == "admin_list")
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
                            report.AppendLine($"   ❤️ Reactions: {g.TotalReactions}");
                            report.AppendLine("-----------------------------------");
                        }

                        string finalMsg = report.ToString();

                        if (finalMsg.Length > 4000)
                        {
                            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(finalMsg));
                            await botClient.SendDocument(callbackChatId, new InputFileStream(stream, "groups_report.txt"), caption: "Groups Report (Too long for message)", cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendMessage(callbackChatId, finalMsg, parseMode: ParseMode.Markdown, cancellationToken: cancellationToken);
                        }
                    }
                    else if (callbackQuery.Data == "admin_see")
                    {
                        string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
                        string filePath = Path.Combine(dataDir, "groups.json");

                        if (System.IO.File.Exists(filePath))
                        {
                            await using var stream = System.IO.File.OpenRead(filePath);
                            await botClient.SendDocument(callbackChatId, new InputFileStream(stream, "groups.json"), caption: "Here is the groups configuration file.", cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await botClient.SendMessage(callbackChatId, "No groups.json file found.", cancellationToken: cancellationToken);
                        }
                    }
                    else if (callbackQuery.Data == "admin_send")
                    {
                        await botClient.SendMessage(callbackChatId,
                            "📢 **Broadcast Message**\n\n" +
                            "To send a message to all users, groups, and channels:\n" +
                            "`/send \"Your message here\"`\n\n" +
                            "To send only to inactive users/groups:\n" +
                            "`/send_inactive \"Your message here\"`\n\n" +
                            "💡 **Tip for inactive users:** Include this in your message:\n" +
                            "• Mention the `/configure` command in Arabic: اضغط على كلمة \"configure\" من القائمة\n" +
                            "• Include instructions link: https://telegra.ph/quran-how-12-16",
                            parseMode: ParseMode.Markdown,
                            cancellationToken: cancellationToken);
                    }
                }
                return;
            }

            // Handle Bot Added to Group or Channel
            if (update.Type == UpdateType.MyChatMember && update.MyChatMember != null)
            {
                var myChatMember = update.MyChatMember;
                
                // Check if this is a NEW addition (not just a permission change)
                // Old status should be Left, Kicked, or Restricted
                var wasNotMember = myChatMember.OldChatMember.Status == ChatMemberStatus.Left ||
                                   myChatMember.OldChatMember.Status == ChatMemberStatus.Kicked ||
                                   myChatMember.OldChatMember.Status == ChatMemberStatus.Restricted;
                
                // New status should be Member or Administrator
                var isNowMember = myChatMember.NewChatMember.Status == ChatMemberStatus.Administrator || 
                                  myChatMember.NewChatMember.Status == ChatMemberStatus.Member;
                
                // Only send welcome if bot was just added (not already a member)
                if (wasNotMember && isNowMember)
                {
                    try 
                    {
                        await botClient.SendMessage(
                            myChatMember.Chat.Id, 
                            "السلام عليكم! 🤖\nأنا بوت الورد اليومي للقرآن الكريم.\n\nللبدء، يجب على المشرف إعداد البوت باستخدام الأمر:\n/configure", 
                            cancellationToken: cancellationToken);
                    }
                    catch
                    {
                        // Silent failure - bot might not have Post Messages permission in channel yet
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
                var chat = await botClient.GetChat(chatId, cancellationToken);

                // Check if this is a private chat with an inactive user
                if (chat.Type == ChatType.Private && !group.IsActive)
                {
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("⚙️ إعداد المدينة لأوقات الصلاة", "configure_city")
                        }
                    });

                    await botClient.SendMessage(chatId,
                        "مرحباً! 👋\n\nلاستقبال الورد اليومي من القرآن الكريم، يجب عليك إعداد مدينتك أولاً لحساب أوقات الصلاة.\n\nاضغط على الزر أدناه للبدء:",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);
                }
                else
                {
                    await botClient.SendMessage(chatId, "مرحباً! استخدم الأمر /configure لإعداد أوقات الصلاة لهذه المجموعة.", cancellationToken: cancellationToken);
                }
                return;
            }

            // Super Admin Commands
            if (messageText.StartsWith("/see") || messageText.StartsWith("/stats") || messageText.StartsWith("/list") ||
                messageText.StartsWith("/send") || messageText.StartsWith("/admin"))
            {
                var username = message.From?.Username;
                if (username != "djstackks" && username != "moloko420")
                {
                    // Ignore or say unauthorized
                    return;
                }

                // Set up super admin commands menu for this user (first time only)
                if (message.From != null)
                {
                    await SetupSuperAdminCommands(message.From.Id);
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

                if (messageText.StartsWith("/send ") && !messageText.StartsWith("/send_inactive"))
                {
                    // Extract message after "/send "
                    string broadcastMsg = messageText.Substring(6).Trim();

                    if (string.IsNullOrEmpty(broadcastMsg))
                    {
                        await botClient.SendMessage(chatId, "❌ Usage: /send \"Your message here\"", cancellationToken: cancellationToken);
                        return;
                    }

                    // Remove quotes if present
                    if (broadcastMsg.StartsWith("\"") && broadcastMsg.EndsWith("\""))
                    {
                        broadcastMsg = broadcastMsg.Substring(1, broadcastMsg.Length - 2);
                    }

                    var groups = _storageService.GetGroups();
                    int successCount = 0;
                    int failCount = 0;

                    await botClient.SendMessage(chatId, $"📢 Starting broadcast to {groups.Count} chats...", cancellationToken: cancellationToken);

                    foreach (var g in groups)
                    {
                        try
                        {
                            await botClient.SendMessage(g.ChatId, broadcastMsg, cancellationToken: cancellationToken);
                            successCount++;
                            await Task.Delay(100); // Prevent rate limiting
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Console.WriteLine($"Failed to send to {g.ChatId}: {ex.Message}");
                        }
                    }

                    await botClient.SendMessage(chatId,
                        $"✅ Broadcast complete!\n\n✔️ Sent: {successCount}\n❌ Failed: {failCount}",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (messageText.StartsWith("/send_inactive "))
                {
                    // Extract message after "/send_inactive "
                    string broadcastMsg = messageText.Substring(15).Trim();

                    if (string.IsNullOrEmpty(broadcastMsg))
                    {
                        await botClient.SendMessage(chatId, "❌ Usage: /send_inactive \"Your message here\"", cancellationToken: cancellationToken);
                        return;
                    }

                    // Remove quotes if present
                    if (broadcastMsg.StartsWith("\"") && broadcastMsg.EndsWith("\""))
                    {
                        broadcastMsg = broadcastMsg.Substring(1, broadcastMsg.Length - 2);
                    }

                    var groups = _storageService.GetGroups();
                    var inactiveGroups = groups.FindAll(g => !g.IsActive);
                    int successCount = 0;
                    int failCount = 0;

                    await botClient.SendMessage(chatId, $"📢 Starting broadcast to {inactiveGroups.Count} inactive chats...", cancellationToken: cancellationToken);

                    foreach (var g in inactiveGroups)
                    {
                        try
                        {
                            await botClient.SendMessage(g.ChatId, broadcastMsg, cancellationToken: cancellationToken);
                            successCount++;
                            await Task.Delay(100); // Prevent rate limiting
                        }
                        catch (Exception ex)
                        {
                            failCount++;
                            Console.WriteLine($"Failed to send to {g.ChatId}: {ex.Message}");
                        }
                    }

                    await botClient.SendMessage(chatId,
                        $"✅ Broadcast to inactive chats complete!\n\n✔️ Sent: {successCount}\n❌ Failed: {failCount}",
                        cancellationToken: cancellationToken);
                    return;
                }

                if (messageText.StartsWith("/admin"))
                {
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("📊 إحصائيات | Stats", "admin_stats"),
                            InlineKeyboardButton.WithCallbackData("📋 قائمة | List", "admin_list")
                        },
                        new[]
                        {
                            InlineKeyboardButton.WithCallbackData("📥 تحميل | Download", "admin_see"),
                            InlineKeyboardButton.WithCallbackData("📢 إرسال | Broadcast", "admin_send")
                        }
                    });

                    string adminMenu = "👤 **Super Admin Commands**\n\n" +
                                      "**Available Commands:**\n\n" +
                                      "• `/stats` - View bot statistics (total groups, active groups, messages sent)\n" +
                                      "• `/list` - Detailed report of all groups with metadata\n" +
                                      "• `/see` - Download groups.json configuration file\n" +
                                      "• `/send \"message\"` - Broadcast message to ALL users, groups, and channels\n" +
                                      "• `/send_inactive \"message\"` - Send to inactive users (include: `/configure` + https://telegra.ph/quran-how-12-16)\n" +
                                      "• `/admin` - Show this menu\n\n" +
                                      "**Quick Access Buttons:**";

                    await botClient.SendMessage(chatId, adminMenu,
                        parseMode: ParseMode.Markdown,
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken);
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
                await botClient.SendMessage(chatId, "الرجاء إرسال اسم المدينة (يفضل بالإنجليزية للدقة) لحساب أوقات الصلاة:", cancellationToken: cancellationToken);
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

                string state = _configState[chatId];
                if (state == "WAITING_CITY")
                {
                    group.City = messageText.Trim();
                    _storageService.UpdateGroup(group);
                    _configState[chatId] = "WAITING_COUNTRY";
                    await botClient.SendMessage(chatId, "ممتاز! الآن الرجاء إرسال اسم الدولة (يفضل بالإنجليزية):", cancellationToken: cancellationToken);
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
                        var retryKeyboard = new InlineKeyboardMarkup(new[]
                        {
                            new[]
                            {
                                InlineKeyboardButton.WithCallbackData("⚙️ إعداد المدينة مرة أخرى", "configure_city")
                            }
                        });

                        await botClient.SendMessage(chatId,
                            "⚠️ عذراً، لم يتم العثور على المدينة أو الدولة المحددة.\n\n" +
                            "💡 نصائح:\n" +
                            "• حاول كتابة اسم المدينة بطريقة مختلفة\n" +
                            "• استخدم اللغة الإنجليزية للحصول على نتائج أفضل\n" +
                            "• تأكد من صحة الإملاء\n\n" +
                            "إذا استمرت المشكلة، يمكنك التواصل معنا: @moloko420\n\n" +
                            "للمحاولة مرة أخرى، اضغط على الزر أدناه:",
                            replyMarkup: retryKeyboard,
                            cancellationToken: cancellationToken);
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
