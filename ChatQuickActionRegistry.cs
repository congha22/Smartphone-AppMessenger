using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;

namespace SmartphoneAppMessenger
{
    internal sealed class RegisteredChatQuickActionButton
    {
        public string OwnerModId { get; init; } = "";
        public string ActionId { get; init; } = "";
        public Texture2D IconTexture { get; init; } = null!;
        public Action<string> OnClick { get; init; } = null!;
        public bool ClosePhoneOnLaunch { get; init; }
        public int SortOrder { get; init; }
        public Rectangle? SourceRect { get; init; }
        public HashSet<string>? AllowedNpcNames { get; init; }

        public string CompositeId => BuildCompositeId(this.OwnerModId, this.ActionId);

        public static string BuildCompositeId(string ownerModId, string actionId)
        {
            return $"{ownerModId.Trim()}::{actionId.Trim()}";
        }
    }

    public partial class ModEntry
    {
        private static readonly object ChatQuickActionLock = new();
        private static readonly Dictionary<string, RegisteredChatQuickActionButton> RegisteredChatQuickActionButtons = new(StringComparer.OrdinalIgnoreCase);

        internal static bool RegisterChatQuickActionButtonInternal(
            string ownerModId,
            string actionId,
            Texture2D iconTexture,
            Action<string> onClick,
            bool closePhoneOnLaunch,
            int sortOrder,
            Rectangle? sourceRect,
            List<string>? npcNames)
        {
            if (string.IsNullOrWhiteSpace(ownerModId)
                || string.IsNullOrWhiteSpace(actionId)
                || iconTexture == null
                || onClick == null)
            {
                SMonitor?.Log("RegisterChatQuickActionButton failed: ownerModId, actionId, iconTexture, and onClick are required.", LogLevel.Warn);
                return false;
            }

            if (sourceRect.HasValue && (sourceRect.Value.Width <= 0 || sourceRect.Value.Height <= 0))
            {
                SMonitor?.Log($"RegisterChatQuickActionButton failed for '{ownerModId}:{actionId}': sourceRect must have positive width and height.", LogLevel.Warn);
                return false;
            }

            HashSet<string>? allowedNpcNames = null;
            if (npcNames != null)
            {
                var sanitizedNames = npcNames
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name.Trim())
                    .ToList();

                if (sanitizedNames.Count > 0)
                    allowedNpcNames = new HashSet<string>(sanitizedNames, StringComparer.OrdinalIgnoreCase);
            }

            string key = RegisteredChatQuickActionButton.BuildCompositeId(ownerModId, actionId);
            var action = new RegisteredChatQuickActionButton
            {
                OwnerModId = ownerModId.Trim(),
                ActionId = actionId.Trim(),
                IconTexture = iconTexture,
                OnClick = onClick,
                ClosePhoneOnLaunch = closePhoneOnLaunch,
                SortOrder = sortOrder,
                SourceRect = sourceRect,
                AllowedNpcNames = allowedNpcNames
            };

            bool replaced;
            lock (ChatQuickActionLock)
            {
                replaced = RegisteredChatQuickActionButtons.ContainsKey(key);
                RegisteredChatQuickActionButtons[key] = action;
            }

            SMonitor?.Log(
                replaced
                    ? $"Updated chat quick-action button '{key}'."
                    : $"Registered chat quick-action button '{key}'.",
                LogLevel.Trace);
            return true;
        }

        internal static bool UnregisterChatQuickActionButtonInternal(string ownerModId, string actionId)
        {
            if (string.IsNullOrWhiteSpace(ownerModId) || string.IsNullOrWhiteSpace(actionId))
                return false;

            string key = RegisteredChatQuickActionButton.BuildCompositeId(ownerModId, actionId);

            bool removed;
            lock (ChatQuickActionLock)
                removed = RegisteredChatQuickActionButtons.Remove(key);

            if (removed)
                SMonitor?.Log($"Unregistered chat quick-action button '{key}'.", LogLevel.Trace);

            return removed;
        }

        internal static List<RegisteredChatQuickActionButton> GetRegisteredChatQuickActionButtonsSnapshot(string selectedNpcName)
        {
            if (string.IsNullOrWhiteSpace(selectedNpcName))
                return new List<RegisteredChatQuickActionButton>();

            List<RegisteredChatQuickActionButton> snapshot;
            lock (ChatQuickActionLock)
            {
                snapshot = RegisteredChatQuickActionButtons.Values
                    .OrderBy(p => p.SortOrder)
                    .ThenBy(p => p.CompositeId, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var visibleActions = new List<RegisteredChatQuickActionButton>();
            foreach (RegisteredChatQuickActionButton action in snapshot)
            {
                if (action.AllowedNpcNames == null || action.AllowedNpcNames.Contains(selectedNpcName))
                    visibleActions.Add(action);
            }

            return visibleActions;
        }
    }
}
