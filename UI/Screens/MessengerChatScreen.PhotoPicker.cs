using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace SmartphoneAppMessenger
{
    public partial class MessengerChatScreen : IClickableMenu
    {
        // Dimensions & Constants
        private const int ChatPhotoPickerMaxCount = 3;
        private const int ChatImageMaxWidthBase = 320;
        private const int ChatImageMaxHeightBase = 300;
        private const float ChatImageScale = 0.7f;

        private int ChatImageMaxWidth => Math.Max(1, ScaleValue(ChatImageMaxWidthBase));
        private int ChatImageMaxHeight => Math.Max(1, ScaleValue(ChatImageMaxHeightBase));

        // Photo Picker UI State
        private bool chatPhotoPickerOpen = false;
        private readonly List<string> chatPhotoCandidates = new();
        private int chatPhotoCandidateIndex = -1;
        private readonly List<string> chatSelectedPhotos = new();

        private Rectangle chatPhotoPickerPrevBounds;
        private Rectangle chatPhotoPickerNextBounds;
        private Rectangle chatPhotoPickerToggleBounds;
        private Rectangle chatPhotoPickerCancelBounds;
        private Rectangle chatPhotoPickerSendBounds;

        private readonly List<ChatPhotoNavigationEntry> chatPhotoNavigationEntries = new();
        private readonly List<ChatPhotoHoverEntry> chatPhotoHoverEntries = new();
        private readonly Dictionary<string, int> chatPhotoGroupIndices = new();
        private static readonly Dictionary<string, Texture2D> chatImageCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> chatFailedImagePaths = new(StringComparer.OrdinalIgnoreCase);

        internal static void ClearChatImageCache()
        {
            foreach (var texture in chatImageCache.Values)
            {
                if (texture != null && !texture.IsDisposed)
                {
                    try { texture.Dispose(); } catch { }
                }
            }
            chatImageCache.Clear();
            chatFailedImagePaths.Clear();
        }

        // Nested helper types
        private class ChatPhotoNavigationEntry
        {
            public string GroupId { get; set; } = "";
            public int PhotoCount { get; set; }
            public Rectangle PreviousBounds { get; set; }
            public Rectangle NextBounds { get; set; }
        }

        private class ChatPhotoHoverEntry
        {
            public Rectangle Bounds { get; set; }
            public string TagText { get; set; } = "";
        }

        // Methods to position components relative to phone
        private int PhoneX(int baseOffset) => this.xPositionOnScreen + ScaleValue(baseOffset);
        private int PhoneY(int baseOffset) => this.yPositionOnScreen + ScaleValue(baseOffset);
        private Rectangle PhoneRect(int baseX, int baseY, int baseWidth, int baseHeight) =>
            new Rectangle(PhoneX(baseX), PhoneY(baseY), ScaleValue(baseWidth), ScaleValue(baseHeight));

        private Rectangle GetUiViewportBounds()
        {
            int viewportWidth = Math.Max(1, Game1.uiViewport.Width);
            int viewportHeight = Math.Max(1, Game1.uiViewport.Height);
            return new Rectangle(0, 0, viewportWidth, viewportHeight);
        }



        private static string GetPhotoTag(string photoPath)
        {
            string fileName = Path.GetFileName(photoPath);
            if (string.IsNullOrWhiteSpace(fileName) || iSmartphoneApi == null)
                return "";

            try
            {
                string metadataJson = iSmartphoneApi.GetPlayerPhotoMetadata(fileName);
                if (string.IsNullOrWhiteSpace(metadataJson))
                    return "";

                var obj = Newtonsoft.Json.Linq.JObject.Parse(metadataJson);
                return obj["tag"]?.ToString() ?? "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        private static readonly string PlayerPhotoPrefix = "PlayerPhoto:";
        private static readonly string PlayerPhotoTagPrefix = "PlayerPhotoTag:";
        private static readonly string NpcPhotoPrefix = "NpcPhoto:";
        private static readonly string NpcPhotoTagPrefix = "NpcPhotoTag:";

        private static bool TryParseChatPhotoMessage(string rawMessage, out bool isPlayerPhoto, out List<string> photoPaths)
        {
            string safeMessage = rawMessage ?? "";
            if (safeMessage.StartsWith(PlayerPhotoPrefix, StringComparison.OrdinalIgnoreCase))
            {
                isPlayerPhoto = true;
                photoPaths = safeMessage.Substring(PlayerPhotoPrefix.Length)
                    .Split("||", StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(path, ".jpg") : path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return photoPaths.Count > 0;
            }

            if (safeMessage.StartsWith(NpcPhotoPrefix, StringComparison.OrdinalIgnoreCase))
            {
                isPlayerPhoto = false;
                photoPaths = safeMessage.Substring(NpcPhotoPrefix.Length)
                    .Split("||", StringSplitOptions.RemoveEmptyEntries)
                    .Select(path => path.Trim())
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(path, ".jpg") : path)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return photoPaths.Count > 0;
            }

            isPlayerPhoto = false;
            photoPaths = new List<string>();
            return false;
        }

        private static bool TryParseChatPhotoTagMessage(string rawMessage, bool? expectedPlayerTag, out string tagText)
        {
            string safeMessage = rawMessage ?? "";
            if (safeMessage.StartsWith(PlayerPhotoTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (expectedPlayerTag.HasValue && !expectedPlayerTag.Value)
                {
                    tagText = "";
                    return false;
                }

                tagText = safeMessage.Substring(PlayerPhotoTagPrefix.Length).Trim();
                return true;
            }

            if (safeMessage.StartsWith(NpcPhotoTagPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (expectedPlayerTag.HasValue && expectedPlayerTag.Value)
                {
                    tagText = "";
                    return false;
                }

                tagText = safeMessage.Substring(NpcPhotoTagPrefix.Length).Trim();
                return true;
            }

            tagText = "";
            return false;
        }

        private void OpenChatPhotoPicker()
        {
            this.isAttachmentMenuOpen = false;

            bool isPlayer = Game1.getOnlineFarmers().Any(f => string.Equals(f.Name, this.npcName, StringComparison.OrdinalIgnoreCase))
                            || MessageManager.PlayerConversations.ContainsKey(this.npcName);

            // NPC chat only needs metadata; P2P needs raw textures
            bool getTexture = isPlayer;
            bool getMetadata = !isPlayer;

            // Capture local fields for closure
            var api = this.smartphoneApi;
            string targetNpc = this.npcName;
            var backAction = this.onBack;

            // Close current menu so the phone screen displays selection interface
            Game1.activeClickableMenu = null;

            api.RetrievePhotos(limit: 3, getTexture: getTexture, getMetadata: getMetadata, onComplete: (jsonString) =>
            {
                // Reopen the texting screen
                Game1.activeClickableMenu = new MessengerChatScreen(api, targetNpc, backAction);

                List<SelectedPhotoResult>? results = null;
                try
                {
                    results = string.IsNullOrWhiteSpace(jsonString)
                        ? null
                        : Newtonsoft.Json.JsonConvert.DeserializeObject<List<SelectedPhotoResult>>(jsonString);
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log($"Failed to deserialize photo results: {ex.Message}", LogLevel.Error);
                }

                if (results == null || results.Count == 0)
                {
                    return;
                }

                if (isPlayer)
                {
                    // P2P: retrieve texture raw bytes, write to shared directory, queue transmission, and send message references
                    string activeSave = MessageManager.GetActiveSaveFolderName();
                    string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                    Directory.CreateDirectory(photoSharedDir);

                    List<string> relativePaths = new List<string>();

                    foreach (var result in results)
                    {
                        if (result.TextureData != null)
                        {
                            try
                            {
                                string extension = Path.GetExtension(result.FileName);
                                if (string.IsNullOrEmpty(extension)) extension = ".jpg";
                                string destFileName = Guid.NewGuid().ToString("N") + extension;
                                string destPath = Path.Combine(photoSharedDir, destFileName);

                                File.WriteAllBytes(destPath, result.TextureData);

                                string relativePath = "photo_shared/" + destFileName;
                                relativePaths.Add(relativePath);

                                TransferManager.QueueSend("Photo", destFileName, destPath, targetNpc);
                            }
                            catch (Exception ex)
                            {
                                ModEntry.Instance.Monitor.Log($"Failed to write shared photo: {ex.Message}", LogLevel.Error);
                            }
                        }
                    }

                    if (relativePaths.Count > 0)
                    {
                        string localPhotoMsg = "PlayerPhoto: " + string.Join("||", relativePaths);
                        MessageManager.AddPlayerMessage(targetNpc, localPhotoMsg, isFromPlayer: true);

                        string remotePhotoMsg = "NpcPhoto: " + string.Join("||", relativePaths);
                        TransferManager.SendTextMessage(targetNpc, remotePhotoMsg);
                    }
                }
                else
                {
                    // NPC: retrieve metadata and send raw messages with tag references
                    List<string> absolutePaths = results.Select(r => r.AbsolutePath).ToList();
                    string photoMsg = "PlayerPhoto: " + string.Join("||", absolutePaths);
                    MessageManager.AddMessage(targetNpc, photoMsg, type: "raw");

                    List<string> tagList = results.Select(r => r.Tag).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
                    string tagsCombined = string.Join("; ", tagList);
                    string tagMsg = "PlayerPhotoTag: " + tagsCombined;
                    MessageManager.AddMessage(targetNpc, tagMsg, type: "raw");

                    string promptArg = $"[Attached photo tags: {tagsCombined}]";
                    ModEntry.QueueUserMessage(targetNpc, promptArg);
                }

                // Force layout update and scroll to bottom
                if (Game1.activeClickableMenu is MessengerChatScreen chatScreen)
                {
                    chatScreen.RebuildChatBubbles();
                }
            });
        }

        private void CloseChatPhotoPicker(bool clearSelection)
        {
            this.chatPhotoPickerOpen = false;
            this.chatPhotoPickerPrevBounds = Rectangle.Empty;
            this.chatPhotoPickerNextBounds = Rectangle.Empty;
            this.chatPhotoPickerToggleBounds = Rectangle.Empty;
            this.chatPhotoPickerCancelBounds = Rectangle.Empty;
            this.chatPhotoPickerSendBounds = Rectangle.Empty;

            if (clearSelection)
            {
                this.chatSelectedPhotos.Clear();
                this.chatPhotoCandidates.Clear();
                this.chatPhotoCandidateIndex = -1;
            }
        }


        private void MoveChatPhotoCandidate(int delta)
        {
            if (this.chatPhotoCandidates.Count == 0)
            {
                this.chatPhotoCandidateIndex = -1;
                return;
            }

            this.chatPhotoCandidateIndex += delta;
            if (this.chatPhotoCandidateIndex < 0)
                this.chatPhotoCandidateIndex = this.chatPhotoCandidates.Count - 1;
            else if (this.chatPhotoCandidateIndex >= this.chatPhotoCandidates.Count)
                this.chatPhotoCandidateIndex = 0;
        }

        private void ToggleCurrentChatPhotoSelection()
        {
            if (this.chatPhotoCandidateIndex < 0 || this.chatPhotoCandidateIndex >= this.chatPhotoCandidates.Count)
                return;

            string candidatePath = this.chatPhotoCandidates[this.chatPhotoCandidateIndex];
            int existingIndex = this.chatSelectedPhotos.FindIndex(path => string.Equals(path, candidatePath, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                this.chatSelectedPhotos.RemoveAt(existingIndex);
                return;
            }

            if (this.chatSelectedPhotos.Count >= ChatPhotoPickerMaxCount)
                return;

            this.chatSelectedPhotos.Add(candidatePath);
        }

        private bool IsChatPhotoSelected(string imagePath)
        {
            return this.chatSelectedPhotos.Any(path => string.Equals(path, imagePath, StringComparison.OrdinalIgnoreCase));
        }

        private bool TryHandleChatPhotoNavigationClick(int x, int y)
        {
            foreach (ChatPhotoNavigationEntry navEntry in this.chatPhotoNavigationEntries)
            {
                if (navEntry.PhotoCount <= 1)
                    continue;

                if (navEntry.PreviousBounds.Contains(x, y))
                {
                    int currentIndex = GetChatPhotoGroupIndex(navEntry.GroupId, navEntry.PhotoCount);
                    int nextIndex = currentIndex - 1;
                    if (nextIndex < 0)
                        nextIndex = navEntry.PhotoCount - 1;

                    this.chatPhotoGroupIndices[navEntry.GroupId] = nextIndex;
                    Game1.playSound("shwip");
                    return true;
                }

                if (navEntry.NextBounds.Contains(x, y))
                {
                    int currentIndex = GetChatPhotoGroupIndex(navEntry.GroupId, navEntry.PhotoCount);
                    int nextIndex = (currentIndex + 1) % navEntry.PhotoCount;

                    this.chatPhotoGroupIndices[navEntry.GroupId] = nextIndex;
                    Game1.playSound("shwip");
                    return true;
                }
            }

            return false;
        }

        private void HandleChatPhotoPickerClick(int x, int y)
        {
            if (this.chatPhotoPickerPrevBounds.Contains(x, y))
            {
                MoveChatPhotoCandidate(-1);
                Game1.playSound("shwip");
                return;
            }

            if (this.chatPhotoPickerNextBounds.Contains(x, y))
            {
                MoveChatPhotoCandidate(1);
                Game1.playSound("shwip");
                return;
            }

            if (this.chatPhotoPickerToggleBounds.Contains(x, y))
            {
                ToggleCurrentChatPhotoSelection();
                Game1.playSound("smallSelect");
                return;
            }

            if (this.chatPhotoPickerCancelBounds.Contains(x, y))
            {
                CloseChatPhotoPicker(clearSelection: true);
                Game1.playSound("bigDeSelect");
                return;
            }

            if (this.chatPhotoPickerSendBounds.Contains(x, y))
            {
                bool confirmed = TryConfirmChatPhotoSelection();
                Game1.playSound(confirmed ? "smallSelect" : "cancel");

                if (confirmed)
                    CloseChatPhotoPicker(clearSelection: false);
            }
        }

        private bool TryConfirmChatPhotoSelection()
        {
            List<string> validPhotos = this.chatSelectedPhotos
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Take(ChatPhotoPickerMaxCount)
                .ToList();

            this.chatSelectedPhotos.Clear();
            this.chatSelectedPhotos.AddRange(validPhotos);
            return true;
        }

        private void DrawChatPhotoPickerMenu(SpriteBatch b)
        {
            b.Draw(Game1.staminaRect, GetUiViewportBounds(), Color.Black * 0.35f);

            Rectangle panelBounds = PhoneRect(65, 180, 470, 600);
            UI.CardDrawing.DrawCard(
                b,
                panelBounds.X,
                panelBounds.Y,
                panelBounds.Width,
                panelBounds.Height,
                new Color(255, 255, 255, 240),
                1f,
                false);

            string title = $"Send photos ({this.chatSelectedPhotos.Count}/{ChatPhotoPickerMaxCount})";
            DrawPhoneText(
                b,
                Game1.dialogueFont,
                title,
                new Vector2(panelBounds.X + ScaleValue(20), panelBounds.Y + ScaleValue(14)),
                Color.Black);

            this.chatPhotoPickerPrevBounds = Rectangle.Empty;
            this.chatPhotoPickerNextBounds = Rectangle.Empty;
            this.chatPhotoPickerToggleBounds = Rectangle.Empty;
            this.chatPhotoPickerCancelBounds = new Rectangle(
                panelBounds.Right - ScaleValue(190),
                panelBounds.Bottom - ScaleValue(80),
                ScaleValue(96),
                ScaleValue(48));
            this.chatPhotoPickerSendBounds = new Rectangle(
                panelBounds.Right - ScaleValue(84),
                panelBounds.Bottom - ScaleValue(86),
                ScaleValue(64),
                ScaleValue(64));

            Rectangle previewBounds = new Rectangle(
                panelBounds.X + ScaleValue(30),
                panelBounds.Y + ScaleValue(80),
                panelBounds.Width - ScaleValue(60),
                ScaleValue(330));
            UI.CardDrawing.DrawCard(
                b,
                previewBounds.X,
                previewBounds.Y,
                previewBounds.Width,
                previewBounds.Height,
                new Color(255, 255, 255, 220),
                1f,
                false);

            if (this.chatPhotoCandidates.Count == 0)
            {
                DrawPhoneText(
                    b,
                    Game1.smallFont,
                    "No photos found.",
                    new Vector2(previewBounds.X + ScaleValue(20), previewBounds.Y + ScaleValue(20)),
                    Color.Black);
            }
            else
            {
                this.chatPhotoCandidateIndex = Math.Clamp(this.chatPhotoCandidateIndex, 0, this.chatPhotoCandidates.Count - 1);
                string currentPath = this.chatPhotoCandidates[this.chatPhotoCandidateIndex];

                if (TryGetChatImageTexture(currentPath, out Texture2D previewTexture))
                {
                    float scale = Math.Min(
                        (previewBounds.Width - 20) / (float)Math.Max(1, previewTexture.Width),
                        (previewBounds.Height - 20) / (float)Math.Max(1, previewTexture.Height));
                    scale = Math.Clamp(scale, 0.1f, 1f);

                    int drawWidth = Math.Max(1, (int)Math.Round(previewTexture.Width * scale));
                    int drawHeight = Math.Max(1, (int)Math.Round(previewTexture.Height * scale));
                    Rectangle drawRect = new Rectangle(
                        previewBounds.X + (previewBounds.Width - drawWidth) / 2,
                        previewBounds.Y + (previewBounds.Height - drawHeight) / 2,
                        drawWidth,
                        drawHeight);

                    b.Draw(previewTexture, drawRect, Color.White);
                }
                else
                {
                    DrawPhoneText(
                        b,
                        Game1.smallFont,
                        "Unable to load this image.",
                        new Vector2(previewBounds.X + ScaleValue(20), previewBounds.Y + ScaleValue(20)),
                        Color.Black);
                }

                if (this.chatPhotoCandidates.Count > 1)
                {
                    this.chatPhotoPickerPrevBounds = new Rectangle(
                        previewBounds.X + ScaleValue(8),
                        previewBounds.Y + previewBounds.Height / 2 - ScaleValue(20),
                        ScaleValue(40),
                        ScaleValue(40));
                    this.chatPhotoPickerNextBounds = new Rectangle(
                        previewBounds.Right - ScaleValue(48),
                        previewBounds.Y + previewBounds.Height / 2 - ScaleValue(20),
                        ScaleValue(40),
                        ScaleValue(40));
                    DrawSocialImageNavButton(b, this.chatPhotoPickerPrevBounds, isNext: false);
                    DrawSocialImageNavButton(b, this.chatPhotoPickerNextBounds, isNext: true);
                }

                bool selected = IsChatPhotoSelected(currentPath);
                this.chatPhotoPickerToggleBounds = new Rectangle(
                    panelBounds.X + ScaleValue(168),
                    previewBounds.Bottom + ScaleValue(18),
                    ScaleValue(132),
                    ScaleValue(46));
                UI.CardDrawing.DrawCard(
                    b,
                    this.chatPhotoPickerToggleBounds.X,
                    this.chatPhotoPickerToggleBounds.Y,
                    this.chatPhotoPickerToggleBounds.Width,
                    this.chatPhotoPickerToggleBounds.Height,
                    selected ? new Color(200, 240, 200, 230) : new Color(255, 255, 255, 220),
                    1f,
                    false);

                string toggleLabel = selected ? "Selected" : "Select";
                Vector2 toggleSize = Game1.smallFont.MeasureString(toggleLabel) * this.phoneUiScale;
                DrawPhoneText(
                    b,
                    Game1.smallFont,
                    toggleLabel,
                    new Vector2(
                        this.chatPhotoPickerToggleBounds.X + (this.chatPhotoPickerToggleBounds.Width - toggleSize.X) / 2f,
                        this.chatPhotoPickerToggleBounds.Y + ScaleValue(10)),
                    Color.Black);
            }

            UI.CardDrawing.DrawCard(
                b,
                this.chatPhotoPickerCancelBounds.X,
                this.chatPhotoPickerCancelBounds.Y,
                this.chatPhotoPickerCancelBounds.Width,
                this.chatPhotoPickerCancelBounds.Height,
                new Color(255, 255, 255, 220),
                1f,
                false);

            string cancelText = "Cancel";
            Vector2 cancelSize = Game1.smallFont.MeasureString(cancelText) * this.phoneUiScale;
            DrawPhoneText(
                b,
                Game1.smallFont,
                cancelText,
                new Vector2(
                    this.chatPhotoPickerCancelBounds.X + (this.chatPhotoPickerCancelBounds.Width - cancelSize.X) / 2f,
                    this.chatPhotoPickerCancelBounds.Y + (this.chatPhotoPickerCancelBounds.Height - cancelSize.Y) / 2f + ScaleValue(2)),
                Color.Black);

            var sButton = new ClickableTextureComponent(
                this.chatPhotoPickerSendBounds,
                Game1.mouseCursors,
                new Rectangle(128, 256, 64, 64),
                this.phoneUiScale);
            sButton.draw(b);
        }

        private void DrawSocialImageNavButton(SpriteBatch b, Rectangle bounds, bool isNext)
        {
            // IClickableMenu.drawTextureBox(
            //     b,
            //     Game1.menuTexture,
            //     new Rectangle(0, 256, 60, 60),
            //     bounds.X,
            //     bounds.Y,
            //     bounds.Width,
            //     bounds.Height,
            //     new Color(0, 0, 0, 140),
            //     1f,
            //     false);

            Rectangle source = isNext
                ? Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 33)
                : Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44);

            b.Draw(
                Game1.mouseCursors,
                new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                source,
                Color.White);
        }

        private bool TryGetChatImageTexture(string imagePath, out Texture2D texture)
        {
            texture = null!;
            string resolvedPath = (imagePath ?? "").Trim();
            if (string.IsNullOrWhiteSpace(resolvedPath))
                return false;

            if (!Path.IsPathRooted(resolvedPath))
            {
                string activeSave = MessageManager.GetActiveSaveFolderName();
                string appSharedPath = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared", Path.GetFileName(resolvedPath));
                if (File.Exists(appSharedPath))
                {
                    resolvedPath = appSharedPath;
                }
                else
                {
                    string smartphoneDir = Path.Combine(Directory.GetParent(ModEntry.Instance.Helper.DirectoryPath).FullName, "Smartphone");
                    string playerPath = Path.Combine(smartphoneDir, "userdata", activeSave, "photo_player", resolvedPath);
                    string npcPath = Path.Combine(smartphoneDir, "userdata", activeSave, "shared_photo", resolvedPath);

                    if (File.Exists(playerPath))
                        resolvedPath = playerPath;
                    else if (File.Exists(npcPath))
                        resolvedPath = npcPath;
                }
            }

            if (!File.Exists(resolvedPath))
            {
                string fileName = Path.GetFileName(resolvedPath);
                string smartphoneDir = Path.Combine(Directory.GetParent(ModEntry.Instance.Helper.DirectoryPath).FullName, "Smartphone");
                string activeSave = MessageManager.GetActiveSaveFolderName();
                string playerPath = Path.Combine(smartphoneDir, "userdata", activeSave, "photo_player", fileName);
                string npcPath = Path.Combine(smartphoneDir, "userdata", activeSave, "shared_photo", fileName);

                if (File.Exists(playerPath))
                    resolvedPath = playerPath;
                else if (File.Exists(npcPath))
                    resolvedPath = npcPath;
            }

            if (chatImageCache.TryGetValue(resolvedPath, out Texture2D? cachedTexture) && cachedTexture != null)
            {
                texture = cachedTexture;
                return true;
            }

            if (chatFailedImagePaths.Contains(resolvedPath))
                return false;

            if (!File.Exists(resolvedPath))
            {
                chatFailedImagePaths.Add(resolvedPath);
                return false;
            }

            try
            {
                using var stream = new FileStream(resolvedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                Texture2D loadedTexture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                chatImageCache[resolvedPath] = loadedTexture;
                texture = loadedTexture;
                return true;
            }
            catch (Exception)
            {
                chatFailedImagePaths.Add(resolvedPath);
                return false;
            }
        }

        private string BuildChatPhotoGroupId(bool isPlayer, List<string> paths, int index)
        {
            return $"{(isPlayer ? "player" : "npc")}_{string.Join("_", paths.Select(Path.GetFileNameWithoutExtension))}_{index}";
        }

        private int GetChatPhotoGroupIndex(string groupId, int photoCount)
        {
            if (photoCount <= 0)
                return 0;

            if (!this.chatPhotoGroupIndices.TryGetValue(groupId, out int currentIndex))
                currentIndex = 0;

            currentIndex = Math.Clamp(currentIndex, 0, photoCount - 1);
            this.chatPhotoGroupIndices[groupId] = currentIndex;
            return currentIndex;
        }

        private string GetActiveChatPhotoPath(ChatBubble bubble)
        {
            if (bubble.PhotoPaths == null || bubble.PhotoPaths.Count == 0)
                return "";

            int index = GetChatPhotoGroupIndex(bubble.PhotoGroupId, bubble.PhotoPaths.Count);
            return bubble.PhotoPaths[index];
        }

        private Point GetChatPhotoGroupDrawSize(ChatBubble bubble)
        {
            if (bubble.PhotoPaths == null || bubble.PhotoPaths.Count == 0)
                return new Point(220, 120);

            int maxWidth = 0;
            int maxHeight = 0;

            foreach (string photoPath in bubble.PhotoPaths)
            {
                Point currentSize = GetChatPhotoDrawSize(photoPath);
                maxWidth = Math.Max(maxWidth, currentSize.X);
                maxHeight = Math.Max(maxHeight, currentSize.Y);
            }

            if (maxWidth <= 0 || maxHeight <= 0)
                return new Point(220, 120);

            return new Point(maxWidth, maxHeight);
        }

        private Point GetChatPhotoDrawSize(string photoPath)
        {
            if (!TryGetChatImageTexture(photoPath, out Texture2D texture))
                return new Point(220, 120);

            float scaleX = ChatImageMaxWidth / (float)Math.Max(1, texture.Width);
            float scaleY = ChatImageMaxHeight / (float)Math.Max(1, texture.Height);
            float scale = Math.Min(ChatImageScale, Math.Min(scaleX, scaleY));
            scale = Math.Clamp(scale, 0.1f, 1f);

            return new Point(
                Math.Max(64, (int)Math.Round(texture.Width * scale)),
                Math.Max(64, (int)Math.Round(texture.Height * scale)));
        }

        private Rectangle GetScaledDrawBoundsInArea(Texture2D texture, Rectangle targetArea)
        {
            if (texture == null || targetArea.Width <= 0 || targetArea.Height <= 0)
                return targetArea;

            float widthScale = targetArea.Width / (float)Math.Max(1, texture.Width);
            float heightScale = targetArea.Height / (float)Math.Max(1, texture.Height);
            float scale = Math.Min(widthScale, heightScale);
            scale = Math.Clamp(scale, 0.01f, 100f);

            int drawWidth = Math.Max(1, (int)Math.Round(texture.Width * scale));
            int drawHeight = Math.Max(1, (int)Math.Round(texture.Height * scale));

            return new Rectangle(
                targetArea.X + (targetArea.Width - drawWidth) / 2,
                targetArea.Y + (targetArea.Height - drawHeight) / 2,
                drawWidth,
                drawHeight);
        }

        private void DrawSocialTagTooltip(SpriteBatch b, string tagText, int mouseX, int mouseY)
        {
            if (string.IsNullOrWhiteSpace(tagText))
                return;

            List<string> lines = new List<string>();
            foreach (string part in tagText.Split('\n'))
            {
                lines.AddRange(SplitTextIntoLines(part, Game1.smallFont, GetPhoneScaledWrapWidth(360)));
            }
            if (lines.Count == 0)
                lines.Add(tagText);

            int lineHeight = GetPhoneScaledLineHeight(Game1.smallFont);
            int paddingX = 12;
            int paddingY = 10;

            int maxTextWidth = 0;
            foreach (string line in lines)
                maxTextWidth = Math.Max(maxTextWidth, (int)Math.Ceiling(MeasurePhoneText(Game1.smallFont, line).X));

            int boxWidth = Math.Max(150, maxTextWidth + paddingX * 2);
            int boxHeight = paddingY * 2 + (lines.Count * lineHeight);

            int x = mouseX + 24;
            int y = mouseY + 24;
            int maxX = Math.Max(12, Game1.viewport.Width - boxWidth - 12);
            int maxY = Math.Max(12, Game1.viewport.Height - boxHeight - 12);
            x = Math.Clamp(x, 12, maxX);
            y = Math.Clamp(y, 12, maxY);

            UI.CardDrawing.DrawCard(
                b,
                x,
                y,
                boxWidth,
                boxHeight,
                new Color(255, 255, 255, 235),
                1f,
                false);

            for (int i = 0; i < lines.Count; i++)
                DrawPhoneText(b, Game1.smallFont, lines[i], new Vector2(x + paddingX, y + paddingY + (i * lineHeight)), Color.Black);
        }

        private void DrawChatPhotoHoverTooltips(SpriteBatch b)
        {
            if (ModEntry.Config == null || !ModEntry.Config.ShowMessageImageTags)
                return;

            if (this.chatPhotoPickerOpen || this.chatPhotoHoverEntries.Count == 0)
                return;

            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            Rectangle scrollArea = GetMessageScrollArea();

            if (!scrollArea.Contains(mouseX, mouseY))
                return;

            foreach (var hoverEntry in this.chatPhotoHoverEntries)
            {
                if (hoverEntry.Bounds.Contains(mouseX, mouseY))
                {
                    DrawSocialTagTooltip(b, hoverEntry.TagText, mouseX, mouseY);
                    return;
                }
            }
        }
    }
}
