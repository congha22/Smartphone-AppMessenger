using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Smartphone;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SmartphoneAppMessenger
{
    public partial class ModEntry : Mod
    {
        private const string SmartphoneModId = "d5a1lamdtd.Smartphone";
        private const string UnlimitedEventModId = "d5a1lamdtd.UnlimitedEventExpansion";
        private const string AppId = "messenger";

        public static ModEntry Instance { get; private set; } = null!;
        public static ModConfig Config { get; private set; } = null!;
        public static string currentPlayerProfile = string.Empty;
        public static Dictionary<(string season, int day), List<NPC>> NpcBirthdaysByDate = new();
        public static Dictionary<string, string> NpcCharacteristicsShort = new();
        public static Dictionary<string, string> NpcCharacteristicsMinimal = new();
        public static Dictionary<string, string> NpcCharacteristicsLong = new();
        public static IMonitor SMonitor = null!;
        public static List<RecentEvent> RecentEvents = new();
        public static Dictionary<string, string> npcConversationSummary = new();
        public static Dictionary<string, GiftMemory> GiftMemories = new(StringComparer.OrdinalIgnoreCase);
        public static bool isTodayEventAdded = false;
        public static List<string> FarmCropNames = new();
        public static List<string> FarmTreeNames = new();
        public static int lastTimeReceiveMessage = 300;

        public static IUnlimitedEventExpansionApi? iUnlimitedEventExpansionApi;
        internal static ISmartPhoneApi? iSmartphoneApi;
        private Texture2D? appIcon;
        private Dictionary<string, Texture2D> themedIcons = new(StringComparer.OrdinalIgnoreCase);
        public static Texture2D? PortraitBackgroundTexture { get; private set; }

        // Message queue fields
        public static readonly object replyQueueLock = new();
        public static readonly Dictionary<string, List<string>> pendingMessages = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, System.Threading.CancellationTokenSource> replyTimers = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, DateTime> lastInputActivityUtc = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, TimeSpan> replyInactivityDelays = new(StringComparer.OrdinalIgnoreCase);

        public static void QueueUserMessage(string npcName, string message)
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(message))
                return;

            lock (replyQueueLock)
            {
                if (!pendingMessages.TryGetValue(npcName, out List<string>? queue))
                {
                    queue = new List<string>();
                    pendingMessages[npcName] = queue;
                }

                queue.Add(message);
                lastInputActivityUtc[npcName] = DateTime.UtcNow;

                SchedulePendingReplyLocked(npcName);
            }
        }

        public static void RegisterTextInputActivity(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return;

            lock (replyQueueLock)
            {
                lastInputActivityUtc[npcName] = DateTime.UtcNow;

                if (pendingMessages.TryGetValue(npcName, out List<string>? queue) && queue.Count > 0)
                {
                    SchedulePendingReplyLocked(npcName);
                }
            }
        }

        private static void SchedulePendingReplyLocked(string npcName)
        {
            if (replyTimers.TryGetValue(npcName, out var previousToken))
            {
                try { previousToken.Cancel(); } catch { }
                previousToken.Dispose();
            }

            double seconds = 6.0 + Game1.random.NextDouble() * 4.0;
            replyInactivityDelays[npcName] = TimeSpan.FromSeconds(seconds);

            var cts = new System.Threading.CancellationTokenSource();
            replyTimers[npcName] = cts;

            _ = WaitForReplyInactivity(npcName, cts);
        }

        private static async System.Threading.Tasks.Task WaitForReplyInactivity(string npcName, System.Threading.CancellationTokenSource cts)
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    TimeSpan remainingDelay = GetRemainingReplyDelay(npcName);
                    if (remainingDelay > TimeSpan.Zero)
                        await System.Threading.Tasks.Task.Delay(remainingDelay, cts.Token);

                    if (cts.IsCancellationRequested)
                        return;

                    if (GetRemainingReplyDelay(npcName) > TimeSpan.Zero)
                        continue;

                    await SendBatchMessage(npcName);
                    return;
                }
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                // timer reset
            }
            finally
            {
                lock (replyQueueLock)
                {
                    if (replyTimers.TryGetValue(npcName, out var activeCts) && ReferenceEquals(activeCts, cts))
                    {
                        replyTimers.Remove(npcName);
                    }
                }
                cts.Dispose();
            }
        }

        private static TimeSpan GetRemainingReplyDelay(string npcName)
        {
            lock (replyQueueLock)
            {
                if (!lastInputActivityUtc.TryGetValue(npcName, out DateTime lastInputUtc))
                    return TimeSpan.Zero;

                if (!replyInactivityDelays.TryGetValue(npcName, out TimeSpan delay))
                    delay = TimeSpan.FromSeconds(10);

                TimeSpan elapsed = DateTime.UtcNow - lastInputUtc;
                return elapsed >= delay
                    ? TimeSpan.Zero
                    : delay - elapsed;
            }
        }

        private static async System.Threading.Tasks.Task SendBatchMessage(string npcName)
        {
            await SendBatchMessage(npcName, consumeAiSlotNow: true);
        }

        private static async System.Threading.Tasks.Task SendBatchMessage(string npcName, bool consumeAiSlotNow)
        {
            if (consumeAiSlotNow)
            {
                await RunAiActionWithQueueAsync(
                    () => SendBatchMessage(npcName, consumeAiSlotNow: false),
                    queueKey: $"chat:{npcName}",
                    highPriority: true);

                return;
            }

            List<string> messages;
            lock (replyQueueLock)
            {
                if (!pendingMessages.TryGetValue(npcName, out List<string>? queue) || queue.Count == 0)
                    return;

                messages = new List<string>(queue);
                queue.Clear();
            }

            string merged = string.Join("\n", messages.Where(text => !string.IsNullOrWhiteSpace(text)));
            string response = await SendMessageToAssistant(npcName, merged);

            if (!string.IsNullOrWhiteSpace(response))
            {
                if (response.StartsWith($"{npcName}:", StringComparison.OrdinalIgnoreCase))
                    response = response.Substring(npcName.Length + 1).TrimStart();

                MessageManager.AddMessage(npcName, response, type: "response");
            }
        }

        public static void ClearPendingQueuedChatReplies()
        {
            lock (replyQueueLock)
            {
                foreach (var cts in replyTimers.Values)
                {
                    try { cts.Cancel(); } catch { }
                    cts.Dispose();
                }

                replyTimers.Clear();
                pendingMessages.Clear();
                lastInputActivityUtc.Clear();
                replyInactivityDelays.Clear();
            }
        }


        public override void Entry(IModHelper helper)
        {
            Instance = this;
            SMonitor = this.Monitor;
            Config = helper.ReadConfig<ModConfig>();
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.DayEnding += this.OnDayEnding;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.TimeChanged += this.OnTimeChanged;
            helper.Events.GameLoop.Saving += this.OnSaving;
            helper.Events.GameLoop.OneSecondUpdateTicked += this.OnOneSecondUpdateTicked;

            // Multiplayer chunked transfer events
            helper.Events.GameLoop.UpdateTicked += TransferManager.OnUpdateTicked;
            helper.Events.Multiplayer.ModMessageReceived += TransferManager.OnModMessageReceived;
            helper.Events.Multiplayer.PeerConnected += TransferManager.OnPeerConnected;

            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.PatchAll();
        }

        public override object GetApi()
        {
            return new AppMessengerApi();
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            HandleAiModelSettingTimeChanged(e.NewTime);
            HandleAiUsageTimeChanged(e.NewTime);
            if (Game1.timeOfDay < 2200)
            {
                CheckSendNewMessage();
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            try
            {
                MessengerChatScreen.ClearChatImageCache();
                MessengerAppScreen.ClearAvatarCache();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error clearing photo caches: {ex.Message}", LogLevel.Warn);
            }

            string summaryPath = System.IO.Path.Combine("userdata", MessageManager.GetActiveSaveFolderName(), "npc_conversation_summary.json");
            npcConversationSummary = this.Helper.Data.ReadJsonFile<Dictionary<string, string>>(summaryPath)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string saveDir = System.IO.Path.Combine("userdata", MessageManager.GetActiveSaveFolderName());
            GiftMemories = this.Helper.Data.ReadJsonFile<Dictionary<string, GiftMemory>>(System.IO.Path.Combine(saveDir, "gift_memory.json"))
                ?? new Dictionary<string, GiftMemory>(StringComparer.OrdinalIgnoreCase);
            RecentEvents = this.Helper.Data.ReadJsonFile<List<RecentEvent>>(System.IO.Path.Combine(saveDir, "recent_event_memory.json"))
                ?? new List<RecentEvent>();

            MessageManager.LoadHistory(this.Helper);
            PhoneDialogueRuntime.ClearDailyState();

            HandleAiModelSettingTimeChanged(600);
            HandleAiUsageTimeChanged(600);

            // Clean up photo_shared folder
            string activeSave = MessageManager.GetActiveSaveFolderName();
            string photoSharedDir = System.IO.Path.Combine(this.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
            MessageManager.EnforcePhotoSharedRetention(photoSharedDir, Config.PhotoShared);

            string npc_characteristic_minimal = this.Helper.ModContent.GetInternalAssetName("assets/npc_characteristics_minimal.json").BaseName;
            string npc_characteristic_short = this.Helper.ModContent.GetInternalAssetName("assets/npc_characteristics_short.json").BaseName;
            string npc_characteristic_long = this.Helper.ModContent.GetInternalAssetName("assets/npc_characteristics_long.json").BaseName;

            NpcCharacteristicsMinimal = this.Helper.ModContent.Load<Dictionary<string, string>>(npc_characteristic_minimal);
            NpcCharacteristicsShort = this.Helper.ModContent.Load<Dictionary<string, string>>(npc_characteristic_short);
            NpcCharacteristicsLong = this.Helper.ModContent.Load<Dictionary<string, string>>(npc_characteristic_long);

            NpcBirthdaysByDate.Clear();
            foreach (var npc in Utility.getAllVillagers())
            {
                if (npc.CanSocialize && !npc.IsInvisible && !string.IsNullOrEmpty(npc.Birthday_Season) && npc.Birthday_Day > 0)
                {
                    var key = (npc.Birthday_Season, npc.Birthday_Day);
                    if (!NpcBirthdaysByDate.ContainsKey(key))
                        NpcBirthdaysByDate[key] = new List<NPC>();

                    NpcBirthdaysByDate[key].Add(npc);
                }
            }

            if (iUnlimitedEventExpansionApi != null)
                iUnlimitedEventExpansionApi.SendNpcConversationSummary(npcConversationSummary);
        }

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            ClearPendingQueuedChatReplies();

            // gift memory
            var giftKeysToRemove = new List<string>();
            foreach (var entry in GiftMemories)
            {
                entry.Value.DaysRemaining--;
                if (entry.Value.DaysRemaining <= 0)
                    giftKeysToRemove.Add(entry.Key);
            }
            foreach (var key in giftKeysToRemove)
                GiftMemories.Remove(key);

            // event memory
            foreach (var evt in RecentEvents)
                evt.DaysRemaining--;
            RecentEvents = RecentEvents
                .Where(evt => evt.DaysRemaining > 0)
                .ToList();

            var conversationsToSummarize = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in MessageManager.NpcMessagesToday)
            {
                string npcName = kvp.Key;
                List<string> messages = kvp.Value
                    .Skip(Math.Max(0, kvp.Value.Count - 30))
                    .ToList();

                if (messages.Count == 0)
                    continue;

                conversationsToSummarize[npcName] = string.Join("\n", messages);
            }

            MessageManager.SaveAndResetDailyMessages(this.Helper);

            // Clean up photo_shared folder
            string activeSave = MessageManager.GetActiveSaveFolderName();
            string photoSharedDir = System.IO.Path.Combine(this.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
            MessageManager.EnforcePhotoSharedRetention(photoSharedDir, Config.PhotoShared);

            if (conversationsToSummarize.Count > 0)
            {
                System.Threading.Tasks.Task.Run(async () =>
                {
                    try
                    {
                        Dictionary<string, string> batchSummaries = await SummaryConversationsBatch(conversationsToSummarize, bypassAiLimit: true);
                        if (batchSummaries.Count == 0)
                            return;

                        foreach (var kvp in batchSummaries)
                            npcConversationSummary[kvp.Key] = kvp.Value;

                        string relativePath = System.IO.Path.Combine("userdata", MessageManager.GetActiveSaveFolderName(), "npc_conversation_summary.json");
                        this.Helper.Data.WriteJsonFile(relativePath, npcConversationSummary);

                        // send conversationSummary to iModApi
                        if (iUnlimitedEventExpansionApi != null)
                            iUnlimitedEventExpansionApi.SendNpcConversationSummary(npcConversationSummary);
                    }
                    catch (Exception ex)
                    {
                        SMonitor.Log($"Unable to update NPC conversation summaries in batch: {ex}", LogLevel.Trace);
                    }
                });
            }
            else
            {
                if (iUnlimitedEventExpansionApi != null)
                    iUnlimitedEventExpansionApi.SendNpcConversationSummary(npcConversationSummary);
            }
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            lastTimeReceiveMessage = 600;
            isTodayEventAdded = false;
            try
            {
                MessengerChatScreen.ClearChatImageCache();
                MessengerAppScreen.ClearAvatarCache();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Error clearing photo caches: {ex.Message}", LogLevel.Warn);
            }

            string saveDir = System.IO.Path.Combine("userdata", MessageManager.GetActiveSaveFolderName());
            GiftMemories = this.Helper.Data.ReadJsonFile<Dictionary<string, GiftMemory>>(System.IO.Path.Combine(saveDir, "gift_memory.json"))
                ?? new Dictionary<string, GiftMemory>(StringComparer.OrdinalIgnoreCase);
            RecentEvents = this.Helper.Data.ReadJsonFile<List<RecentEvent>>(System.IO.Path.Combine(saveDir, "recent_event_memory.json"))
                ?? new List<RecentEvent>();
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            ConfigMenu(this.ModManifest, this.Helper);

            iSmartphoneApi = this.Helper.ModRegistry.GetApi<ISmartPhoneApi>(SmartphoneModId);
            iUnlimitedEventExpansionApi = this.Helper.ModRegistry.GetApi<IUnlimitedEventExpansionApi>(UnlimitedEventModId);
            if (iSmartphoneApi == null)
            {
                this.Monitor.Log("Smartphone API is unavailable; Messenger app was not registered.", LogLevel.Warn);
                return;
            }

            iSmartphoneApi.ContactableNpcsChanged += MessageManager.UpdateAvailableNpcs;


            this.LoadIcons();
            this.RegisterMessengerApp();
        }

        private void LoadIcons()
        {
            try
            {
                this.themedIcons.Clear();
                try
                {
                    this.themedIcons["default"] = this.Helper.ModContent.Load<Texture2D>("assets/default/1x1.png");
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed to load default theme icon: {ex.Message}", LogLevel.Error);
                }

                try
                {
                    this.themedIcons["v2"] = this.Helper.ModContent.Load<Texture2D>("assets/v2/1x1.png");
                }
                catch (Exception ex)
                {
                    this.Monitor.Log($"Failed to load v2 theme icon: {ex.Message}", LogLevel.Error);
                }

                string iconPath = "assets/default/1x1.png";
                this.appIcon = this.Helper.ModContent.Load<Texture2D>(iconPath);
                PortraitBackgroundTexture = this.Helper.ModContent.Load<Texture2D>("assets/background.png");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed to load messenger assets: {ex.Message}", LogLevel.Error);
            }
        }


        private void RegisterMessengerApp()
        {
            if (iSmartphoneApi == null || this.themedIcons.Count == 0)
                return;

            string compositeId = $"{this.ModManifest.UniqueID}::{AppId}";

            bool appRegistered = iSmartphoneApi.RegisterPhoneApp(
                ownerModId: this.ModManifest.UniqueID,
                appId: AppId,
                displayName: GetTranslation("app.name"),
                onClick: this.OpenMessengerApp,
                closePhoneOnLaunch: true,
                sourceRect: null,
                getBadgeCount: () =>
                {
                    try
                    {
                        return MessageManager.UnreadCounts.Values.Sum();
                    }
                    catch
                    {
                        return 0;
                    }
                },
                supportedSizes: new AppSize[] { AppSize.Size1x1, AppSize.Size2x1, AppSize.Size2x2 },
                onDrawWidget: (b, rect, size) => MessengerWidget.Draw(b, rect, size, this.appIcon ?? this.themedIcons["default"], PortraitBackgroundTexture, iSmartphoneApi, compositeId),
                themedIconTextures: this.themedIcons
            );

            if (!appRegistered)
            {
                this.Monitor.Log("Failed to register Messenger app.", LogLevel.Warn);
            }

            // Register Contact Action Card for Messenger
            List<IContactActionCardButton> buttons = new List<IContactActionCardButton>
            {
                new ContactActionCardButton
                {
                    Text = GetTranslation("button.chat"),
                    BackgroundColor = Color.SeaGreen,
                    TextColor = Color.White,
                    OnClick = (npcName) =>
                    {
                        if (iSmartphoneApi == null) return;
                        MessageManager.UnreadCounts[npcName] = 0;
                        Game1.activeClickableMenu = new MessengerChatScreen(
                            iSmartphoneApi,
                            npcName,
                            () =>
                            {
                                MessageManager.UnreadCounts[npcName] = 0;
                                Game1.activeClickableMenu = new MessengerAppScreen(iSmartphoneApi, () => iSmartphoneApi.OpenPhoneHomeScreen());
                            }
                        );
                    }
                }
            };
            iSmartphoneApi.RegisterContactActionCard(this.ModManifest.UniqueID, GetTranslation("app.name"), buttons);
        }

        private void OpenMessengerApp()
        {
            if (!Context.IsWorldReady || iSmartphoneApi == null)
                return;

            Game1.activeClickableMenu = new MessengerAppScreen(
                iSmartphoneApi,
                () => iSmartphoneApi.OpenPhoneHomeScreen());
        }

        public static void CheckSendNewMessage()
        {
            int timePassed = Game1.timeOfDay - lastTimeReceiveMessage;
            int baseChance = Config.NewMessageChance == ModConfig.NewMessageChanceLow
                ? (timePassed - 100) / 100
                : (timePassed - 100) / 50;
            baseChance = Math.Min(baseChance, 15);

            if (Game1.random.NextDouble() < baseChance / 100.0)
            {
                List<string> npcCandidates = MessageManager.GetAvailableNpcNames()
                    .OrderByDescending(name => Game1.player.getFriendshipHeartLevelForNPC(name))
                    .ToList();

                if (npcCandidates.Count == 0)
                    return;

                double power = 1.4;
                int maxValue = Math.Min(npcCandidates.Count, 20);
                if (maxValue < 1)
                    return;

                double rand = Game1.random.NextDouble();
                int result = (int)(Math.Pow(rand, power) * maxValue);

                int counter = 0;

                while (counter < 3)
                {
                    string npcName = npcCandidates[Math.Min(result + counter, maxValue - 1)];
                    NPC npc = Game1.getCharacterFromName(npcName, mustBeVillager: false);

                    if (npc == null)
                    {
                        counter++;
                        continue;
                    }

                    long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    long lastTime = MessageManager.LatestMessageTimestamps.TryGetValue(npcName, out int time) ? time : 0;

                    if (currentTime - lastTime > 180 && !MessageManager.NpcMessagesToday.ContainsKey(npcName))
                    {
                        bool talkedToToday = Game1.player.friendshipData.TryGetValue(npcName, out Friendship? friendship)
                                             && friendship.TalkedToToday;

                        if (!talkedToToday && !Config.DisableDailyMessage)
                        {
                            if (friendship != null)
                                friendship.TalkedToToday = true;

                            npc.checkForNewCurrentDialogue(Game1.player.getFriendshipHeartLevelForNPC(npcName));

                            if (npc.currentMarriageDialogue != null && npc.currentMarriageDialogue.Count > 0)
                            {
                                if (npc.CurrentDialogue == null)
                                    npc.CurrentDialogue = new Stack<Dialogue>();

                                for (int i = npc.currentMarriageDialogue.Count - 1; i >= 0; i--)
                                {
                                    var dialogueRef = npc.currentMarriageDialogue[i];
                                    Dialogue actualDialogue = dialogueRef.GetDialogue(npc);

                                    if (actualDialogue != null)
                                    {
                                        npc.CurrentDialogue.Push(actualDialogue);
                                    }
                                }

                                npc.currentMarriageDialogue.Clear();
                            }

                            if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
                            {
                                Task.Run(async () =>
                                {
                                    await PhoneDialogueRuntime.DeliverDialogueSequenceAsync(
                                        npcName,
                                        npc.CurrentDialogue,
                                        useRandomDelay: false,
                                        minDelayMs: 0,
                                        maxDelayMs: 1);

                                    npc.CurrentDialogue?.Clear();
                                });
                            }
                        }
                        else
                        {
                            if (iUnlimitedEventExpansionApi != null && Game1.timeOfDay < 1900 && Game1.random.NextDouble() < 0.3 && Game1.player.getFriendshipHeartLevelForNPC(npcName) >= 3 && iUnlimitedEventExpansionApi.CanScheduleNewEvent())
                            {
                                Task.Run(async () =>
                                {
                                    if (!TryConsumeAiCallSlot())
                                        return;

                                    string messages = await SendMessageToAssistant(npcName, type: "invite");
                                    if (!string.IsNullOrWhiteSpace(messages)
                                        && !messages.StartsWith("SYSTEM:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (messages.StartsWith($"{npcName}:", StringComparison.OrdinalIgnoreCase))
                                            messages = messages.Substring(npcName.Length + 1).TrimStart();
                                        MessageManager.AddMessage(npcName, messages, type: "response");
                                        lastTimeReceiveMessage = Game1.timeOfDay;
                                    }
                                });
                            }
                            else
                            {
                                Task.Run(async () =>
                                {
                                    if (!TryConsumeAiCallSlot())
                                        return;

                                    string messages = await SendMessageToAssistant(npcName, type: "text");
                                    if (!string.IsNullOrWhiteSpace(messages)
                                        && !messages.StartsWith("SYSTEM:", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (messages.StartsWith($"{npcName}:", StringComparison.OrdinalIgnoreCase))
                                            messages = messages.Substring(npcName.Length + 1).TrimStart();
                                        MessageManager.AddMessage(npcName, messages, type: "response");
                                        lastTimeReceiveMessage = Game1.timeOfDay;
                                    }
                                });
                            }
                        }

                        break;
                    }

                    counter++;
                }
            }
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            string saveDir = System.IO.Path.Combine("userdata", MessageManager.GetActiveSaveFolderName());
            this.Helper.Data.WriteJsonFile(System.IO.Path.Combine(saveDir, "gift_memory.json"), GiftMemories);
            this.Helper.Data.WriteJsonFile(System.IO.Path.Combine(saveDir, "recent_event_memory.json"), RecentEvents);
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (e.IsMultipleOf(15))
            {
                CheckCurrentEvent();
            }
        }

        public static List<NPC> GetNpcsWithBirthdayToday()
        {
            int today = Game1.dayOfMonth;
            string season = Game1.currentSeason;

            return NpcBirthdaysByDate.TryGetValue((season, today), out var list)
                ? list
                : new List<NPC>();
        }

        public static void CheckCurrentEvent()
        {
            if (Game1.currentSeason == "spring" && Game1.dayOfMonth == 24 && Game1.player.dancePartner.TryGetVillager() != null && !isTodayEventAdded)
            {
                RecentEvents.Add(new RecentEvent
                {
                    Description = $"Player and {Game1.player.dancePartner.TryGetVillager().Name} danced together at the Flower Dance",
                    DaysRemaining = 7
                });
                isTodayEventAdded = true;
            }
            else if (Game1.CurrentEvent != null && Game1.CurrentEvent.isFestival && !isTodayEventAdded && !(Game1.currentSeason == "spring" && Game1.dayOfMonth == 24))
            {
                string festivalId = Game1.CurrentEvent.FestivalName;
                RecentEvents.Add(new RecentEvent
                {
                    Description = $"Player joined {festivalId} event with everyone in the town.",
                    DaysRemaining = 5
                });
                isTodayEventAdded = true;
            }
        }

        [HarmonyPatch(typeof(NPC), nameof(NPC.receiveGift))]
        [HarmonyPatch(new[] { typeof(StardewValley.Object), typeof(Farmer), typeof(bool), typeof(float), typeof(bool) })]
        public static class NPCReceiveGiftPatch
        {
            public static void Postfix(NPC __instance, StardewValley.Object o)
            {
                if (__instance != null && o != null)
                {
                    GiftMemories[__instance.Name] = new GiftMemory
                    {
                        GiftName = o.DisplayName,
                        DaysRemaining = 3
                    };
                }
            }
        }

        public static string GetTranslation(string key, object? tokens = null)
        {
            return Instance.Helper.Translation.Get(key, tokens).ToString();
        }
    }

    public class AppMessengerApi : IAppMessengerApi
    {
        public bool RegisterChatQuickActionButton(
            string ownerModId,
            string actionId,
            Texture2D iconTexture,
            Action<string> onClick,
            bool closePhoneOnLaunch = false,
            int sortOrder = 0,
            Rectangle? sourceRect = null,
            List<string>? npcNames = null)
        {
            return ModEntry.RegisterChatQuickActionButtonInternal(
                ownerModId,
                actionId,
                iconTexture,
                onClick,
                closePhoneOnLaunch,
                sortOrder,
                sourceRect,
                npcNames);
        }

        public bool UnregisterChatQuickActionButton(string ownerModId, string actionId)
        {
            return ModEntry.UnregisterChatQuickActionButtonInternal(ownerModId, actionId);
        }

        public bool RegisterUnlimitedEvent(
            string ownerModId,
            string eventType,
            Action<string> triggerEvent,
            int minimumHeartLevel = 0,
            string toolDescription = "")
        {
            return ModEntry.RegisterUnlimitedEventInternal(
                ownerModId,
                eventType,
                triggerEvent,
                minimumHeartLevel,
                toolDescription);
        }

        public bool UnregisterUnlimitedEvent(string ownerModId, string eventType)
        {
            return ModEntry.UnregisterUnlimitedEventInternal(ownerModId, eventType);
        }

        public void SendSmartphoneMessageFromNPC(string npcName, string message, string playerId = "")
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(message))
                return;

            // Only add if NPC is unlocked (already in the list)
            if (!MessageManager.IsNpcUnlocked(npcName))
                return;

            MessageManager.AddMessage(npcName, message, type: "response");
        }

        public void SendSmartphoneMessageFromPlayer(string npcName, string message, string playerId = "")
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(message))
                return;

            // Only add if NPC is unlocked (already in the list)
            if (!MessageManager.IsNpcUnlocked(npcName))
                return;

            MessageManager.AddMessage(npcName, message, type: "sent");
        }
    }
}

