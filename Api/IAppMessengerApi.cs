using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SmartphoneAppMessenger
{
    public interface IAppMessengerApi
    {
        /// <summary>
        /// Registers a custom quick-action icon in the App Messenger chat quick-action menu (opened by the ^ button).
        /// The callback receives the currently selected NPC internal name.
        /// </summary>
        /// <param name="ownerModId">The unique ID of the mod that owns this quick action.</param>
        /// <param name="actionId">A unique quick-action ID within the owner mod.</param>
        /// <param name="iconTexture">Texture used as the quick-action icon.</param>
        /// <param name="onClick">Callback invoked when the quick-action icon is clicked. Receives selected NPC name.</param>
        /// <param name="closePhoneOnLaunch">Whether the phone menu should close before invoking <paramref name="onClick"/>.</param>
        /// <param name="sortOrder">Lower values are shown earlier in the quick-action stack.</param>
        /// <param name="sourceRect">Optional source rectangle if the icon is part of a spritesheet.</param>
        /// <param name="npcNames">Optional allowlist of NPC internal names. If provided, only these NPC names will show this quick action (e.g. "Abigail", "Lewis").</param>
        /// <returns>True if registration succeeded; otherwise false.</returns>
        bool RegisterChatQuickActionButton(
            string ownerModId,
            string actionId,
            Texture2D iconTexture,
            Action<string> onClick,
            bool closePhoneOnLaunch = false,
            int sortOrder = 0,
            Rectangle? sourceRect = null,
            List<string>? npcNames = null
        );

        /// <summary>
        /// Unregisters a previously registered App Messenger chat quick-action icon.
        /// </summary>
        /// <param name="ownerModId">The unique ID of the mod that owns this quick action.</param>
        /// <param name="actionId">The quick-action ID that was used during registration.</param>
        /// <returns>True if a quick-action icon was removed; otherwise false.</returns>
        bool UnregisterChatQuickActionButton(string ownerModId, string actionId);

        /// <summary>
        /// Registers or updates an event type that can be suggested by AI chat and scheduled through Smartphone.
        /// The <paramref name="eventType"/> value is used as the tool enum value, so keep it stable.
        /// </summary>
        /// <param name="ownerModId">The unique ID of the mod that owns this event registration.</param>
        /// <param name="eventType">Unique event key shown to the AI tool (for example: "Birthday").</param>
        /// <param name="triggerEvent">Callback invoked when Smartphone triggers this event for an NPC name.</param>
        /// <param name="minimumHeartLevel">Minimum heart level required before this event is exposed to AI tools.</param>
        /// <param name="toolDescription">Optional extra context appended to the Schedule_Event tool description.</param>
        /// <returns>True if registration succeeded; otherwise false.</returns>
        bool RegisterUnlimitedEvent(
            string ownerModId,
            string eventType,
            Action<string> triggerEvent,
            int minimumHeartLevel = 0,
            string toolDescription = ""
        );

        /// <summary>
        /// Unregisters a previously registered event type.
        /// </summary>
        /// <param name="ownerModId">The unique ID of the mod that owns this event registration.</param>
        /// <param name="eventType">The event key that was used during registration.</param>
        /// <returns>True if an event type was removed; otherwise false.</returns>
        bool UnregisterUnlimitedEvent(string ownerModId, string eventType);

        /// <summary>
        /// Gets a list of NPCs that have appear in the messenger app for a specific player.
        /// </summary>
        /// <param name="playerId">(optional) The target player's UniqueMultiplayerID as string. If provided and not a valid online player ID, returns an empty list.</param>
        /// <returns>A list of NPC names.</returns>
        List<string> GetPhoneNpcList(string playerId = "");

        /// <summary>
        /// Sends a message from an NPC to the player. This method is used to simulate receiving messages on the player's smartphone from NPCs in the game. Nothing will happen if the specified NPC is not in the messenger app list.
        /// </summary>
        /// <param name="npcName">The name of the NPC sending the message (case-sensitive).</param>
        /// <param name="message">The content of the message being sent.</param>
        /// <param name="playerId">(optional) The target player's UniqueMultiplayerID as string. If null/empty/invalid, this is broadcast to all online players.</param>
        void SendSmartphoneMessageFromNPC(string npcName, string message, string playerId = "");

        /// <summary>
        /// Sends a message from the player to an NPC. This method is used to simulate sending messages from the player's smartphone to NPCs in the game. Nothing will happen if the specified NPC is not in the messenger app list.
        /// </summary>
        /// <param name="npcName">The name of the NPC receiving the message (case-sensitive).</param>
        /// <param name="message">The content of the message being sent.</param>
        /// <param name="playerId">(optional) The target player's UniqueMultiplayerID as string. If null/empty/invalid, this is broadcast to all online players.</param>
        void SendSmartphoneMessageFromPlayer(string npcName, string message, string playerId = "");
    }
}
