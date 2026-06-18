using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StardewModdingAPI;
using StardewValley;

namespace SmartphoneAppMessenger
{
    public partial class ModEntry
    {
        private sealed class QueuedAiAction
        {
            public Func<Task> Action { get; init; } = null!;
            public string QueueKey { get; init; } = string.Empty;
        }

        private const int AiCreditRemaining = 5;
        public const int DailyAiLimit = 15;
        public static int AiCallsRemainingToday = AiCreditRemaining;
        public static int DailyAiUsageRemainingToday = DailyAiLimit;
        public static int SuccessfulAiCallsToday = 0;
        public static bool IsAiDisabledForPhoneInactivityToday = false;

        private static readonly object aiQuotaLock = new();
        private static readonly Queue<QueuedAiAction> highPriorityQueuedAiActions = new();
        private static readonly Queue<QueuedAiAction> normalPriorityQueuedAiActions = new();
        private static readonly HashSet<string> queuedAiActionKeys = new(StringComparer.OrdinalIgnoreCase);
        private static bool aiQueueProcessorRunning = false;

        public static bool IsAiUsageLimitEnabled()
        {
            return IsSharedAiProviderMode();
        }

        public static bool IsAiTemporarilyDisabledForPhoneInactivity()
        {
            return IsAiUsageLimitEnabled() && IsAiDisabledForPhoneInactivityToday;
        }

        public static void ResetDailyAiUsageLimit()
        {
            lock (aiQuotaLock)
            {
                AiCallsRemainingToday = AiCreditRemaining;
                DailyAiUsageRemainingToday = DailyAiLimit;
                SuccessfulAiCallsToday = 0;
            }
        }

        public static (int DailyUsageLeft, int DailyUsageMax, int CreditsLeft, int CreditsMax) GetAiUsageSnapshot()
        {
            lock (aiQuotaLock)
            {
                return (
                    DailyAiUsageRemainingToday,
                    DailyAiLimit,
                    AiCallsRemainingToday,
                    AiCreditRemaining);
            }
        }

        public static bool TryGetNextAiCreditRefillTime(int currentTime, out int nextRefillTime)
        {
            nextRefillTime = 0;
            if (!IsAiUsageLimitEnabled())
                return false;

            lock (aiQuotaLock)
            {
                if (DailyAiUsageRemainingToday <= 0 || AiCallsRemainingToday >= AiCreditRemaining)
                    return false;
            }

            int hour = NormalizeTimeToHour(currentTime);
            int nextRefillHour = ((hour / 2) + 1) * 2;
            if (nextRefillHour > 26)
                return false;

            nextRefillTime = nextRefillHour * 100;
            return true;
        }

        public static void HandleAiUsageTimeChanged(int newTime)
        {
            if (IsMaxedLimit)
            {
                AiCallsRemainingToday = 0;
                return;
            }

            if (IsAiTemporarilyDisabledForPhoneInactivity())
                return;

            if (!IsAiUsageLimitEnabled())
            {
                TriggerQueuedAiActions();
                return;
            }

            if (newTime % 100 != 0)
                return;

            bool refilled = false;
            lock (aiQuotaLock)
            {
                if (DailyAiUsageRemainingToday > 0 && AiCallsRemainingToday < AiCreditRemaining)
                {
                    AiCallsRemainingToday++;
                    refilled = true;
                }
            }

            if (refilled)
            {
                if (TryTakeNextQueuedAiAction(out Func<Task> queuedAction))
                    _ = RunAiActionSafeAsync(queuedAction);
            }
        }

        public static bool TryConsumeAiCallSlot()
        {
            if (IsAiTemporarilyDisabledForPhoneInactivity())
                return false;

            if (!IsAiUsageLimitEnabled())
                return true;

            lock (aiQuotaLock)
            {
                if (DailyAiUsageRemainingToday <= 0)
                    return false;

                if (AiCallsRemainingToday <= 0)
                    return false;

                AiCallsRemainingToday--;
                DailyAiUsageRemainingToday--;
                return true;
            }
        }

        public static void RegisterSuccessfulAiCall()
        {
            lock (aiQuotaLock)
            {
                SuccessfulAiCallsToday++;
            }
        }

        public static Task RunAiActionWithQueueAsync(Func<Task> action, string queueKey = "", bool highPriority = false)
        {
            return RunAiActionWithQueueInternalAsync(action, queueKey, highPriority, consumeSlotNow: true);
        }

        private static async Task RunAiActionWithQueueInternalAsync(Func<Task> action, string queueKey, bool highPriority, bool consumeSlotNow)
        {
            if (action == null)
                return;

            if (IsAiTemporarilyDisabledForPhoneInactivity())
                return;

            if (!IsAiUsageLimitEnabled())
            {
                await RunAiActionSafeAsync(action);
                TriggerQueuedAiActions();
                return;
            }

            if (consumeSlotNow)
            {
                if (!TryConsumeAiCallSlot())
                {
                    EnqueueAiAction(
                        () => RunAiActionWithQueueInternalAsync(action, queueKey, highPriority, consumeSlotNow: false),
                        queueKey,
                        highPriority);
                    return;
                }
            }

            await RunAiActionSafeAsync(action);
        }

        public static void ClearQueuedAiActions()
        {
            lock (aiQuotaLock)
            {
                highPriorityQueuedAiActions.Clear();
                normalPriorityQueuedAiActions.Clear();
                queuedAiActionKeys.Clear();
            }
        }

        public static void TriggerQueuedAiActions(int maxActionsToProcess = int.MaxValue)
        {
            if (maxActionsToProcess <= 0)
                return;

            _ = ProcessQueuedAiActionsAsync(maxActionsToProcess);
        }

        private static void EnqueueAiAction(Func<Task> action, string queueKey, bool highPriority)
        {
            if (action == null)
                return;

            lock (aiQuotaLock)
            {
                if (!string.IsNullOrWhiteSpace(queueKey) && queuedAiActionKeys.Contains(queueKey))
                    return;

                var queuedAction = new QueuedAiAction
                {
                    Action = action,
                    QueueKey = queueKey ?? string.Empty
                };

                if (highPriority)
                    highPriorityQueuedAiActions.Enqueue(queuedAction);
                else
                    normalPriorityQueuedAiActions.Enqueue(queuedAction);

                if (!string.IsNullOrWhiteSpace(queuedAction.QueueKey))
                    queuedAiActionKeys.Add(queuedAction.QueueKey);
            }
        }

        private static async Task ProcessQueuedAiActionsAsync(int maxActionsToProcess)
        {
            lock (aiQuotaLock)
            {
                if (aiQueueProcessorRunning)
                    return;

                aiQueueProcessorRunning = true;
            }

            try
            {
                int processed = 0;

                while (processed < maxActionsToProcess)
                {
                    if (!TryTakeNextQueuedAiAction(out Func<Task> nextAction))
                        break;

                    await RunAiActionSafeAsync(nextAction);
                    processed++;
                }
            }
            finally
            {
                lock (aiQuotaLock)
                {
                    aiQueueProcessorRunning = false;
                }
            }
        }

        private static bool TryTakeNextQueuedAiAction(out Func<Task> action)
        {
            action = static () => Task.CompletedTask;

            if (IsAiTemporarilyDisabledForPhoneInactivity())
                return false;

            bool limitEnabled = IsAiUsageLimitEnabled();

            lock (aiQuotaLock)
            {
                if (highPriorityQueuedAiActions.Count == 0 && normalPriorityQueuedAiActions.Count == 0)
                    return false;

                if (limitEnabled && (AiCallsRemainingToday <= 0 || DailyAiUsageRemainingToday <= 0))
                    return false;

                QueuedAiAction nextAction = highPriorityQueuedAiActions.Count > 0
                    ? highPriorityQueuedAiActions.Dequeue()
                    : normalPriorityQueuedAiActions.Dequeue();

                if (!string.IsNullOrWhiteSpace(nextAction.QueueKey))
                    queuedAiActionKeys.Remove(nextAction.QueueKey);

                if (limitEnabled)
                {
                    AiCallsRemainingToday--;
                    DailyAiUsageRemainingToday--;
                }

                action = nextAction.Action;
                return true;
            }
        }

        private static async Task RunAiActionSafeAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SMonitor.Log($"Queued AI action failed: {ex}", LogLevel.Trace);
            }
        }

        private static int NormalizeTimeToHour(int rawTime)
        {
            if (rawTime <= 0)
                return 0;

            int hour = rawTime / 100;
            return Math.Clamp(hour, 0, 26);
        }
    }
}
