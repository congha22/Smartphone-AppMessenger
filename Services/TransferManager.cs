using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace SmartphoneAppMessenger
{
    public class TransferChunkMessage
    {
        public string TransferId { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string ReceiverName { get; set; } = "";
        public string DataType { get; set; } = ""; // "Text", "Photo", "Avatar"
        public string FileName { get; set; } = "";
        public int ChunkIndex { get; set; }
        public int TotalChunks { get; set; }
        public string ChunkData { get; set; } = "";
        public string SaveFolderName { get; set; } = "";
    }

    public class SendChunkJob
    {
        public TransferChunkMessage Message { get; set; } = null!;
        public long TargetPlayerId { get; set; }
    }

    public static class TransferManager
    {
        public static readonly Queue<SendChunkJob> SendQueue = new();
        private static readonly Dictionary<string, List<TransferChunkMessage>> IncomingTransfers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<long> PendingJoinedPeerIds = new();

        public static void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMultiplayer)
                return;

            // Process send queue (1 chunk per tick)
            if (SendQueue.Count > 0)
            {
                var job = SendQueue.Dequeue();
                try
                {
                    ModEntry.Instance.Helper.Multiplayer.SendMessage(
                        job.Message,
                        "AppMessenger_TransferChunk",
                        new[] { ModEntry.Instance.ModManifest.UniqueID },
                        new[] { job.TargetPlayerId }
                    );
                }
                catch (Exception ex)
                {
                    ModEntry.SMonitor.Log($"Failed to send chunk: {ex}", LogLevel.Error);
                }
            }

            // Process pending player joins (on the host side)
            if (Context.IsMainPlayer)
            {
                ProcessPendingJoins();
            }
        }

        public static void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            lock (PendingJoinedPeerIds)
            {
                PendingJoinedPeerIds.Add(e.Peer.PlayerID);
            }
            ModEntry.SMonitor.Log($"Host detected peer join context (ID: {e.Peer.PlayerID}). Will queue avatars once farmer object is loaded.", LogLevel.Info);
        }

        private static void ProcessPendingJoins()
        {
            List<long> toRemove = new();
            lock (PendingJoinedPeerIds)
            {
                foreach (long peerId in PendingJoinedPeerIds)
                {
                    var farmer = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == peerId);
                    if (farmer != null && !string.IsNullOrWhiteSpace(farmer.Name))
                    {
                        QueueAllAvatarsToPlayer(farmer);
                        toRemove.Add(peerId);
                    }
                }
                foreach (long peerId in toRemove)
                {
                    PendingJoinedPeerIds.Remove(peerId);
                }
            }
        }

        private static void QueueAllAvatarsToPlayer(Farmer target)
        {
            string activeSave = MessageManager.GetActiveSaveFolderName();
            string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
            if (!Directory.Exists(photoSharedDir))
                return;

            ModEntry.SMonitor.Log($"Queueing all existing avatars to newly joined player '{target.Name}'.", LogLevel.Info);
            foreach (string filePath in Directory.GetFiles(photoSharedDir))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName.Contains("_avatar", StringComparison.OrdinalIgnoreCase))
                {
                    QueueSend("Avatar", fileName, filePath, target.Name);
                }
            }
        }

        public static void QueueSend(string dataType, string fileName, string absoluteFilePath, string receiverName)
        {
            if (!File.Exists(absoluteFilePath))
            {
                ModEntry.SMonitor.Log($"Cannot transfer file: '{absoluteFilePath}' does not exist.", LogLevel.Warn);
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(absoluteFilePath);
                string base64 = Convert.ToBase64String(bytes);
                QueueSendBase64(dataType, fileName, base64, receiverName);
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed to read file for transfer queue: {ex}", LogLevel.Error);
            }
        }

        public static void QueueSendBase64(string dataType, string fileName, string base64Data, string receiverName)
        {
            var targetFarmer = Game1.getOnlineFarmers()
                .FirstOrDefault(f => string.Equals(f.Name, receiverName, StringComparison.OrdinalIgnoreCase));
            if (targetFarmer == null)
            {
                ModEntry.SMonitor.Log($"Cannot transfer data: target player '{receiverName}' is offline.", LogLevel.Warn);
                return;
            }

            long targetId = targetFarmer.UniqueMultiplayerID;
            string transferId = Guid.NewGuid().ToString("N");
            string senderName = Game1.player.Name;

            int chunkSize = 10000; // 10KB chunk size limit
            int totalChunks = (int)Math.Ceiling(base64Data.Length / (double)chunkSize);
            if (totalChunks == 0)
                totalChunks = 1;

            ModEntry.SMonitor.Log($"Queueing transfer of '{fileName}' ({dataType}) to {receiverName} in {totalChunks} chunks.", LogLevel.Info);

            for (int i = 0; i < totalChunks; i++)
            {
                int startIndex = i * chunkSize;
                int length = Math.Min(chunkSize, base64Data.Length - startIndex);
                string chunkData = length > 0 ? base64Data.Substring(startIndex, length) : "";

                var msg = new TransferChunkMessage
                {
                    TransferId = transferId,
                    SenderName = senderName,
                    ReceiverName = receiverName,
                    DataType = dataType,
                    FileName = fileName,
                    ChunkIndex = i,
                    TotalChunks = totalChunks,
                    ChunkData = chunkData,
                    SaveFolderName = MessageManager.GetActiveSaveFolderName()
                };

                SendQueue.Enqueue(new SendChunkJob
                {
                    Message = msg,
                    TargetPlayerId = targetId
                });
            }
        }

        public static void SendTextMessage(string receiverName, string text)
        {
            var targetFarmer = Game1.getOnlineFarmers()
                .FirstOrDefault(f => string.Equals(f.Name, receiverName, StringComparison.OrdinalIgnoreCase));
            if (targetFarmer == null)
            {
                ModEntry.SMonitor.Log($"Cannot send text: target player '{receiverName}' is offline.", LogLevel.Warn);
                return;
            }

            long targetId = targetFarmer.UniqueMultiplayerID;
            string transferId = Guid.NewGuid().ToString("N");

            var msg = new TransferChunkMessage
            {
                TransferId = transferId,
                SenderName = Game1.player.Name,
                ReceiverName = receiverName,
                DataType = "Text",
                FileName = "",
                ChunkIndex = 0,
                TotalChunks = 1,
                ChunkData = text,
                SaveFolderName = MessageManager.GetActiveSaveFolderName()
            };

            // Text message size is small, send right away bypassing queue
            try
            {
                ModEntry.Instance.Helper.Multiplayer.SendMessage(
                    msg,
                    "AppMessenger_TransferChunk",
                    new[] { ModEntry.Instance.ModManifest.UniqueID },
                    new[] { targetId }
                );
            }
            catch (Exception ex)
            {
                ModEntry.SMonitor.Log($"Failed to send direct text message: {ex}", LogLevel.Error);
            }
        }

        public static void SendSelectedAvatar(string destPath)
        {
            if (string.IsNullOrWhiteSpace(destPath) || !File.Exists(destPath))
                return;

            string fileName = Path.GetFileName(destPath);

            if (Context.IsMainPlayer)
            {
                // Host broadcasts to all other online players
                foreach (var farmer in Game1.getOnlineFarmers())
                {
                    if (farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                    {
                        QueueSend("Avatar", fileName, destPath, farmer.Name);
                    }
                }
            }
            else
            {
                // Farmhand sends to host
                if (Game1.MasterPlayer != null)
                {
                    QueueSend("Avatar", fileName, destPath, Game1.MasterPlayer.Name);
                }
            }
        }

        public static void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != ModEntry.Instance.ModManifest.UniqueID)
                return;

            if (e.Type == "AppMessenger_TransferChunk")
            {
                TransferChunkMessage? chunk = e.ReadAs<TransferChunkMessage>();
                if (chunk == null)
                    return;

                // Only process messages destined for this player
                if (!string.Equals(chunk.ReceiverName, Game1.player.Name, StringComparison.OrdinalIgnoreCase))
                    return;

                if (chunk.TotalChunks <= 1)
                {
                    ProcessCompletedTransfer(chunk.SenderName, chunk.DataType, chunk.FileName, chunk.ChunkData, chunk.SaveFolderName);
                }
                else
                {
                    if (!IncomingTransfers.TryGetValue(chunk.TransferId, out var chunks))
                    {
                        chunks = new List<TransferChunkMessage>();
                        IncomingTransfers[chunk.TransferId] = chunks;
                    }

                    chunks.Add(chunk);

                    if (chunks.Count >= chunk.TotalChunks)
                    {
                        var orderedChunks = chunks.OrderBy(c => c.ChunkIndex).ToList();
                        string fullBase64 = string.Concat(orderedChunks.Select(c => c.ChunkData));

                        ProcessCompletedTransfer(chunk.SenderName, chunk.DataType, chunk.FileName, fullBase64, chunk.SaveFolderName);
                        IncomingTransfers.Remove(chunk.TransferId);
                    }
                }
            }
        }

        private static void ProcessCompletedTransfer(string senderName, string dataType, string fileName, string data, string saveFolderName)
        {
            string activeSave = !string.IsNullOrWhiteSpace(saveFolderName)
                ? saveFolderName
                : MessageManager.GetActiveSaveFolderName();

            if (dataType == "Text")
            {
                MessageManager.AddPlayerMessage(senderName, data, isFromPlayer: false);
            }
            else if (dataType == "Photo")
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(data);
                    string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                    Directory.CreateDirectory(photoSharedDir);

                    string destPath = Path.Combine(photoSharedDir, fileName);
                    File.WriteAllBytes(destPath, bytes);
                }
                catch (Exception ex)
                {
                    ModEntry.SMonitor.Log($"Failed to process received photo: {ex}", LogLevel.Error);
                }
            }
            else if (dataType == "Avatar")
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(data);
                    string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                    Directory.CreateDirectory(photoSharedDir);

                    string destPath = Path.Combine(photoSharedDir, fileName);
                    File.WriteAllBytes(destPath, bytes);

                    // Host broadcasts this new avatar to all other connected farmhands
                    if (Context.IsMainPlayer)
                    {
                        foreach (var farmer in Game1.getOnlineFarmers())
                        {
                            if (farmer.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID &&
                                !string.Equals(farmer.Name, senderName, StringComparison.OrdinalIgnoreCase))
                            {
                                QueueSendBase64("Avatar", fileName, data, farmer.Name);
                            }
                        }
                    }

                    // Refresh avatar texture cache if app screen is currently open
                    if (Game1.activeClickableMenu is MessengerAppScreen appScreen)
                    {
                        MessengerAppScreen.ClearAvatarCache();
                    }
                }
                catch (Exception ex)
                {
                    ModEntry.SMonitor.Log($"Failed to process received avatar: {ex}", LogLevel.Error);
                }
            }
        }
    }
}
