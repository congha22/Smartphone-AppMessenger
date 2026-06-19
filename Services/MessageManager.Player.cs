using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace SmartphoneAppMessenger
{
    public partial class MessageManager
    {
        public static Dictionary<string, List<string>> PlayerConversations = new(StringComparer.OrdinalIgnoreCase);

        public static void LoadPlayerHistory(IModHelper helper)
        {
            PlayerConversations.Clear();
            if (!Context.IsWorldReady)
                return;

            string activeSave = GetActiveSaveFolderName();
            string folderPath = Path.Combine(helper.DirectoryPath, "userdata", activeSave);
            string filePath = Path.Combine(folderPath, "player_conversation.json");

            if (File.Exists(filePath))
            {
                try
                {
                    var loaded = helper.Data.ReadJsonFile<Dictionary<string, List<string>>>(Path.Combine("userdata", activeSave, "player_conversation.json"));
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded)
                        {
                            PlayerConversations[kvp.Key] = kvp.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log($"Failed to load player_conversation.json: {ex}", LogLevel.Error);
                }
            }
        }

        public static void SavePlayerHistory(IModHelper helper)
        {
            if (!Context.IsWorldReady)
                return;

            string activeSave = GetActiveSaveFolderName();
            string folderPath = Path.Combine(helper.DirectoryPath, "userdata", activeSave);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            try
            {
                helper.Data.WriteJsonFile(Path.Combine("userdata", activeSave, "player_conversation.json"), PlayerConversations);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Failed to save player_conversation.json: {ex}", LogLevel.Error);
            }
        }

        public static void AddPlayerMessage(string otherPlayerName, string message, bool isFromPlayer)
        {
            if (string.IsNullOrWhiteSpace(otherPlayerName) || string.IsNullOrWhiteSpace(message))
                return;

            if (!PlayerConversations.TryGetValue(otherPlayerName, out var list))
            {
                list = new List<string>();
                PlayerConversations[otherPlayerName] = list;
            }

            string formattedMessage;
            if (message.StartsWith("PlayerPhoto:", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("NpcPhoto:", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("SYSTEM:", StringComparison.OrdinalIgnoreCase))
            {
                formattedMessage = message;
            }
            else if (isFromPlayer)
            {
                formattedMessage = $"PLAYER: {message}";
            }
            else
            {
                formattedMessage = $"{otherPlayerName}: {message}";
            }

            list.Add(formattedMessage);
            TrimPlayerMessages(otherPlayerName);

            // Update metadata timestamps
            LatestMessageTimestamps[otherPlayerName] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (!isFromPlayer)
            {
                if (!UnreadCounts.ContainsKey(otherPlayerName))
                    UnreadCounts[otherPlayerName] = 0;
                UnreadCounts[otherPlayerName] = Math.Min(9, UnreadCounts[otherPlayerName] + 1);
            }

            SavePlayerHistory(ModEntry.Instance.Helper);
            SaveMetadata(ModEntry.Instance.Helper);
        }

        public static void TrimPlayerMessages(string otherPlayerName)
        {
            int maxMessages = GetMaxMessagesPerNpc();
            if (PlayerConversations.TryGetValue(otherPlayerName, out var list))
            {
                if (list.Count > maxMessages)
                {
                    list.RemoveRange(0, list.Count - maxMessages);
                }
            }
        }

        public static string? GetPlayerAvatarPath(string playerName)
        {
            long? foundId = null;
            var online = Game1.getOnlineFarmers().FirstOrDefault(f => string.Equals(f.Name, playerName, StringComparison.OrdinalIgnoreCase));
            if (online != null)
            {
                foundId = online.UniqueMultiplayerID;
            }
            else
            {
                foreach (var other in Game1.otherFarmers.Values)
                {
                    if (string.Equals(other.Name, playerName, StringComparison.OrdinalIgnoreCase))
                    {
                        foundId = other.UniqueMultiplayerID;
                        break;
                    }
                }
            }

            if (foundId.HasValue)
            {
                string activeSave = GetActiveSaveFolderName();
                string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                if (Directory.Exists(photoSharedDir))
                {
                    string[] files = Directory.GetFiles(photoSharedDir, $"{foundId.Value}_avatar.*");
                    if (files.Length > 0)
                        return files[0];
                }
            }

            return null;
        }
    }
}
