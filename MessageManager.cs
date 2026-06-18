using System;
using System.Collections.Generic;
using System.IO;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace SmartphoneAppMessenger
{
    public class MessengerMetadata
    {
        public List<string> FavouriteNpcs { get; set; } = new();
        public Dictionary<string, int> LatestMessageTimestamps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> UnreadCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }


    public class PlayerProfileData
    {
        public string Age { get; set; } = "Adult";
        public string BirthDate { get; set; } = "1";
        public string BirthSeason { get; set; } = "Spring";
        public string Profile { get; set; } = "";
        public string AvatarPath { get; set; } = "";
    }


    public partial class MessageManager
    {
        public static string currentPlayerProfile = string.Empty;
        public static string currentPlayerAge = "Adult";
        public static string currentPlayerBirthDate = "1";
        public static string currentPlayerBirthSeason = "Spring";
        public static string currentPlayerAvatar = string.Empty;
        public static Dictionary<string, List<string>> NpcMessagesToday = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, List<string>> NpcMessagesHistory = new(StringComparer.OrdinalIgnoreCase);
        public static List<string> FavouriteNpcs = new();
        public static Dictionary<string, int> LatestMessageTimestamps = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, int> UnreadCounts = new(StringComparer.OrdinalIgnoreCase);



        public static void AddMessage(string npcName, string message, string type = "response")
        {
            if (string.IsNullOrWhiteSpace(npcName) || string.IsNullOrWhiteSpace(message))
                return;

            LatestMessageTimestamps[npcName] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (type == "response")
            {
                if (!UnreadCounts.ContainsKey(npcName))
                    UnreadCounts[npcName] = 0;
                UnreadCounts[npcName] = Math.Min(9, UnreadCounts[npcName] + 1);
            }



            if (!NpcMessagesToday.ContainsKey(npcName))
            {
                NpcMessagesToday[npcName] = new List<string>();

                // Only add the date header if this is the first message today and they haven't chatted before?
                // The requirements say when they first chat, maybe just add the date.

                NpcMessagesToday[npcName].Add($"SYSTEM: ---{SDate.Now().DayOfWeek}, {SDate.Now().Season} {SDate.Now().Day:00}-Y{SDate.Now().Year}---");
            }

            string formattedMessage;
            if (type == "response")
                formattedMessage = $"{npcName}: {message}";
            else if (type == "sent")
                formattedMessage = $"PLAYER: {message}";
            else if (type == "system")
                formattedMessage = message.StartsWith("SYSTEM:") ? message : $"SYSTEM: {message}";
            else
                formattedMessage = message;


            NpcMessagesToday[npcName].Add(formattedMessage);
            TrimMessagesForNpc(npcName);
        }

        public static List<string> GetAvailableNpcNames()
        {
            List<string> npcNames = new List<string>();
            string[] ignoredNpcs = ModEntry.Config.IgnoredNpc
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .ToArray();

            foreach (NPC npc in Utility.getAllVillagers())
            {
                if (npc == null ||
                    npc.IsMonster ||
                    npc.IsInvisible ||
                    !npc.CanSocialize ||
                    ignoredNpcs.Contains(npc.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string req = ModEntry.Config.NpcMessageRequirement;
                bool meetsReq = false;

                if (string.Equals(req, ModConfig.NpcRequirementMeet, StringComparison.OrdinalIgnoreCase))
                {
                    meetsReq = Game1.player.friendshipData.ContainsKey(npc.Name);
                }
                else
                {
                    meetsReq = Game1.player.getFriendshipHeartLevelForNPC(npc.Name) >= 1;
                }

                if (meetsReq)
                {
                    npcNames.Add(npc.Name);
                }
            }

            return npcNames.Distinct().OrderBy(n => n).ToList();
        }

        public static bool IsNpcUnlocked(string npcName)
        {
            var availableNpcs = GetAvailableNpcNames();
            return availableNpcs.Contains(npcName, StringComparer.OrdinalIgnoreCase);
        }

        public static List<string> GetMessagesForNpc(string npcName)
        {
            var combined = new List<string>();
            if (NpcMessagesHistory.TryGetValue(npcName, out var history))
            {
                combined.AddRange(history);
            }
            if (NpcMessagesToday.TryGetValue(npcName, out var today))
            {
                combined.AddRange(today);
            }
            return combined;
        }

        public static void LoadMetadata(IModHelper helper)
        {
            FavouriteNpcs.Clear();
            LatestMessageTimestamps.Clear();
            UnreadCounts.Clear();

            if (!Context.IsWorldReady)
                return;

            string filePath = Path.Combine("userdata", GetActiveSaveFolderName(), "npc_conversation_metadata.json");
            try
            {
                var loaded = helper.Data.ReadJsonFile<MessengerMetadata>(filePath);
                if (loaded != null)
                {
                    FavouriteNpcs = loaded.FavouriteNpcs ?? new List<string>();
                    LatestMessageTimestamps = loaded.LatestMessageTimestamps ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    UnreadCounts = loaded.UnreadCounts ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                }
            }

            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Failed to load npc_conversation_metadata.json: {ex}", LogLevel.Error);
            }
        }

        public static void SaveMetadata(IModHelper helper)
        {
            if (!Context.IsWorldReady)
                return;

            string folderPath = Path.Combine(helper.DirectoryPath, "userdata", GetActiveSaveFolderName());
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            try
            {
                var data = new MessengerMetadata
                {
                    FavouriteNpcs = FavouriteNpcs,
                    LatestMessageTimestamps = LatestMessageTimestamps,
                    UnreadCounts = UnreadCounts
                };
                helper.Data.WriteJsonFile(Path.Combine("userdata", GetActiveSaveFolderName(), "npc_conversation_metadata.json"), data);

            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Failed to save npc_conversation_metadata.json: {ex}", LogLevel.Error);
            }
        }

        public static void LoadPlayerProfile(IModHelper helper)
        {
            currentPlayerAge = "Adult";
            currentPlayerBirthDate = "1";
            currentPlayerBirthSeason = "Spring";
            currentPlayerProfile = "";
            currentPlayerAvatar = "";

            if (!Context.IsWorldReady)
                return;

            string filePath = Path.Combine("userdata", GetActiveSaveFolderName(), "player_profile.json");
            try
            {
                var loaded = helper.Data.ReadJsonFile<PlayerProfileData>(filePath);
                if (loaded != null)
                {
                    currentPlayerAge = string.IsNullOrWhiteSpace(loaded.Age) ? "Adult" : loaded.Age;
                    currentPlayerBirthDate = string.IsNullOrWhiteSpace(loaded.BirthDate) ? "1" : loaded.BirthDate;
                    currentPlayerBirthSeason = string.IsNullOrWhiteSpace(loaded.BirthSeason) ? "Spring" : loaded.BirthSeason;
                    currentPlayerProfile = loaded.Profile ?? "";
                    currentPlayerAvatar = loaded.AvatarPath ?? "";
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Failed to load player_profile.json: {ex}", LogLevel.Error);
            }
        }

        public static void SavePlayerProfile(IModHelper helper)
        {
            if (!Context.IsWorldReady)
                return;

            try
            {
                var data = new PlayerProfileData
                {
                    Age = currentPlayerAge,
                    BirthDate = currentPlayerBirthDate,
                    BirthSeason = currentPlayerBirthSeason,
                    Profile = currentPlayerProfile,
                    AvatarPath = currentPlayerAvatar
                };
                helper.Data.WriteJsonFile(Path.Combine("userdata", GetActiveSaveFolderName(), "player_profile.json"), data);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Failed to save player_profile.json: {ex}", LogLevel.Error);
            }
        }

        public static void LoadHistory(IModHelper helper)
        {
            NpcMessagesHistory.Clear();
            NpcMessagesToday.Clear();

            if (!Context.IsWorldReady)
                return;

            LoadMetadata(helper);
            LoadPlayerProfile(helper);


            string folderPath = Path.Combine(helper.DirectoryPath, "userdata", GetActiveSaveFolderName());
            string filePath = Path.Combine(folderPath, "npc_conversation.json");

            if (File.Exists(filePath))
            {
                try
                {
                    var loaded = helper.Data.ReadJsonFile<Dictionary<string, List<string>>>(Path.Combine("userdata", GetActiveSaveFolderName(), "npc_conversation.json"));
                    if (loaded != null)
                    {
                        foreach (var kvp in loaded)
                        {
                            NpcMessagesHistory[kvp.Key] = kvp.Value;
                            TrimMessagesForNpc(kvp.Key);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log($"Failed to load npc_conversation.json: {ex}", LogLevel.Error);
                }
            }
        }

        public static void SaveAndResetDailyMessages(IModHelper helper)
        {
            if (!Context.IsWorldReady)
                return;

            // Merge today's messages into history
            foreach (var kvp in NpcMessagesToday)
            {
                if (!NpcMessagesHistory.ContainsKey(kvp.Key))
                {
                    NpcMessagesHistory[kvp.Key] = new List<string>();
                }
                NpcMessagesHistory[kvp.Key].AddRange(kvp.Value);
            }
            NpcMessagesToday.Clear();

            // Trim messages for all NPCs with chat history
            foreach (var npc in NpcMessagesHistory.Keys.ToList())
            {
                TrimMessagesForNpc(npc);
            }

            string folderPath = Path.Combine(helper.DirectoryPath, "userdata", GetActiveSaveFolderName());
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            try
            {
                helper.Data.WriteJsonFile(Path.Combine("userdata", GetActiveSaveFolderName(), "npc_conversation.json"), NpcMessagesHistory);
                SaveMetadata(helper);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Failed to save npc_conversation.json: {ex}", LogLevel.Error);
            }

        }

        private static string NormalizeSaveFolderName(string saveFolderName)
        {
            string normalizedValue = (saveFolderName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedValue))
                return string.Empty;

            char[] invalidChars = Path.GetInvalidFileNameChars();
            var builder = new System.Text.StringBuilder(normalizedValue.Length);
            foreach (char character in normalizedValue)
            {
                if (character == '/' || character == '\\' || Array.IndexOf(invalidChars, character) >= 0)
                    continue;

                builder.Append(character);
            }

            return builder.ToString().Trim();
        }

        public static string GetActiveSaveFolderName()
        {
            string constantsSaveFolder = NormalizeSaveFolderName(Constants.SaveFolderName);

            if (!string.IsNullOrWhiteSpace(constantsSaveFolder))
            {
                int underscoreIndex = constantsSaveFolder.IndexOf('_');
                if (underscoreIndex != -1)
                {
                    return constantsSaveFolder.Substring(underscoreIndex);
                }
            }

            long uniqueId = 0;
            if (Context.IsWorldReady && Context.IsMultiplayer && Game1.MasterPlayer != null)
                uniqueId = Game1.MasterPlayer.UniqueMultiplayerID;
            else if (Context.IsWorldReady && Game1.player != null)
                uniqueId = Game1.player.UniqueMultiplayerID;

            if (uniqueId > 0)
                return $"_{uniqueId}";

            if (!string.IsNullOrWhiteSpace(constantsSaveFolder))
            {
                int lastUnderscore = constantsSaveFolder.LastIndexOf('_');
                if (lastUnderscore >= 0 && lastUnderscore < constantsSaveFolder.Length - 1)
                {
                    string possibleId = constantsSaveFolder.Substring(lastUnderscore + 1);
                    if (long.TryParse(possibleId, out _))
                        return $"_{possibleId}";
                }
                return constantsSaveFolder;
            }

            return "default";
        }

        public static int GetMaxMessagesPerNpc()
        {
            int configuredMax = ModEntry.Config?.MaxMessage ?? 500;
            return Math.Max(1, configuredMax);
        }

        public static void TrimMessageListToMax(List<string> messages, int maxMessages)
        {
            if (messages == null || messages.Count <= maxMessages)
                return;

            int removeCount = messages.Count - maxMessages;
            messages.RemoveRange(0, removeCount);
        }

        public static void TrimMessagesForNpc(string npc)
        {
            int maxMessages = GetMaxMessagesPerNpc();

            NpcMessagesHistory.TryGetValue(npc, out List<string>? historicalMessages);
            NpcMessagesToday.TryGetValue(npc, out List<string>? todaysMessages);

            int totalCount = (historicalMessages?.Count ?? 0) + (todaysMessages?.Count ?? 0);
            int overflow = totalCount - maxMessages;
            if (overflow <= 0)
                return;

            if (historicalMessages != null && historicalMessages.Count > 0)
            {
                int removeFromHistorical = Math.Min(overflow, historicalMessages.Count);
                historicalMessages.RemoveRange(0, removeFromHistorical);
                overflow -= removeFromHistorical;
            }

            if (overflow > 0 && todaysMessages != null && todaysMessages.Count > 0)
            {
                int removeFromToday = Math.Min(overflow, todaysMessages.Count);
                todaysMessages.RemoveRange(0, removeFromToday);
            }
        }

        public static void EnforcePhotoSharedRetention(string directoryPath, int maxPhotos)
        {
            try
            {
                if (!Directory.Exists(directoryPath))
                    return;

                var files = new List<FileInfo>();
                foreach (string filePath in Directory.GetFiles(directoryPath))
                {
                    if (Path.GetFileName(filePath).Contains("_avatar", StringComparison.OrdinalIgnoreCase))
                        continue;
                    files.Add(new FileInfo(filePath));
                }

                if (files.Count <= maxPhotos)
                    return;

                // Sort by last write time ascending (oldest first)
                files.Sort((a, b) => a.LastWriteTime.CompareTo(b.LastWriteTime));

                int deleteCount = files.Count - maxPhotos;
                for (int i = 0; i < deleteCount; i++)
                {
                    try
                    {
                        files[i].Delete();
                    }
                    catch (Exception ex)
                    {
                        ModEntry.SMonitor.Log($"Failed to delete old photo file {files[i].FullName}: {ex.Message}", LogLevel.Warn);
                    }
                }
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Error enforcing photo shared retention in {directoryPath}: {ex.Message}", LogLevel.Error);
            }
        }
    }
}
