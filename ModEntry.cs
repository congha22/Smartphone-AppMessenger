using System;
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
        public static List<RecentEvent> RecentEvents = new(); // Just stubbing it out for compilation if it's not ported
        public static Dictionary<string, string> npcConversationSummary = new();
        public static Dictionary<string, GiftMemory> GiftMemories = new(); // Stub
        public static List<string> FarmCropNames = new();
        public static List<string> FarmTreeNames = new();
        public static int lastTimeReceiveMessage = 300;

        public static IUnlimitedEventExpansionApi? iUnlimitedEventExpansionApi;
        internal static ISmartPhoneApi? iSmartphoneApi;
        private Texture2D? appIcon;
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
        }

        public override object GetApi()
        {
            return new AppMessengerApi();
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            HandleAiModelSettingTimeChanged(e.NewTime);
            HandleAiUsageTimeChanged(e.NewTime);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            string summaryPath = System.IO.Path.Combine("userdata", MessageManager.GetActiveSaveFolderName(), "npc_conversation_summary.json");
            npcConversationSummary = this.Helper.Data.ReadJsonFile<Dictionary<string, string>>(summaryPath)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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


            this.LoadIcons();
            this.RegisterMessengerApp();
        }

        private void LoadIcons()
        {
            try
            {
                this.appIcon = this.Helper.ModContent.Load<Texture2D>("assets/app_messenger.png");
                PortraitBackgroundTexture = this.Helper.ModContent.Load<Texture2D>("assets/background.png");
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed to load messenger assets: {ex.Message}", LogLevel.Error);
            }
        }


        private void RegisterMessengerApp()
        {
            if (iSmartphoneApi == null || this.appIcon == null)
                return;

            bool appRegistered = iSmartphoneApi.RegisterPhoneApp(
                ownerModId: this.ModManifest.UniqueID,
                appId: AppId,
                displayName: "Messenger",
                iconTexture: this.appIcon,
                onClick: this.OpenMessengerApp,
                closePhoneOnLaunch: true,
                sortOrder: 1, // Sort order
                sourceRect: null,
                isVisible: () => Context.IsWorldReady,
                getBadgeCount: null);

            if (!appRegistered)
            {
                this.Monitor.Log("Failed to register Messenger app.", LogLevel.Warn);
            }
        }

        private void OpenMessengerApp()
        {
            if (!Context.IsWorldReady || iSmartphoneApi == null)
                return;

            Game1.activeClickableMenu = new MessengerAppScreen(
                iSmartphoneApi,
                () => iSmartphoneApi.OpenPhoneHomeScreen());
        }
    }
}
