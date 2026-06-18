using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SmartphoneAppMessenger
{
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

        public List<string> GetPhoneNpcList(string playerId = "")
        {
            // Only returns the list for the current player for now, since it is a local mod
            return MessageManager.GetAvailableNpcNames();
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
