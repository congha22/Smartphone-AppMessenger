using StardewModdingAPI;

namespace SmartphoneAppMessenger
{
    internal sealed class RegisteredUnlimitedEvent
    {
        public string OwnerModId { get; init; } = "";
        public string EventType { get; init; } = "";
        public int MinimumHeartLevel { get; init; }
        public string ToolDescription { get; init; } = "";
        public Action<string> TriggerEvent { get; init; } = null!;

        public string CompositeId => BuildCompositeId(this.OwnerModId, this.EventType);

        public static string BuildCompositeId(string ownerModId, string eventType)
        {
            return $"{ownerModId.Trim()}::{eventType.Trim()}";
        }
    }

    public partial class ModEntry
    {
        private static readonly object RegisteredUnlimitedEventsLock = new();
        private static readonly Dictionary<string, RegisteredUnlimitedEvent> RegisteredUnlimitedEventsByType = new(StringComparer.OrdinalIgnoreCase);

        internal static bool RegisterUnlimitedEventInternal(
            string ownerModId,
            string eventType,
            Action<string> triggerEvent,
            int minimumHeartLevel,
            string toolDescription)
        {
            if (string.IsNullOrWhiteSpace(ownerModId)
                || string.IsNullOrWhiteSpace(eventType)
                || triggerEvent == null)
            {
                SMonitor?.Log("RegisterUnlimitedEvent failed: ownerModId, eventType, and triggerEvent are required.", LogLevel.Warn);
                return false;
            }

            string normalizedOwnerModId = ownerModId.Trim();
            string normalizedEventType = eventType.Trim();
            int normalizedMinimumHeartLevel = Math.Max(0, minimumHeartLevel);

            var registration = new RegisteredUnlimitedEvent
            {
                OwnerModId = normalizedOwnerModId,
                EventType = normalizedEventType,
                MinimumHeartLevel = normalizedMinimumHeartLevel,
                ToolDescription = toolDescription?.Trim() ?? "",
                TriggerEvent = triggerEvent
            };

            bool replaced;
            lock (RegisteredUnlimitedEventsLock)
            {
                replaced = RegisteredUnlimitedEventsByType.TryGetValue(normalizedEventType, out var existing);
                if (replaced
                    && existing != null
                    && !string.Equals(existing.OwnerModId, normalizedOwnerModId, StringComparison.OrdinalIgnoreCase))
                {
                    SMonitor?.Log(
                        $"RegisterUnlimitedEvent failed: event type '{normalizedEventType}' is already owned by '{existing.OwnerModId}'.",
                        LogLevel.Warn);
                    return false;
                }

                RegisteredUnlimitedEventsByType[normalizedEventType] = registration;
            }

            SMonitor?.Log(
                replaced
                    ? $"Updated unlimited event registration '{registration.CompositeId}'."
                    : $"Registered unlimited event '{registration.CompositeId}'.",
                LogLevel.Trace);

            return true;
        }

        internal static bool UnregisterUnlimitedEventInternal(string ownerModId, string eventType)
        {
            if (string.IsNullOrWhiteSpace(ownerModId) || string.IsNullOrWhiteSpace(eventType))
                return false;

            string normalizedOwnerModId = ownerModId.Trim();
            string normalizedEventType = eventType.Trim();

            bool removed = false;
            lock (RegisteredUnlimitedEventsLock)
            {
                if (!RegisteredUnlimitedEventsByType.TryGetValue(normalizedEventType, out var existing) || existing == null)
                    return false;

                if (!string.Equals(existing.OwnerModId, normalizedOwnerModId, StringComparison.OrdinalIgnoreCase))
                {
                    SMonitor?.Log(
                        $"UnregisterUnlimitedEvent denied for '{normalizedOwnerModId}::{normalizedEventType}' because it is owned by '{existing.OwnerModId}'.",
                        LogLevel.Warn);
                    return false;
                }

                removed = RegisteredUnlimitedEventsByType.Remove(normalizedEventType);
            }

            if (removed)
                SMonitor?.Log($"Unregistered unlimited event '{normalizedOwnerModId}::{normalizedEventType}'.", LogLevel.Trace);

            return removed;
        }

        internal static List<RegisteredUnlimitedEvent> GetRegisteredUnlimitedEventsForHeartLevel(int heartLevel)
        {
            lock (RegisteredUnlimitedEventsLock)
            {
                return RegisteredUnlimitedEventsByType.Values
                    .Where(evt => heartLevel >= evt.MinimumHeartLevel)
                    .OrderBy(evt => evt.MinimumHeartLevel)
                    .ThenBy(evt => evt.EventType, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
        }

        internal static bool TryGetRegisteredUnlimitedEvent(string eventType, out RegisteredUnlimitedEvent? registeredEvent)
        {
            registeredEvent = null;
            if (string.IsNullOrWhiteSpace(eventType))
                return false;

            string normalizedEventType = eventType.Trim();
            lock (RegisteredUnlimitedEventsLock)
            {
                return RegisteredUnlimitedEventsByType.TryGetValue(normalizedEventType, out registeredEvent);
            }
        }

        internal static bool TryTriggerRegisteredUnlimitedEvent(string eventType, string npcName)
        {
            if (!TryGetRegisteredUnlimitedEvent(eventType, out var registeredEvent) || registeredEvent == null)
                return false;

            try
            {
                registeredEvent.TriggerEvent(npcName);
                return true;
            }
            catch (Exception ex)
            {
                SMonitor?.Log($"Failed triggering unlimited event '{registeredEvent.EventType}' for '{npcName}': {ex}", LogLevel.Error);
                return false;
            }
        }
    }
}
