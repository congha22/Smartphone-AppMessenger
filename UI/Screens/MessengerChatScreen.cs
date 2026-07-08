using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Smartphone;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;
using TextCopy;

namespace SmartphoneAppMessenger
{
    public partial class MessengerChatScreen : IClickableMenu
    {
        private readonly ISmartPhoneApi smartphoneApi;
        public readonly string npcName;
        private readonly Action onBack;

        // Layout bounds
        private int phoneFrameWidth;
        private int phoneFrameHeight;
        private int phoneContentOffsetX;
        private int phoneContentOffsetY;
        private float phoneUiScale;

        private Texture2D? phoneFrameTexture;
        private Texture2D? phoneBackgroundTexture;
        private readonly Texture2D? appPhotoTexture;

        private int contentWidth;
        private int contentHeight;

        // Drag & Swipe State
        private bool isDragging;
        private int dragOffsetX;
        private int dragOffsetY;
        private bool isSwiping;
        private int lastMouseY;

        // UI Components
        private ClickableTextureComponent? sendButton;
        private ClickableComponent? hiButton;
        private ClickableTextureComponent removeButton;
        private Rectangle chatInputBounds;
        private Rectangle chatAttachmentButtonBounds;
        private bool isAttachmentMenuOpen = false;
        private bool isEventMenuOpen = false;
        private List<RegisteredChatQuickActionButton> currentQuickActions = new();
        private List<Rectangle> quickActionBounds = new();
        private List<Rectangle> eventButtonBounds = new();
        private List<RegisteredUnlimitedEvent> currentEvents = new();

        private enum ChatQuickActionType
        {
            Photo,
            Uee,
            AiCredit,
            Custom
        }

        private struct QuickActionItem
        {
            public ChatQuickActionType Type { get; set; }
            public Rectangle Bounds { get; set; }
            public RegisteredChatQuickActionButton? CustomAction { get; set; }
        }

        private List<QuickActionItem> activeQuickActions = new();
        private Rectangle chatAiCreditInfoBounds = Rectangle.Empty;

        // Scroll State
        private float chatScrollOffset = 0f;
        private float maxScrollOffset = 0f;
        private bool shouldAutoScrollToBottom = true;
        private List<ChatBubble> chatBubbles = new();
        private int lastMessageCount = -1;

        // Android Keyboard
        private Task<string>? pendingKeyboardTask;

        // Custom Text Editor State
        private EditableTextBox chatTextBox = new();
        private double textCursorBlinkElapsedSeconds = 0d;
        private MessengerTextInputSubscriber? textInputSubscriber;

        private static ISmartPhoneApi? iSmartphoneApi => ModEntry.iSmartphoneApi;

        private class ChatBubble
        {
            public string Prefix = "";
            public string Text = "";
            public List<string> Lines = new();
            public int Width;
            public int Height;
            public bool IsPhoto;
            public List<string> PhotoPaths = new();
            public string PhotoGroupId = "";
        }

        public MessengerChatScreen(ISmartPhoneApi api, string npcName, Action onBack)
            : base()
        {
            this.smartphoneApi = api;
            this.npcName = npcName;
            this.onBack = onBack;

            // Get phone position
            var (px, py) = api.GetPhonePosition();
            this.xPositionOnScreen = px;
            this.yPositionOnScreen = py;

            this.phoneFrameWidth = api.GetPhoneFrameWidth();
            this.phoneFrameHeight = api.GetPhoneFrameHeight();
            var (offX, offY) = api.GetPhoneContentOffset();
            this.phoneContentOffsetX = offX;
            this.phoneContentOffsetY = offY;
            this.phoneUiScale = api.GetPhoneUiScale();
            this.phoneFrameTexture = api.GetPhoneFrameTexture();
            this.phoneBackgroundTexture = api.GetPhoneBackgroundTexture();
            this.appPhotoTexture = api.GetAppTexture(AppIconType.Photo);

            this.width = this.phoneFrameWidth;
            this.height = this.phoneFrameHeight;

            if (this.phoneBackgroundTexture != null && !this.phoneBackgroundTexture.IsDisposed)
            {
                this.contentWidth = (int)Math.Round(this.phoneBackgroundTexture.Width * this.phoneUiScale);
                this.contentHeight = (int)Math.Round(this.phoneBackgroundTexture.Height * this.phoneUiScale);
            }
            else
            {
                this.contentWidth = Math.Max(1, this.phoneFrameWidth - (this.phoneContentOffsetX * 2));
                this.contentHeight = Math.Max(1, this.phoneFrameHeight - this.phoneContentOffsetY - ScaleValue(80));
            }

            this.removeButton = new ClickableTextureComponent(
                new Rectangle(0, 0, ScaleValue(32), ScaleValue(52)),
                Game1.mouseCursors,
                new Rectangle(564, 102, 16, 26),
                1.6f * this.phoneUiScale);

            SetupChatInputs();
            var messages = MessageManager.GetMessagesForNpc(this.npcName);
            this.lastMessageCount = messages.Count;
            RebuildChatBubbles();
            this.textInputSubscriber = new MessengerTextInputSubscriber(this);
            Game1.keyboardDispatcher.Subscriber = this.textInputSubscriber;
        }

        private void RebuildChatBubbles()
        {
            this.chatBubbles.Clear();
            var messages = MessageManager.GetMessagesForNpc(this.npcName);
            SpriteFont font = Game1.smallFont;

            // 75% of content width minus margins

            int maxBubbleWidth = (int)((this.contentWidth - ScaleValue(20)) * 0.75f);
            int padding = ScaleValue(12);
            float textScale = this.phoneUiScale;

            for (int i = 0; i < messages.Count; i++)
            {
                string text = messages[i];


                if (TryParseChatPhotoMessage(text, out bool isPlayerPhoto, out List<string> photoPaths))
                {
                    string tags = "";
                    if (i + 1 < messages.Count && TryParseChatPhotoTagMessage(messages[i + 1], isPlayerPhoto, out string tagText))
                    {
                        tags = tagText;
                        i++; // consume tag message
                    }


                    ChatBubble bubble = new ChatBubble
                    {
                        Prefix = isPlayerPhoto ? "PLAYER" : "NPC",
                        IsPhoto = true,
                        PhotoPaths = photoPaths,
                        PhotoGroupId = BuildChatPhotoGroupId(isPlayerPhoto, photoPaths, i),
                        Text = tags
                    };


                    Point photoDrawSize = GetChatPhotoGroupDrawSize(bubble);
                    bubble.Width = photoDrawSize.X + padding * 2;
                    bubble.Height = photoDrawSize.Y + padding * 2;


                    this.chatBubbles.Add(bubble);
                    continue;
                }


                if (TryParseChatPhotoTagMessage(text, null, out _))
                {
                    continue;
                }

                ChatBubble standardBubble = new ChatBubble();
                if (text.StartsWith("SYSTEM: "))
                {
                    standardBubble.Prefix = "SYSTEM";
                    text = text.Substring("SYSTEM: ".Length).Trim();
                    standardBubble.Lines = new List<string> { text };
                    standardBubble.Width = (int)(font.MeasureString(text).X * textScale);
                    standardBubble.Height = (int)(font.MeasureString(text).Y * textScale);
                }
                else if (text.StartsWith("PLAYER: "))
                {
                    standardBubble.Prefix = "PLAYER";
                    text = text.Substring("PLAYER: ".Length).Trim();
                    standardBubble.Lines = SplitTextIntoLines(text, font, (int)(maxBubbleWidth / textScale));
                    standardBubble.Width = (int)(standardBubble.Lines.Max(l => font.MeasureString(l).X) * textScale) + (padding * 2);
                    standardBubble.Height = (int)(standardBubble.Lines.Count * font.MeasureString("A").Y * textScale) + (padding * 2);
                }
                else
                {
                    standardBubble.Prefix = "NPC";
                    if (text.StartsWith(this.npcName + ": ", StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Substring(this.npcName.Length + 2).Trim();
                    }
                    else if (text.StartsWith(this.npcName + ":", StringComparison.OrdinalIgnoreCase))
                    {
                        text = text.Substring(this.npcName.Length + 1).Trim();
                    }
                    standardBubble.Lines = SplitTextIntoLines(text, font, (int)(maxBubbleWidth / textScale));
                    standardBubble.Width = (int)(standardBubble.Lines.Max(l => font.MeasureString(l).X) * textScale) + (padding * 2);
                    standardBubble.Height = (int)(standardBubble.Lines.Count * font.MeasureString("A").Y * textScale) + (padding * 2);
                }


                standardBubble.Text = text;
                this.chatBubbles.Add(standardBubble);
            }
            this.shouldAutoScrollToBottom = true;
        }

        private static bool IsCjk(char c)
        {
            return (c >= 0x4e00 && c <= 0x9fff) || // CJK Unified Ideographs
                   (c >= 0x3040 && c <= 0x309f) || // Hiragana
                   (c >= 0x30a0 && c <= 0x30ff) || // Katakana
                   (c >= 0xac00 && c <= 0xd7af) || // Hangul Syllables
                   (c >= 0xff00 && c <= 0xffef) || // Halfwidth and Fullwidth Forms
                   (c >= 0x3000 && c <= 0x303f);   // CJK Symbols and Punctuation
        }

        private List<string> SplitTextIntoLines(string text, SpriteFont font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text)) return new List<string> { "" };

            string[] paragraphs = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            List<string> allLines = new List<string>();

            foreach (var paragraph in paragraphs)
            {
                List<string> tokens = new List<string>();
                string currentWord = "";

                for (int i = 0; i < paragraph.Length; i++)
                {
                    char c = paragraph[i];
                    if (c == ' ')
                    {
                        if (currentWord != "")
                        {
                            tokens.Add(currentWord);
                            currentWord = "";
                        }
                        tokens.Add(" ");
                    }
                    else if (IsCjk(c))
                    {
                        if (currentWord != "")
                        {
                            tokens.Add(currentWord);
                            currentWord = "";
                        }
                        tokens.Add(c.ToString());
                    }
                    else
                    {
                        currentWord += c;
                    }
                }
                if (currentWord != "")
                {
                    tokens.Add(currentWord);
                }

                List<string> lines = new List<string>();
                string currentLine = "";

                foreach (var token in tokens)
                {
                    if (token == " ")
                    {
                        if (currentLine != "" && !currentLine.EndsWith(" "))
                        {
                            if (font.MeasureString(currentLine + " ").X <= maxWidth)
                            {
                                currentLine += " ";
                            }
                        }
                        continue;
                    }

                    string testLine = currentLine;
                    testLine += token;

                    if (font.MeasureString(testLine).X <= maxWidth)
                    {
                        currentLine = testLine;
                    }
                    else
                    {
                        if (currentLine != "")
                        {
                            lines.Add(currentLine.TrimEnd());
                            currentLine = "";
                        }

                        if (font.MeasureString(token).X <= maxWidth)
                        {
                            currentLine = token;
                        }
                        else
                        {
                            for (int j = 0; j < token.Length; j++)
                            {
                                char tc = token[j];
                                string nextTest = currentLine + tc;
                                if (font.MeasureString(nextTest).X <= maxWidth)
                                {
                                    currentLine = nextTest;
                                }
                                else
                                {
                                    if (currentLine != "")
                                    {
                                        lines.Add(currentLine.TrimEnd());
                                    }
                                    currentLine = tc.ToString();
                                }
                            }
                        }
                    }
                }

                if (currentLine != "")
                {
                    lines.Add(currentLine.TrimEnd());
                }

                if (lines.Count == 0)
                {
                    allLines.Add("");
                }
                else
                {
                    allLines.AddRange(lines);
                }
            }

            return allLines;
        }

        private void SetupChatInputs()
        {
            Rectangle contentRect = GetContentBounds();
            int inputY = contentRect.Bottom - ScaleValue(60);

            bool isPlayer = Game1.getOnlineFarmers().Any(f => string.Equals(f.Name, this.npcName, StringComparison.OrdinalIgnoreCase))
                            || MessageManager.PlayerConversations.ContainsKey(this.npcName);

            bool chattedToday = isPlayer || MessageManager.NpcMessagesToday.ContainsKey(this.npcName) || ModEntry.Config.DisableDailyMessage;
            if (!chattedToday)
            {
                NPC? npc = Game1.getCharacterFromName(this.npcName);
                string selectedNpcDisplayName = npc?.displayName ?? this.npcName;
                string firstMessage = Game1.timeOfDay < 1200
                    ? ModEntry.GetTranslation("chat.morning", new { npc = selectedNpcDisplayName })
                    : Game1.timeOfDay < 1800
                        ? ModEntry.GetTranslation("chat.afternoon", new { npc = selectedNpcDisplayName })
                        : ModEntry.GetTranslation("chat.evening", new { npc = selectedNpcDisplayName });


                this.sendButton = null;
                this.chatInputBounds = Rectangle.Empty;
                this.chatAttachmentButtonBounds = Rectangle.Empty;

                int fontHeight = (int)Game1.smallFont.MeasureString("A").Y;
                int fontWidth = (int)Game1.smallFont.MeasureString(firstMessage).X;
                int btnWidth = (int)(fontWidth * this.phoneUiScale) + ScaleValue(50);
                int btnHeight = (int)(fontHeight * this.phoneUiScale) + ScaleValue(30);
                this.hiButton = new ClickableComponent(
                    new Rectangle(contentRect.Center.X - btnWidth / 2, inputY - 10, btnWidth, btnHeight), firstMessage);
            }
            else
            {
                this.hiButton = null;

                int fontHeight = (int)Game1.smallFont.MeasureString("A").Y;
                int inputHeight = (int)(fontHeight * this.phoneUiScale) + ScaleValue(30);
                int attachmentBtnWidth = ScaleValue(52);
                int attachmentBtnHeight = inputHeight;


                this.chatAttachmentButtonBounds = new Rectangle(
                    this.xPositionOnScreen + this.phoneContentOffsetX + ScaleValue(10),
                    this.yPositionOnScreen + this.phoneContentOffsetY + this.contentHeight - inputHeight - 10,
                    attachmentBtnWidth,
                    attachmentBtnHeight
                );

                int inputX = this.chatAttachmentButtonBounds.Right + ScaleValue(8);
                int okBtnSize = ScaleValue(64);
                int tbWidth = contentRect.Right - ScaleValue(10) - inputX - ScaleValue(5) - okBtnSize;


                this.chatInputBounds = new Rectangle(
                    inputX,
                    this.yPositionOnScreen + this.phoneContentOffsetY + this.contentHeight - inputHeight - 10,
                    tbWidth,
                    inputHeight
                );

                this.sendButton = new ClickableTextureComponent(
                    new Rectangle(this.chatInputBounds.Right + ScaleValue(5), this.chatInputBounds.Y + (inputHeight - okBtnSize) / 2, okBtnSize, okBtnSize),
                    Game1.mouseCursors,
                    new Rectangle(128, 256, 64, 64),
                    this.phoneUiScale);
            }
        }

        private void SendMessage()
        {
            List<string> photosToSend = new List<string>(this.chatSelectedPhotos);
            string text = (this.chatTextBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text) && photosToSend.Count == 0)
                return;

            this.chatTextBox.Clear();
            this.chatSelectedPhotos.Clear();
            CloseChatPhotoPicker(clearSelection: true);

            bool isPlayer = Game1.getOnlineFarmers().Any(f => string.Equals(f.Name, this.npcName, StringComparison.OrdinalIgnoreCase))
                            || MessageManager.PlayerConversations.ContainsKey(this.npcName);

            if (isPlayer)
            {
                var onlineTarget = Game1.getOnlineFarmers().FirstOrDefault(f => string.Equals(f.Name, this.npcName, StringComparison.OrdinalIgnoreCase));
                if (onlineTarget == null)
                {
                    MessageManager.AddPlayerMessage(this.npcName, ModEntry.GetTranslation("chat.offline", new { npc = this.npcName }), isFromPlayer: false);
                    RebuildChatBubbles();
                    this.lastMessageCount = MessageManager.GetMessagesForNpc(this.npcName).Count;
                    return;
                }

                if (!string.IsNullOrWhiteSpace(text))
                {
                    MessageManager.AddPlayerMessage(this.npcName, text, isFromPlayer: true);
                    TransferManager.SendTextMessage(this.npcName, text);
                }

                if (photosToSend.Count > 0)
                {
                    string activeSave = MessageManager.GetActiveSaveFolderName();
                    string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                    Directory.CreateDirectory(photoSharedDir);

                    List<string> relativePaths = new List<string>();

                    foreach (string photoPath in photosToSend)
                    {
                        if (File.Exists(photoPath))
                        {
                            string extension = Path.GetExtension(photoPath);
                            string destFileName = Guid.NewGuid().ToString("N") + extension;
                            string destPath = Path.Combine(photoSharedDir, destFileName);
                            File.Copy(photoPath, destPath, overwrite: true);

                            string relativePath = "photo_shared/" + destFileName;
                            relativePaths.Add(relativePath);

                            TransferManager.QueueSend("Photo", destFileName, destPath, this.npcName);
                        }
                    }

                    if (relativePaths.Count > 0)
                    {
                        string localPhotoMsg = "PlayerPhoto: " + string.Join("||", relativePaths);
                        MessageManager.AddPlayerMessage(this.npcName, localPhotoMsg, isFromPlayer: true);

                        string remotePhotoMsg = "NpcPhoto: " + string.Join("||", relativePaths);
                        TransferManager.SendTextMessage(this.npcName, remotePhotoMsg);
                    }
                }

                RebuildChatBubbles();
                this.lastMessageCount = MessageManager.GetMessagesForNpc(this.npcName).Count;
                return;
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                MessageManager.AddMessage(this.npcName, text, type: "sent");
            }

            string tagsCombined = "";
            if (photosToSend.Count > 0)
            {
                string photoMsg = "PlayerPhoto: " + string.Join("||", photosToSend);
                MessageManager.AddMessage(this.npcName, photoMsg, type: "raw");

                List<string> tagList = new List<string>();
                foreach (string photoPath in photosToSend)
                {
                    string tag = GetPhotoTag(photoPath);
                    if (!string.IsNullOrWhiteSpace(tag))
                        tagList.Add(tag);
                }
                tagsCombined = string.Join("; ", tagList);
                string tagMsg = "PlayerPhotoTag: " + tagsCombined;
                MessageManager.AddMessage(this.npcName, tagMsg, type: "raw");
            }

            RebuildChatBubbles();
            this.lastMessageCount = MessageManager.GetMessagesForNpc(this.npcName).Count;

            string promptArg = text;
            if (!string.IsNullOrWhiteSpace(tagsCombined))
            {
                if (string.IsNullOrWhiteSpace(promptArg))
                    promptArg = $"[Attached photo tags: {tagsCombined}]";
                else
                    promptArg = $"{promptArg} [Attached photo tags: {tagsCombined}]";
            }


            ModEntry.QueueUserMessage(this.npcName, promptArg);
        }

        private int ScaleValue(int baseValue)
        {
            return (int)Math.Round(baseValue * this.phoneUiScale);
        }

        private Rectangle GetFrameBounds()
        {
            return new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.phoneFrameWidth, this.phoneFrameHeight);
        }

        private Rectangle GetContentBounds()
        {
            return new Rectangle(
                this.xPositionOnScreen + this.phoneContentOffsetX,
                this.yPositionOnScreen + this.phoneContentOffsetY,
                this.contentWidth,
                this.contentHeight);
        }


        private Rectangle GetMessageScrollArea()
        {
            Rectangle contentRect = GetContentBounds();
            return new Rectangle(contentRect.X, this.yPositionOnScreen + ScaleValue(175), contentRect.Width, ScaleValue(715));
        }

        public override void draw(SpriteBatch b)
        {
            // Dim background
            b.Draw(Game1.staminaRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.6f);

            Rectangle contentRect = GetContentBounds();
            Rectangle frameRect = GetFrameBounds();

            // Draw phone background
            if (this.phoneBackgroundTexture != null && !this.phoneBackgroundTexture.IsDisposed)
            {
                b.Draw(this.phoneBackgroundTexture, contentRect, Color.White);
            }
            else
            {
                b.Draw(Game1.staminaRect, contentRect, new Color(30, 30, 30));
            }

            DrawContent(b, contentRect);

            // Draw phone border on top
            if (this.phoneFrameTexture != null && !this.phoneFrameTexture.IsDisposed)
            {
                b.Draw(this.phoneFrameTexture, frameRect, Color.White);
            }

            this.smartphoneApi.DrawPhoneSizeButtons(b, this.xPositionOnScreen, this.yPositionOnScreen);

            // --- Draw Header on top of frame ---
            SpriteFont font = Game1.dialogueFont;
            float textScale = 1.0f * this.phoneUiScale;
            Vector2 textPos = new Vector2(
                this.xPositionOnScreen + this.phoneContentOffsetX + ScaleValue(65),
                this.yPositionOnScreen + this.phoneContentOffsetY + ScaleValue(-45));

            NPC? headerNpc = Game1.getCharacterFromName(this.npcName);
            string headerDisplayName = headerNpc?.displayName ?? this.npcName;
            b.DrawString(font, headerDisplayName, textPos, Color.Black, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);

            this.removeButton.bounds.X = this.xPositionOnScreen + this.phoneContentOffsetX + ScaleValue(295);
            this.removeButton.bounds.Y = this.yPositionOnScreen + this.phoneContentOffsetY + ScaleValue(-47);
            this.removeButton.draw(b, Color.White * 0.8f, 1f);

            if (this.chatPhotoPickerOpen)
            {
                DrawChatPhotoPickerMenu(b);
            }
            else
            {
                DrawChatPhotoHoverTooltips(b);
                DrawAiCreditTooltipIfHovered(b);
            }

            drawMouse(b);
        }

        private void DrawContent(SpriteBatch b, Rectangle contentRect)
        {
            this.chatPhotoNavigationEntries.Clear();
            this.chatPhotoHoverEntries.Clear();

            // --- Draw Messages (Scissored) ---
            Rectangle scrollArea = GetMessageScrollArea();
            int messageSpacing = ScaleValue(10);
            int padding = ScaleValue(12);

            int totalHeight = 0;
            for (int i = 0; i < this.chatBubbles.Count; i++)
            {
                var bubble = this.chatBubbles[i];
                int nextSpacing = 0;
                if (i < this.chatBubbles.Count - 1 && this.chatBubbles[i + 1].Prefix != bubble.Prefix)
                {
                    nextSpacing = messageSpacing;
                }
                totalHeight += bubble.Height + nextSpacing;
            }

            this.maxScrollOffset = Math.Max(0, totalHeight - scrollArea.Height + messageSpacing);
            if (this.shouldAutoScrollToBottom)
            {
                this.chatScrollOffset = this.maxScrollOffset;
                this.shouldAutoScrollToBottom = false;
            }
            this.chatScrollOffset = Math.Clamp(this.chatScrollOffset, 0, this.maxScrollOffset);

            int messageY = scrollArea.Y - (int)this.chatScrollOffset;

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
            Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
            Game1.graphics.GraphicsDevice.ScissorRectangle = scrollArea;

            for (int i = 0; i < this.chatBubbles.Count; i++)
            {
                var bubble = this.chatBubbles[i];
                if (messageY + bubble.Height > scrollArea.Y && messageY < scrollArea.Bottom)
                {
                    if (bubble.Prefix == "SYSTEM")
                    {
                        Vector2 pos = new Vector2(scrollArea.Center.X - (bubble.Width / 2f), messageY);
                        b.DrawString(Game1.smallFont, bubble.Text, pos, Color.Black * 0.5f, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
                    }
                    else if (bubble.Prefix == "PLAYER")
                    {
                        int boxX = scrollArea.Right - bubble.Width - ScaleValue(10);
                        UI.CardDrawing.DrawCard(b, boxX - ScaleValue(5), messageY, bubble.Width + ScaleValue(12), bubble.Height, Color.LightGreen, 1f, false);


                        if (bubble.IsPhoto)
                        {
                            string photoPath = GetActiveChatPhotoPath(bubble);
                            if (TryGetChatImageTexture(photoPath, out Texture2D photoTexture))
                            {
                                Rectangle targetArea = new Rectangle(boxX + padding, messageY + padding, bubble.Width - padding * 2, bubble.Height - padding * 2);
                                Rectangle drawBounds = GetScaledDrawBoundsInArea(photoTexture, targetArea);
                                b.Draw(photoTexture, drawBounds, Color.White);


                                if (!string.IsNullOrWhiteSpace(bubble.Text))
                                {
                                    this.chatPhotoHoverEntries.Add(new ChatPhotoHoverEntry
                                    {
                                        Bounds = drawBounds,
                                        TagText = bubble.Text
                                    });
                                }


                                if (bubble.PhotoPaths.Count > 1)
                                {
                                    Rectangle prevArrowBounds = new Rectangle(
                                        boxX + ScaleValue(6),
                                        messageY + bubble.Height / 2 - ScaleValue(19),
                                        ScaleValue(38),
                                        ScaleValue(38));
                                    Rectangle nextArrowBounds = new Rectangle(
                                        boxX + bubble.Width - ScaleValue(38) - ScaleValue(6),
                                        messageY + bubble.Height / 2 - ScaleValue(19),
                                        ScaleValue(38),
                                        ScaleValue(38));


                                    this.chatPhotoNavigationEntries.Add(new ChatPhotoNavigationEntry
                                    {
                                        GroupId = bubble.PhotoGroupId,
                                        PhotoCount = bubble.PhotoPaths.Count,
                                        PreviousBounds = prevArrowBounds,
                                        NextBounds = nextArrowBounds
                                    });


                                    DrawSocialImageNavButton(b, prevArrowBounds, isNext: false);
                                    DrawSocialImageNavButton(b, nextArrowBounds, isNext: true);
                                }
                            }
                        }
                        else
                        {
                            int textY = messageY + padding;
                            foreach (var line in bubble.Lines)
                            {
                                b.DrawString(Game1.smallFont, line, new Vector2(boxX + padding, textY), Game1.textColor, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
                                textY += (int)(Game1.smallFont.MeasureString("A").Y * this.phoneUiScale);
                            }
                        }
                    }
                    else // NPC
                    {
                        int boxX = scrollArea.Left + ScaleValue(10);
                        UI.CardDrawing.DrawCard(b, boxX - ScaleValue(5), messageY, bubble.Width + ScaleValue(12), bubble.Height, Color.Silver, 1f, false);


                        if (bubble.IsPhoto)
                        {
                            string photoPath = GetActiveChatPhotoPath(bubble);
                            if (TryGetChatImageTexture(photoPath, out Texture2D photoTexture))
                            {
                                Rectangle targetArea = new Rectangle(boxX + padding, messageY + padding, bubble.Width - padding * 2, bubble.Height - padding * 2);
                                Rectangle drawBounds = GetScaledDrawBoundsInArea(photoTexture, targetArea);
                                b.Draw(photoTexture, drawBounds, Color.White);


                                if (!string.IsNullOrWhiteSpace(bubble.Text))
                                {
                                    this.chatPhotoHoverEntries.Add(new ChatPhotoHoverEntry
                                    {
                                        Bounds = drawBounds,
                                        TagText = bubble.Text
                                    });
                                }


                                if (bubble.PhotoPaths.Count > 1)
                                {
                                    Rectangle prevArrowBounds = new Rectangle(
                                        boxX + ScaleValue(6),
                                        messageY + bubble.Height / 2 - ScaleValue(19),
                                        ScaleValue(38),
                                        ScaleValue(38));
                                    Rectangle nextArrowBounds = new Rectangle(
                                        boxX + bubble.Width - ScaleValue(38) - ScaleValue(6),
                                        messageY + bubble.Height / 2 - ScaleValue(19),
                                        ScaleValue(38),
                                        ScaleValue(38));


                                    this.chatPhotoNavigationEntries.Add(new ChatPhotoNavigationEntry
                                    {
                                        GroupId = bubble.PhotoGroupId,
                                        PhotoCount = bubble.PhotoPaths.Count,
                                        PreviousBounds = prevArrowBounds,
                                        NextBounds = nextArrowBounds
                                    });


                                    DrawSocialImageNavButton(b, prevArrowBounds, isNext: false);
                                    DrawSocialImageNavButton(b, nextArrowBounds, isNext: true);
                                }
                            }
                        }
                        else
                        {
                            int textY = messageY + padding;
                            foreach (var line in bubble.Lines)
                            {
                                b.DrawString(Game1.smallFont, line, new Vector2(boxX + padding, textY), Game1.textColor, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
                                textY += (int)(Game1.smallFont.MeasureString("A").Y * this.phoneUiScale);
                            }
                        }
                    }
                }


                int nextSpacing = 0;
                if (i < this.chatBubbles.Count - 1 && this.chatBubbles[i + 1].Prefix != bubble.Prefix)
                {
                    nextSpacing = messageSpacing;
                }
                messageY += bubble.Height + nextSpacing;
            }

            b.End();
            Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);


            // --- Draw Inputs ---
            if (this.hiButton != null)
            {
                UI.CardDrawing.DrawCard(b, this.hiButton.bounds.X, this.hiButton.bounds.Y, this.hiButton.bounds.Width, this.hiButton.bounds.Height, Color.LightGray, 1f, false);
                Vector2 hiSize = Game1.smallFont.MeasureString(this.hiButton.name) * this.phoneUiScale;
                b.DrawString(Game1.smallFont, this.hiButton.name, new Vector2((int)(this.hiButton.bounds.Center.X - hiSize.X / 2), (int)(this.hiButton.bounds.Center.Y - hiSize.Y / 2)), Game1.textColor, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
            }
            else if (!this.chatInputBounds.IsEmpty && this.sendButton != null)
            {
                // Draw toggle button (the arrow)
                UI.CardDrawing.DrawCard(b,
                    this.chatAttachmentButtonBounds.X, this.chatAttachmentButtonBounds.Y,
                    this.chatAttachmentButtonBounds.Width, this.chatAttachmentButtonBounds.Height,
                    Color.LightGray, 1f, false);


                Rectangle arrowSource = this.isAttachmentMenuOpen
                    ? new Rectangle(421, 472, 11, 12)  // Up arrow in old mod
                    : new Rectangle(421, 459, 11, 12); // Down arrow in old mod

                int iconWidth = Math.Max(14, this.chatAttachmentButtonBounds.Width - 24);
                int iconHeight = Math.Max(14, this.chatAttachmentButtonBounds.Height - 24);
                Rectangle iconBounds = new Rectangle(
                    this.chatAttachmentButtonBounds.X + (this.chatAttachmentButtonBounds.Width - iconWidth) / 2,
                    this.chatAttachmentButtonBounds.Y + (this.chatAttachmentButtonBounds.Height - iconHeight) / 2,
                    iconWidth,
                    iconHeight);

                b.Draw(Game1.mouseCursors, iconBounds, arrowSource, Color.White);

                // Draw badge on the attachment button if photos are selected and menu is closed
                if (this.chatSelectedPhotos.Count > 0 && !this.isAttachmentMenuOpen)
                {
                    string badgeText = this.chatSelectedPhotos.Count.ToString();
                    Rectangle badgeBounds = new Rectangle(this.chatAttachmentButtonBounds.Right - ScaleValue(4), this.chatAttachmentButtonBounds.Y - ScaleValue(6), ScaleValue(24), ScaleValue(18));
                    b.Draw(Game1.staminaRect, badgeBounds, new Color(215, 48, 48, 235));

                    Vector2 badgeSize = MeasurePhoneText(Game1.smallFont, badgeText);
                    Vector2 badgePos = new Vector2(
                        badgeBounds.X + (badgeBounds.Width - badgeSize.X) / 2f,
                        badgeBounds.Y + (badgeBounds.Height - badgeSize.Y) / 2f - ScaleValue(1));
                    DrawPhoneText(b, Game1.smallFont, badgeText, badgePos, Color.White);
                }


                if (this.isAttachmentMenuOpen)
                {
                    foreach (var action in this.activeQuickActions)
                    {
                        switch (action.Type)
                        {
                            case ChatQuickActionType.Photo:
                                DrawChatQuickIconActionButton(
                                    b,
                                    action.Bounds,
                                    false,
                                    this.appPhotoTexture,
                                    null,
                                    new Rectangle(190, 423, 14, 11),
                                    this.chatSelectedPhotos.Count);
                                break;
                            case ChatQuickActionType.Uee:
                                DrawChatQuickIconActionButton(
                                    b,
                                    action.Bounds,
                                    this.isEventMenuOpen,
                                    null,
                                    null,
                                    new Rectangle(190, 423, 14, 11),
                                    0);
                                break;
                            case ChatQuickActionType.AiCredit:
                                DrawChatAiCreditInfoButton(b, action.Bounds);
                                break;
                            case ChatQuickActionType.Custom:
                                if (action.CustomAction != null)
                                {
                                    DrawChatQuickIconActionButton(
                                        b,
                                        action.Bounds,
                                        false,
                                        action.CustomAction.IconTexture,
                                        action.CustomAction.SourceRect,
                                        null,
                                        0);
                                }
                                break;
                        }
                    }
                }

                var ueeAction = this.activeQuickActions.FirstOrDefault(a => a.Type == ChatQuickActionType.Uee);
                if (this.isEventMenuOpen && ueeAction.Bounds != Rectangle.Empty)
                {
                    int panelPadding = ScaleValue(10);
                    int panelWidth = ScaleValue(370);
                    int rowHeight = ScaleValue(60);
                    int rowSpacing = ScaleValue(6);
                    int rowsHeight = this.currentEvents.Count == 0
                        ? rowHeight
                        : this.currentEvents.Count * rowHeight + Math.Max(0, this.currentEvents.Count - 1) * rowSpacing;
                    int panelHeight = panelPadding * 2 + rowsHeight;


                    int panelX = ueeAction.Bounds.Right + ScaleValue(10);
                    int panelY = ueeAction.Bounds.Bottom - panelHeight;
                    panelX = Math.Clamp(panelX, ScaleValue(10), Game1.uiViewport.Width - panelWidth - ScaleValue(10));
                    panelY = Math.Clamp(panelY, ScaleValue(10), Game1.uiViewport.Height - panelHeight - ScaleValue(10));

                    // Panel background

                    UI.CardDrawing.DrawCard(b,
                        panelX, panelY, panelWidth, panelHeight, new Color(255, 255, 255, 240), 1f, false);


                    if (ModEntry.iUnlimitedEventExpansionApi == null)
                    {
                        DrawPhoneText(b, Game1.smallFont, ModEntry.GetTranslation("chat.event.get-expansion"), new Vector2(panelX + panelPadding, panelY + panelPadding + ScaleValue(3)), Color.Black);
                        DrawPhoneText(b, Game1.smallFont, ModEntry.GetTranslation("chat.event.to-schedule"), new Vector2(panelX + panelPadding, panelY + panelPadding + ScaleValue(30)), Color.Black);
                    }
                    else if (this.currentEvents.Count == 0)
                    {
                        DrawPhoneText(b, Game1.smallFont, ModEntry.GetTranslation("chat.event.no-event"), new Vector2(panelX + panelPadding, panelY + panelPadding + ScaleValue(3)), Color.Black);
                        DrawPhoneText(b, Game1.smallFont, ModEntry.GetTranslation("chat.event.at-friendship"), new Vector2(panelX + panelPadding, panelY + panelPadding + ScaleValue(30)), Color.Black);
                    }
                    else
                    {
                        this.eventButtonBounds.Clear();
                        int startX = panelX + panelPadding;
                        int startY = panelY + panelPadding;
                        for (int i = 0; i < this.currentEvents.Count; i++)
                        {
                            var evt = this.currentEvents[i];
                            Rectangle buttonBounds = new Rectangle(startX, startY, panelWidth - panelPadding * 2, rowHeight);
                            this.eventButtonBounds.Add(buttonBounds);


                            string label = GetTailTextToFit(ModEntry.GetTranslation("chat.event.schedule", new { eventType = evt.EventType }), Game1.smallFont, buttonBounds.Width - ScaleValue(16));
                            DrawChatQuickActionButton(b, buttonBounds, label, false);


                            startY += rowHeight + rowSpacing;
                        }
                    }
                }

                // Input box
                UI.CardDrawing.DrawCard(b, this.chatInputBounds.X, this.chatInputBounds.Y, this.chatInputBounds.Width, this.chatInputBounds.Height, Color.White, 1f, false);
                this.chatTextBox.Draw(b, this.chatInputBounds, this.phoneUiScale, true);
                this.sendButton.draw(b);
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            if (this.chatPhotoPickerOpen)
                return;
            base.receiveScrollWheelAction(direction);
            this.chatScrollOffset -= direction * 0.5f;
            this.chatScrollOffset = Math.Clamp(this.chatScrollOffset, 0, this.maxScrollOffset);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            this.lastMouseY = y;

            if (this.smartphoneApi.HandlePhoneSizeButtonsClick(x, y, this.xPositionOnScreen, this.yPositionOnScreen))
            {
                return;
            }

            if (this.smartphoneApi.HandlePhoneAppBottomNavClick(x, y, this.xPositionOnScreen, this.yPositionOnScreen, onBack: this.onBack))
            {
                return;
            }

            if (this.chatPhotoPickerOpen)
            {
                HandleChatPhotoPickerClick(x, y);
                return;
            }

            if (TryHandleChatPhotoNavigationClick(x, y))
            {
                return;
            }

            if (this.removeButton != null && this.removeButton.containsPoint(x, y))
            {
                MessageManager.NpcMessagesToday.Remove(this.npcName);
                Game1.playSound("trashcan");
                this.onBack?.Invoke();
                return;
            }

            if (this.hiButton != null && this.hiButton.bounds.Contains(x, y))
            {
                NPC? npc = Game1.getCharacterFromName(this.npcName);
                string selectedNpcDisplayName = npc?.displayName ?? this.npcName;
                string firstMessage = Game1.timeOfDay < 1200
                    ? ModEntry.GetTranslation("chat.morning", new { npc = selectedNpcDisplayName })
                    : Game1.timeOfDay < 1800
                        ? ModEntry.GetTranslation("chat.afternoon", new { npc = selectedNpcDisplayName })
                        : ModEntry.GetTranslation("chat.evening", new { npc = selectedNpcDisplayName });

                MessageManager.AddMessage(this.npcName, firstMessage, type: "sent");
                PhoneDialogueRuntime.FirstDailyText(this.npcName, firstMessage);
                SetupChatInputs();
                RebuildChatBubbles();
                Game1.playSound("bigSelect");
                return;
            }

            if (this.chatAttachmentButtonBounds.Contains(x, y))
            {
                this.isAttachmentMenuOpen = !this.isAttachmentMenuOpen;
                if (this.isAttachmentMenuOpen)
                {
                    this.currentQuickActions = ModEntry.GetRegisteredChatQuickActionButtonsSnapshot(this.npcName);
                    this.quickActionBounds.Clear();
                    this.activeQuickActions.Clear();
                    int btnWidth = this.chatAttachmentButtonBounds.Width;
                    int btnHeight = this.chatAttachmentButtonBounds.Height;
                    int currentY = this.chatAttachmentButtonBounds.Y - btnHeight - ScaleValue(8);

                    // 1. Photo
                    Rectangle photoBounds = new Rectangle(this.chatAttachmentButtonBounds.X, currentY, btnWidth, btnHeight);
                    this.activeQuickActions.Add(new QuickActionItem { Type = ChatQuickActionType.Photo, Bounds = photoBounds });
                    this.quickActionBounds.Add(photoBounds);
                    currentY -= btnHeight + ScaleValue(6);

                    // 2. UEE
                    Rectangle ueeBounds = new Rectangle(this.chatAttachmentButtonBounds.X, currentY, btnWidth, btnHeight);
                    this.activeQuickActions.Add(new QuickActionItem { Type = ChatQuickActionType.Uee, Bounds = ueeBounds });
                    this.quickActionBounds.Add(ueeBounds);
                    currentY -= btnHeight + ScaleValue(6);

                    // 3. AI Credit
                    if (ShouldShowAiCreditButton())
                    {
                        Rectangle aiBounds = new Rectangle(this.chatAttachmentButtonBounds.X, currentY, btnWidth, btnHeight);
                        this.activeQuickActions.Add(new QuickActionItem { Type = ChatQuickActionType.AiCredit, Bounds = aiBounds });
                        this.quickActionBounds.Add(aiBounds);
                        this.chatAiCreditInfoBounds = aiBounds;
                        currentY -= btnHeight + ScaleValue(6);
                    }
                    else
                    {
                        this.chatAiCreditInfoBounds = Rectangle.Empty;
                    }

                    // 4. Custom actions
                    foreach (var qa in this.currentQuickActions)
                    {
                        Rectangle customBounds = new Rectangle(this.chatAttachmentButtonBounds.X, currentY, btnWidth, btnHeight);
                        this.activeQuickActions.Add(new QuickActionItem
                        {
                            Type = ChatQuickActionType.Custom,
                            Bounds = customBounds,
                            CustomAction = qa
                        });
                        this.quickActionBounds.Add(customBounds);
                        currentY -= btnHeight + ScaleValue(6);
                    }
                }
                else
                {
                    this.isEventMenuOpen = false;
                }
                Game1.playSound("shwip");
                return;
            }

            if (this.isEventMenuOpen)
            {
                bool clickedEvent = false;
                for (int i = 0; i < this.eventButtonBounds.Count; i++)
                {
                    if (this.eventButtonBounds[i].Contains(x, y))
                    {
                        var evt = this.currentEvents[i];
                        ModEntry.iUnlimitedEventExpansionApi?.OpenScheduleEventTimeMenu(this.npcName, evt.EventType);
                        Game1.playSound("bigSelect");
                        clickedEvent = true;
                        break;
                    }
                }
                this.isEventMenuOpen = false;
                this.isAttachmentMenuOpen = false;
                if (clickedEvent)
                {
                    this.exitThisMenu();
                    return;
                }
            }

            if (this.isAttachmentMenuOpen)
            {
                bool clickedAction = false;
                foreach (var action in this.activeQuickActions)
                {
                    if (action.Bounds.Contains(x, y))
                    {
                        clickedAction = true;
                        if (action.Type == ChatQuickActionType.Photo)
                        {
                            OpenChatPhotoPicker();
                            Game1.playSound("bigSelect");
                        }
                        else if (action.Type == ChatQuickActionType.Uee)
                        {
                            this.isEventMenuOpen = !this.isEventMenuOpen;
                            if (this.isEventMenuOpen)
                            {
                                this.currentEvents = GetSchedulableEventsForSelectedNpc();
                                this.eventButtonBounds.Clear();

                                int panelWidth = ScaleValue(370);
                                int rowHeight = ScaleValue(60);
                                int rowSpacing = ScaleValue(6);
                                int panelPadding = ScaleValue(10);

                                int rowsHeight = this.currentEvents.Count == 0
                                    ? rowHeight
                                    : this.currentEvents.Count * rowHeight + Math.Max(0, this.currentEvents.Count - 1) * rowSpacing;
                                int panelHeight = panelPadding * 2 + rowsHeight;

                                int panelX = action.Bounds.Right + ScaleValue(10);
                                int panelY = action.Bounds.Bottom - panelHeight;
                                panelX = Math.Clamp(panelX, ScaleValue(10), Game1.uiViewport.Width - panelWidth - ScaleValue(10));
                                panelY = Math.Clamp(panelY, ScaleValue(10), Game1.uiViewport.Height - panelHeight - ScaleValue(10));

                                int startX = panelX + panelPadding;
                                int startY = panelY + panelPadding;

                                foreach (var evt in this.currentEvents)
                                {
                                    this.eventButtonBounds.Add(new Rectangle(startX, startY, panelWidth - panelPadding * 2, rowHeight));
                                    startY += rowHeight + rowSpacing;
                                }
                            }
                            Game1.playSound("shwip");
                        }
                        else if (action.Type == ChatQuickActionType.AiCredit)
                        {
                            Game1.playSound("smallSelect");
                        }
                        else if (action.Type == ChatQuickActionType.Custom && action.CustomAction != null)
                        {
                            var qa = action.CustomAction;
                            if (qa.ClosePhoneOnLaunch)
                            {
                                this.smartphoneApi.OpenPhoneHomeScreen();
                            }
                            qa.OnClick?.Invoke(this.npcName);
                            Game1.playSound("bigSelect");
                        }
                        break;
                    }
                }

                if (clickedAction && !this.isEventMenuOpen) this.isAttachmentMenuOpen = false;
                else if (!clickedAction) this.isAttachmentMenuOpen = false;

                if (clickedAction) return;
            }

            if (!this.chatInputBounds.IsEmpty)
            {
                if (this.chatInputBounds.Contains(x, y))
                {
                    if (Constants.TargetPlatform == GamePlatform.Android)
                    {
                        TriggerAndroidKeyboard(this.chatTextBox.Text);
                    }
                    else
                    {
                        this.chatTextBox.SetCursorFromClick(x, this.chatInputBounds, this.phoneUiScale);
                    }
                }

                if (this.sendButton != null && this.sendButton.containsPoint(x, y))
                {
                    SendMessage();
                    Game1.playSound("bigSelect");
                    return;
                }
            }
        }

        private List<RegisteredUnlimitedEvent> GetSchedulableEventsForSelectedNpc()
        {
            if (string.IsNullOrWhiteSpace(this.npcName))
                return new List<RegisteredUnlimitedEvent>();

            int heartLevel = 0;
            if (Game1.player?.friendshipData != null
                && Game1.player.friendshipData.TryGetValue(this.npcName, out Friendship? friendship)
                && friendship != null)
            {
                heartLevel = friendship.Points / 250;
            }

            return ModEntry.GetRegisteredUnlimitedEventsForHeartLevel(heartLevel);
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);

            if (!this.isDragging)
            {
                Rectangle frameBounds = GetFrameBounds();
                Rectangle contentBounds = GetContentBounds();
                Rectangle scrollArea = GetMessageScrollArea();

                if (scrollArea.Contains(x, y) || this.isSwiping)
                {
                    this.isSwiping = true;
                    int delta = this.lastMouseY - y;
                    this.chatScrollOffset += delta;
                    this.chatScrollOffset = Math.Clamp(this.chatScrollOffset, 0, this.maxScrollOffset);
                    this.lastMouseY = y;
                }
                else if (frameBounds.Contains(x, y) && !contentBounds.Contains(x, y))
                {
                    this.isDragging = true;
                    this.dragOffsetX = x - this.xPositionOnScreen;
                    this.dragOffsetY = y - this.yPositionOnScreen;
                }
            }
        }

        public bool IsChatInputActive()
        {
            return !this.chatInputBounds.IsEmpty;
        }

        public override void receiveKeyPress(Keys key)
        {
            bool isTyping = Game1.keyboardDispatcher.Subscriber == this.textInputSubscriber;
            if (!isTyping)
            {
                string keyStr = key.ToString();
                if (keyStr == this.smartphoneApi.GetDecreaseSizeKey())
                {
                    this.smartphoneApi.AdjustPhoneSize(-0.1f);
                    return;
                }
                if (keyStr == this.smartphoneApi.GetIncreaseSizeKey())
                {
                    this.smartphoneApi.AdjustPhoneSize(0.1f);
                    return;
                }
            }

            if (key == Keys.Escape)
            {
                if (this.chatPhotoPickerOpen)
                {
                    CloseChatPhotoPicker(clearSelection: true);
                    Game1.playSound("bigDeSelect");
                }
                else
                {
                    this.onBack?.Invoke();
                }
                return;
            }

            if (this.chatPhotoPickerOpen)
            {
                return;
            }

            if (KeyboardManager.IsTextInputActive(this))
            {
                if (key == Keys.Enter || key == Keys.Execute)
                {
                    SendMessage();
                    return;
                }

                TryApplyEditableTextKey(key);
                return;
            }

            base.receiveKeyPress(key);
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);
            this.isDragging = false;
            this.isSwiping = false;
        }

        public override void update(GameTime time)
        {
            if (Game1.activeClickableMenu != this)
            {
                if (Game1.keyboardDispatcher.Subscriber == this.textInputSubscriber)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                }
            }

            // Sync from API if modified externally
            float activeScale = this.smartphoneApi.GetPhoneUiScale();
            if (Math.Abs(this.phoneUiScale - activeScale) > 0.001f)
            {
                this.phoneUiScale = activeScale;
                this.phoneFrameWidth = this.smartphoneApi.GetPhoneFrameWidth();
                this.phoneFrameHeight = this.smartphoneApi.GetPhoneFrameHeight();
                var (offX, offY) = this.smartphoneApi.GetPhoneContentOffset();
                this.phoneContentOffsetX = offX;
                this.phoneContentOffsetY = offY;
                this.phoneFrameTexture = this.smartphoneApi.GetPhoneFrameTexture();
                this.phoneBackgroundTexture = this.smartphoneApi.GetPhoneBackgroundTexture();

                this.width = this.phoneFrameWidth;
                this.height = this.phoneFrameHeight;

                if (this.phoneBackgroundTexture != null && !this.phoneBackgroundTexture.IsDisposed)
                {
                    this.contentWidth = (int)Math.Round(this.phoneBackgroundTexture.Width * this.phoneUiScale);
                    this.contentHeight = (int)Math.Round(this.phoneBackgroundTexture.Height * this.phoneUiScale);
                }
                else
                {
                    this.contentWidth = Math.Max(1, this.phoneFrameWidth - (this.phoneContentOffsetX * 2));
                    this.contentHeight = Math.Max(1, this.phoneFrameHeight - this.phoneContentOffsetY - ScaleValue(80));
                }

                this.removeButton = new ClickableTextureComponent(
                    new Rectangle(0, 0, ScaleValue(32), ScaleValue(52)),
                    Game1.mouseCursors,
                    new Rectangle(564, 102, 16, 26),
                    1.6f * this.phoneUiScale);

                SetupChatInputs();
                RebuildChatBubbles();
            }

            base.update(time);

            this.chatTextBox.Update(time, IsChatInputActive());

            this.textCursorBlinkElapsedSeconds += time.ElapsedGameTime.TotalSeconds;

            UpdateAndroidKeyboard();

            if (this.isDragging)
            {
                int oldX = this.xPositionOnScreen;
                int oldY = this.yPositionOnScreen;
                this.xPositionOnScreen = Game1.getMouseX() - this.dragOffsetX;
                this.yPositionOnScreen = Game1.getMouseY() - this.dragOffsetY;
                if (this.xPositionOnScreen != oldX || this.yPositionOnScreen != oldY)
                {
                    this.smartphoneApi.SetPhonePosition(this.xPositionOnScreen, this.yPositionOnScreen);
                    SetupChatInputs();
                }
            }

            // Keep phone position synchronized if moved externally
            var (targetX, targetY) = this.smartphoneApi.GetPhonePosition();
            if (this.xPositionOnScreen != targetX || this.yPositionOnScreen != targetY)
            {
                this.xPositionOnScreen = targetX;
                this.yPositionOnScreen = targetY;
                SetupChatInputs();
            }

            var currentMessages = MessageManager.GetMessagesForNpc(this.npcName);
            if (currentMessages.Count != this.lastMessageCount)
            {
                this.lastMessageCount = currentMessages.Count;
                RebuildChatBubbles();
            }
        }

        protected override void cleanupBeforeExit()
        {
            if (Game1.keyboardDispatcher.Subscriber == this.textInputSubscriber)
            {
                Game1.keyboardDispatcher.Subscriber = null;
            }
            base.cleanupBeforeExit();
        }



        private float GetPhoneTextScale(float localScale = 1f)
        {
            float globalScale = this.phoneUiScale < 0.999f
                ? 0.85f // PhoneGlobalTextScale
                : 1f;

            return Math.Max(0.01f, localScale * globalScale);
        }

        private int GetPhoneScaledWrapWidth(int maxWidth, float localScale = 1f)
        {
            float safeScale = GetPhoneTextScale(localScale);
            return Math.Max(1, (int)Math.Floor(maxWidth / safeScale));
        }

        private int GetPhoneScaledLineHeight(SpriteFont font, float localScale = 1f, int extraPadding = 4)
        {
            int baseLineHeight = (int)font.MeasureString("A").Y + extraPadding;
            return Math.Max(1, (int)Math.Ceiling(baseLineHeight * GetPhoneTextScale(localScale)));
        }

        private Vector2 MeasurePhoneText(SpriteFont font, string text, float localScale = 1f)
        {
            return font.MeasureString(text ?? string.Empty) * GetPhoneTextScale(localScale);
        }

        private void DrawPhoneText(SpriteBatch b, SpriteFont font, string text, Vector2 position, Color color, float localScale = 1f, float layerDepth = 1f)
        {
            b.DrawString(
                font,
                text ?? string.Empty,
                position,
                color,
                0f,
                Vector2.Zero,
                GetPhoneTextScale(localScale),
                SpriteEffects.None,
                layerDepth);
        }

        private string GetTailTextToFit(string text, SpriteFont font, int maxWidth)
        {
            string visible = text ?? "";
            while (font.MeasureString(visible).X > maxWidth && visible.Length > 1)
                visible = visible.Substring(1);

            return visible;
        }

        private void DrawChatQuickActionButton(SpriteBatch b, Rectangle bounds, string label, bool active)
        {
            Color color = active
                ? new Color(205, 235, 255, 235)
                : new Color(255, 255, 255, 220);

            UI.CardDrawing.DrawCard(
                b,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                color,
                1f,
                false);

            string safeLabel = label ?? string.Empty;
            List<string> lines = SplitTextIntoLines(
                safeLabel,
                Game1.smallFont,
                GetPhoneScaledWrapWidth(Math.Max(24, bounds.Width - 18)));
            if (lines.Count == 0)
                lines.Add(safeLabel);

            int lineHeight = GetPhoneScaledLineHeight(Game1.smallFont, extraPadding: 2);
            int totalHeight = lines.Count * lineHeight;
            float startY = bounds.Y + Math.Max(2f, (bounds.Height - totalHeight) / 2f);

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                Vector2 lineSize = MeasurePhoneText(Game1.smallFont, line);
                Vector2 textPosition = new Vector2(
                    bounds.X + Math.Max(8f, (bounds.Width - lineSize.X) / 2f),
                    startY + i * lineHeight);
                DrawPhoneText(b, Game1.smallFont, line, textPosition, Color.Black);
            }
        }

        private void DrawChatQuickIconActionButton(
            SpriteBatch b,
            Rectangle bounds,
            bool active,
            Texture2D? useTextureIcon,
            Rectangle? useTextureSourceRect,
            Rectangle? useCursorIcon,
            int badgeCount)
        {
            Color boxColor = active
                ? Color.LightBlue
                : Color.LightGray;

            UI.CardDrawing.DrawCard(
                b,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                boxColor,
                1f,
                false);

            Rectangle iconBounds = new Rectangle(
                bounds.X + 8,
                bounds.Y + 6,
                Math.Max(16, bounds.Width - 16),
                Math.Max(16, bounds.Height - 12));

            if (useTextureIcon != null)
            {
                b.Draw(useTextureIcon, iconBounds, useTextureSourceRect, Color.White);
            }
            else if (useCursorIcon.HasValue)
            {
                b.Draw(Game1.mouseCursors, iconBounds, useCursorIcon.Value, Color.White);
            }

            if (badgeCount > 0)
            {
                string badgeText = badgeCount.ToString();
                Rectangle badgeBounds = new Rectangle(bounds.Right - 2, bounds.Y - 10, 24, 18);
                b.Draw(Game1.staminaRect, badgeBounds, new Color(215, 48, 48, 235));

                Vector2 badgeSize = MeasurePhoneText(Game1.smallFont, badgeText);
                Vector2 badgePos = new Vector2(
                    badgeBounds.X + (badgeBounds.Width - badgeSize.X) / 2f,
                    badgeBounds.Y + (badgeBounds.Height - badgeSize.Y) / 2f - 1f);
                DrawPhoneText(b, Game1.smallFont, badgeText, badgePos, Color.White);
            }
        }

        private bool ShouldShowAiCreditButton()
        {
            if (!(ModEntry.Config?.ShowAiCredit ?? true))
                return false;

            return ModEntry.IsSharedAiProviderMode();
        }

        private string FormatAiRefillTime(int rawTime)
        {
            int hour = Math.Clamp(rawTime / 100, 0, 26);
            int minute = Math.Clamp(rawTime % 100, 0, 59);
            int normalizedHour = ((hour % 24) + 24) % 24;
            int displayHour = normalizedHour % 12;
            if (displayHour == 0)
                displayHour = 12;

            string suffix = normalizedHour < 12 ? "AM" : "PM";
            return $"{displayHour}:{minute:00} {suffix}";
        }

        private string BuildAiCreditTooltipText()
        {
            var usage = ModEntry.GetAiUsageSnapshot();
            var lines = new List<string>
            {
                ModEntry.GetTranslation("chat.usage.daily-left", new { current = Math.Max(0, usage.DailyUsageLeft), max = usage.DailyUsageMax }),
                ModEntry.GetTranslation("chat.usage.credits-left", new { current = Math.Max(0, usage.CreditsLeft), max = usage.CreditsMax })
            };

            if (usage.CreditsLeft < usage.CreditsMax)
            {
                string nextRefillText = "--";
                if (ModEntry.TryGetNextAiCreditRefillTime(Game1.timeOfDay, out int nextRefillTime))
                    nextRefillText = FormatAiRefillTime(nextRefillTime);

                lines.Add(ModEntry.GetTranslation("chat.usage.next-refill", new { time = nextRefillText }));
            }

            lines.Add(ModEntry.GetTranslation("chat.usage.no-refill-note"));
            return string.Join("\n", lines);
        }

        private void DrawAiCreditTooltipIfHovered(SpriteBatch b)
        {
            if (!ShouldShowAiCreditButton() || this.chatAiCreditInfoBounds == Rectangle.Empty)
                return;

            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            if (!this.chatAiCreditInfoBounds.Contains(mouseX, mouseY))
                return;

            DrawSocialTagTooltip(b, BuildAiCreditTooltipText(), mouseX, mouseY);
        }

        private void DrawChatAiCreditInfoButton(SpriteBatch b, Rectangle bounds)
        {
            DrawChatQuickActionButton(b, bounds, "?", false);
        }

        public void DrawScreenContent(SpriteBatch b, Rectangle content)
        {
            float oldScale = this.phoneUiScale;
            int oldX = this.xPositionOnScreen;
            int oldY = this.yPositionOnScreen;
            int oldWidth = this.width;
            int oldHeight = this.height;
            int oldPhoneWidth = this.phoneFrameWidth;
            int oldPhoneHeight = this.phoneFrameHeight;
            int oldContentOffsetX = this.phoneContentOffsetX;
            int oldContentOffsetY = this.phoneContentOffsetY;
            int oldContentWidth = this.contentWidth;
            int oldContentHeight = this.contentHeight;

            try
            {
                this.phoneUiScale = 1f;
                this.phoneFrameWidth = 700;
                this.phoneFrameHeight = 1100;
                this.phoneContentOffsetX = 90;
                this.phoneContentOffsetY = 166;
                this.width = this.phoneFrameWidth;
                this.height = this.phoneFrameHeight;

                if (this.phoneBackgroundTexture != null && !this.phoneBackgroundTexture.IsDisposed)
                {
                    this.contentWidth = (int)Math.Round(this.phoneBackgroundTexture.Width * this.phoneUiScale);
                    this.contentHeight = (int)Math.Round(this.phoneBackgroundTexture.Height * this.phoneUiScale);
                }
                else
                {
                    this.contentWidth = Math.Max(1, this.phoneFrameWidth - (this.phoneContentOffsetX * 2));
                    this.contentHeight = Math.Max(1, this.phoneFrameHeight - this.phoneContentOffsetY - ScaleValue(80));
                }

                this.xPositionOnScreen = -this.phoneContentOffsetX;
                this.yPositionOnScreen = -this.phoneContentOffsetY;

                this.removeButton = new ClickableTextureComponent(
                    new Rectangle(0, 0, ScaleValue(32), ScaleValue(52)),
                    Game1.mouseCursors,
                    new Rectangle(564, 102, 16, 26),
                    1.6f * this.phoneUiScale);

                SetupChatInputs();
                RebuildChatBubbles();

                // Draw background wallpaper
                Rectangle contentRect = GetContentBounds();
                if (this.phoneBackgroundTexture != null && !this.phoneBackgroundTexture.IsDisposed)
                {
                    b.Draw(this.phoneBackgroundTexture, contentRect, Color.White);
                }
                else
                {
                    b.Draw(Game1.staminaRect, contentRect, new Color(30, 30, 30));
                }

                DrawContent(b, contentRect);

                // Draw Header on top
                SpriteFont font = Game1.dialogueFont;
                float textScale = 1.0f * this.phoneUiScale;
                Vector2 textPos = new Vector2(
                    this.xPositionOnScreen + this.phoneContentOffsetX + ScaleValue(65),
                    this.yPositionOnScreen + this.phoneContentOffsetY + ScaleValue(-45));

                NPC? headerNpc = Game1.getCharacterFromName(this.npcName);
                string headerDisplayName = headerNpc?.displayName ?? this.npcName;
                b.DrawString(font, headerDisplayName, textPos, Color.Black, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);

                this.removeButton.bounds.X = this.xPositionOnScreen + this.phoneContentOffsetX + ScaleValue(295);
                this.removeButton.bounds.Y = this.yPositionOnScreen + this.phoneContentOffsetY + ScaleValue(-47);
                this.removeButton.draw(b, Color.White * 0.8f, 1f);

                if (this.chatPhotoPickerOpen)
                {
                    DrawChatPhotoPickerMenu(b);
                }
            }
            finally
            {
                this.phoneUiScale = oldScale;
                this.xPositionOnScreen = oldX;
                this.yPositionOnScreen = oldY;
                this.width = oldWidth;
                this.height = oldHeight;
                this.phoneFrameWidth = oldPhoneWidth;
                this.phoneFrameHeight = oldPhoneHeight;
                this.phoneContentOffsetX = oldContentOffsetX;
                this.phoneContentOffsetY = oldContentOffsetY;
                this.contentWidth = oldContentWidth;
                this.contentHeight = oldContentHeight;

                this.removeButton = new ClickableTextureComponent(
                    new Rectangle(0, 0, ScaleValue(32), ScaleValue(52)),
                    Game1.mouseCursors,
                    new Rectangle(564, 102, 16, 26),
                    1.6f * this.phoneUiScale);

                SetupChatInputs();
                RebuildChatBubbles();
            }
        }
    }
}
