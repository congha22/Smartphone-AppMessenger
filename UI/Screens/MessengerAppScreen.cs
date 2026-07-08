using System;
using System.Collections.Generic;
using System.IO;
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
    public partial class MessengerAppScreen : IClickableMenu, IKeyboardSubscriber
    {
        private readonly ISmartPhoneApi smartphoneApi;
        private readonly Action onBack;

        // Layout bounds
        private int phoneFrameWidth;
        private int phoneFrameHeight;
        private int phoneContentOffsetX;
        private int phoneContentOffsetY;
        private float phoneUiScale;

        private Texture2D? phoneFrameTexture;
        private Texture2D? phoneBackgroundTexture;

        private int contentWidth;
        private int contentHeight;

        // Content
        private List<string> npcNames = new();

        // Drag State
        private bool isDragging;
        private int dragOffsetX;
        private int dragOffsetY;

        // Scroll State
        private int scrollOffset;
        private int maxScroll;
        private bool isScrolling;
        private int lastScrollMouseY;
        private int touchScrollStartY;
        private bool hasTouchScrolled;

        // Hover State
        private string? hoveredNpcName;
        private Dictionary<string, Rectangle> npcItemBounds = new();

        // Search Textbox state
        private EditableTextBox filterTextBox = new();
        private Task<string>? pendingKeyboardTask;

        // Sort delay state
        private bool isSortPending;
        private float sortDelayTimer;

        // IKeyboardSubscriber Implementation
        public bool Selected { get; set; }

        // State Machine
        public enum ScreenState { NpcList, ProfileEditor, AvatarPicker, ThemeList, ThemeDetail, NpcDetailText, ThemeHelpText }
        private ScreenState currentState = ScreenState.NpcList;
        public ScreenState CurrentState => this.currentState;

        // Theme Browsing State
        private string selectedTheme = "vanilla";
        private string selectedNpcName = "";
        private int activeTab = 0; // 0 = Overview, 1 = NPC Detail
        private List<string> availableThemes = new();
        private string themeOverviewText = "";
        private Dictionary<string, string> themeCharacteristicsLong = new();
        private string selectedNpcDetailText = "";

        private Dictionary<string, Rectangle> themeItemBounds = new();
        private Dictionary<string, Rectangle> themeNpcItemBounds = new();
        private Rectangle overviewTabBounds;
        private Rectangle npcDetailTabBounds;

        // Profile Editor State
        public enum ProfileField { None, Age, Birthday, AboutMe }
        private ProfileField activeProfileField = ProfileField.None;

        private EditableTextBox ageTextBox = new();
        private EditableTextBox birthdayTextBox = new() { IsNumericOnly = true, MinNumericValue = 1, MaxNumericValue = 28 };
        private string birthdaySeason = "Spring";
        private EditableTextBox aboutMeTextBox = new() { IsMultiline = true };
        private string avatarDraft = "";

        // Profile Editor Bounds
        private Rectangle profileAvatarCameraButtonBounds;
        private Rectangle profileAgeFieldBounds;
        private Rectangle profileBirthdayFieldBounds;
        private Rectangle profileSeasonButtonBounds;
        private Rectangle profileDescriptionFieldBounds;
        private Rectangle profileOkButtonBounds;
        private Rectangle profileAvatarBounds;

        // Avatar Picker State
        private readonly List<string> avatarPhotoCandidates = new();
        private int avatarPhotoCandidateIndex = -1;
        private string? avatarSelectedPhotoPath;

        // Avatar Picker Bounds
        private Rectangle avatarPickerPrevBounds;
        private Rectangle avatarPickerNextBounds;
        private Rectangle avatarPickerToggleBounds;
        private Rectangle avatarPickerCancelBounds;
        private Rectangle avatarPickerOkBounds;

        // Image Cache for Player Avatar
        private static readonly Dictionary<string, Texture2D> avatarImageCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> avatarFailedImagePaths = new(StringComparer.OrdinalIgnoreCase);

        public MessengerAppScreen(ISmartPhoneApi api, Action onBack)
            : base()
        {
            this.smartphoneApi = api;
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

            // Always select and focus search box
            this.Selected = true;
            Game1.keyboardDispatcher.Subscriber = this;

            // Rescan allowed NPCs on app open
            List<string> validNpcs = new();
            if (ModEntry.Config != null && !string.IsNullOrWhiteSpace(ModEntry.Config.AllowedNpc))
            {
                var names = ModEntry.Config.AllowedNpc.Split(',')
                    .Select(n => n.Trim())
                    .Where(n => !string.IsNullOrEmpty(n))
                    .ToList();

                string req = ModEntry.Config.FriendshipRequirement; // "Meet" or "Friend"
                int requiredPoints = string.Equals(req, "Friend", StringComparison.OrdinalIgnoreCase) ? 250 : 1;

                foreach (var name in names)
                {
                    var npc = Game1.getCharacterFromName(name);
                    if (npc != null)
                    {
                        string npcName = npc.Name;
                        if (ModEntry.NpcCharacteristicsMinimal.ContainsKey(npcName) &&
                            ModEntry.NpcCharacteristicsShort.ContainsKey(npcName) &&
                            ModEntry.NpcCharacteristicsLong.ContainsKey(npcName))
                        {
                            int points = Game1.player.getFriendshipLevelForNPC(name);
                            if (points >= requiredPoints)
                            {
                                validNpcs.Add(name);
                            }
                        }
                    }
                }
            }
            MessageManager.UpdateAvailableNpcs(validNpcs);

            CalculateLayout(rebuildList: true);
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

        private Rectangle GetSearchBoxBounds()
        {
            Rectangle content = GetContentBounds();
            int fontHeight = (int)Game1.smallFont.MeasureString("A").Y;
            int height = (int)(fontHeight * this.phoneUiScale) + ScaleValue(30);
            int buttonWidth = height; // Book/Theme buttons are square (height x height)
            int gap = ScaleValue(15);

            // We have 2 buttons now: theme button on left, book button on right.
            int totalAvailableWidth = content.Width - ScaleValue(40) - 2 * buttonWidth - 2 * gap;
            int searchWidth = (int)(totalAvailableWidth * 0.85f * 0.8f);

            // Center all together horizontally: ThemeButton + gap + SearchBox + gap + BookButton
            int totalWidth = searchWidth + 2 * gap + 2 * buttonWidth;
            int startX = content.X + (content.Width - totalWidth) / 2;

            // The search box starts after the theme button and gap
            return new Rectangle(
                startX + buttonWidth + gap,
                content.Bottom - height - ScaleValue(15),
                searchWidth,
                height);
        }

        private Rectangle GetThemeButtonBounds()
        {
            Rectangle searchBox = GetSearchBoxBounds();
            int gap = ScaleValue(15);
            return new Rectangle(
                searchBox.X - gap - searchBox.Height,
                searchBox.Y,
                searchBox.Height,
                searchBox.Height);
        }

        private Rectangle GetBookButtonBounds()
        {
            Rectangle searchBox = GetSearchBoxBounds();
            int gap = ScaleValue(15);
            return new Rectangle(
                searchBox.Right + gap,
                searchBox.Y,
                searchBox.Height,
                searchBox.Height);
        }

        private void CalculateLayout(bool rebuildList = true)
        {
            if (this.currentState == ScreenState.ThemeList ||
                this.currentState == ScreenState.ThemeDetail ||
                this.currentState == ScreenState.NpcDetailText ||
                this.currentState == ScreenState.ThemeHelpText)
            {
                CalculateTabsBounds();
                CalculateThemeLayout();
                return;
            }

            Rectangle content = GetContentBounds();

            this.npcItemBounds.Clear();

            if (rebuildList)
            {
                // 1. Get and filter contact list (NPCs + online players + players with chat history)
                var allNpcNames = MessageManager.GetAvailableNpcNames();
                var onlinePlayers = Game1.getOnlineFarmers()
                    .Select(f => f.Name)
                    .Where(name => !string.Equals(name, Game1.player.Name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var chatPlayers = MessageManager.PlayerConversations.Keys.ToList();

                var allContacts = allNpcNames
                    .Concat(onlinePlayers)
                    .Concat(chatPlayers)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var filteredNpcs = allContacts;

                if (!string.IsNullOrWhiteSpace(this.filterTextBox.Text))
                {
                    filteredNpcs = filteredNpcs
                        .Where(name =>
                        {
                            NPC? npc = Game1.getCharacterFromName(name);
                            string displayName = npc?.displayName ?? name;
                            return displayName.Contains(this.filterTextBox.Text, StringComparison.OrdinalIgnoreCase)
                                || name.Contains(this.filterTextBox.Text, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();
                }

                // 2. Sort by: Favourited first, then latest message time, then display name
                this.npcNames = filteredNpcs
                    .OrderByDescending(name => MessageManager.FavouriteNpcs.Contains(name) ? 1 : 0)
                    .ThenByDescending(name => MessageManager.LatestMessageTimestamps.TryGetValue(name, out int ts) ? ts : 0)
                    .ThenBy(name =>
                    {
                        NPC? npc = Game1.getCharacterFromName(name);
                        return npc?.displayName ?? name;
                    })
                    .ToList();
            }

            // 3. Lay out NPC item slots with 25% extra height and gap
            int itemHeight = (int)Math.Round(ScaleValue(80) * 1.25f);
            int currentY = ScaleValue(15); // Tripled top padding (was 5)
            int gap = ScaleValue(2); // Reduced gap between items (was 5)

            foreach (var npcName in this.npcNames)
            {
                this.npcItemBounds[npcName] = new Rectangle(
                    content.X,
                    currentY, // Local Y offset
                    this.contentWidth,
                    itemHeight);

                currentY += itemHeight + gap;
            }

            // Calculate max scrollable height (with tripled bottom padding)
            int totalHeight = currentY - gap + ScaleValue(15);
            Rectangle contentRect = GetContentBounds();
            Rectangle searchBox = GetSearchBoxBounds();
            int searchBoxAreaHeight = searchBox.Height + ScaleValue(25);
            int listClipHeight = contentRect.Height - searchBoxAreaHeight;

            this.maxScroll = Math.Max(0, totalHeight - listClipHeight);
            this.scrollOffset = Math.Clamp(this.scrollOffset, 0, this.maxScroll);
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

            if (this.currentState == ScreenState.ProfileEditor)
            {
                DrawProfileEditor(b);
            }
            else if (this.currentState == ScreenState.AvatarPicker)
            {
                DrawAvatarPicker(b);
            }
            else if (this.currentState == ScreenState.ThemeList)
            {
                DrawThemeList(b);
            }
            else if (this.currentState == ScreenState.ThemeDetail)
            {
                DrawThemeDetail(b);
            }
            else if (this.currentState == ScreenState.NpcDetailText)
            {
                DrawNpcDetailText(b);
            }
            else if (this.currentState == ScreenState.ThemeHelpText)
            {
                DrawThemeHelpText(b);
            }
            else
            {
                // Draw Search box and book button at the bottom (unclipped)
                DrawSearchArea(b);

                // Scissor rect to clip scrollable list above the search box area
                Rectangle searchBox = GetSearchBoxBounds();
                int searchBoxAreaHeight = searchBox.Height + ScaleValue(25);
                Rectangle listClipRect = new Rectangle(
                    contentRect.X,
                    contentRect.Y + ScaleValue(5),
                    contentRect.Width,
                    contentRect.Height - searchBoxAreaHeight);

                b.End();
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
                Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
                Game1.graphics.GraphicsDevice.ScissorRectangle = listClipRect;

                DrawScrollableContent(b, contentRect);

                b.End();
                Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
            }

            // Draw phone border on top
            if (this.phoneFrameTexture != null && !this.phoneFrameTexture.IsDisposed)
            {
                b.Draw(this.phoneFrameTexture, frameRect, Color.White);
            }

            this.smartphoneApi.DrawPhoneSizeButtons(b, this.xPositionOnScreen, this.yPositionOnScreen);

            drawMouse(b);
        }

        private void DrawSearchArea(SpriteBatch b)
        {
            Rectangle searchBox = GetSearchBoxBounds();

            // Draw textbox background
            UI.CardDrawing.DrawCard(
                b,
                searchBox.X,
                searchBox.Y,
                searchBox.Width,
                searchBox.Height,
                Color.LightCyan,
                1f,
                false);

            // Draw placeholder or typed text
            if (string.IsNullOrEmpty(this.filterTextBox.Text))
            {
                string textToDraw = ModEntry.GetTranslation("screen.search-placeholder");
                Color textColor = Color.Gray;
                SpriteFont font = Game1.smallFont;
                float textScale = 0.8f * this.phoneUiScale;
                Vector2 textSize = font.MeasureString(textToDraw) * textScale;
                Vector2 textPos = new Vector2(
                    searchBox.X + ScaleValue(17),
                    searchBox.Y + (searchBox.Height - textSize.Y) / 2f);

                b.DrawString(font, textToDraw, textPos, textColor, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);
            }
            else
            {
                // Draw EditableTextBox
                this.filterTextBox.Draw(b, searchBox, 1f * this.phoneUiScale, this.Selected);
            }

            // Draw theme button next to search box on left
            Rectangle themeBounds = GetThemeButtonBounds();
            b.Draw(
                Game1.mouseCursors,
                themeBounds,
                new Rectangle(294, 392, 16, 16),
                Color.White);

            // Draw book button next to search box
            Rectangle bookBounds = GetBookButtonBounds();
            b.Draw(
                Game1.mouseCursors_1_6,
                bookBounds,
                new Rectangle(1, 277, 19, 19),
                Color.White);
        }

        private void DrawScrollableContent(SpriteBatch b, Rectangle contentRect)
        {
            SpriteFont font = Game1.dialogueFont;
            Rectangle searchBox = GetSearchBoxBounds();
            int searchBoxAreaHeight = searchBox.Height + ScaleValue(25);
            Rectangle listClipRect = new Rectangle(
                contentRect.X,
                contentRect.Y + ScaleValue(5),
                contentRect.Width,
                contentRect.Height - searchBoxAreaHeight);

            foreach (var kvp in this.npcItemBounds)
            {
                string npcName = kvp.Key;
                Rectangle localBounds = kvp.Value;

                // Adjust bounds by content rect and scroll offset
                Rectangle actualBounds = new Rectangle(
                    localBounds.X,
                    contentRect.Y - this.scrollOffset + localBounds.Y,
                    localBounds.Width,
                    localBounds.Height);

                // Only draw if within list clip bounds
                if (actualBounds.Bottom < listClipRect.Top || actualBounds.Top > listClipRect.Bottom)
                    continue;

                // Centered portrait dimensions (20% bigger than previous 72 & 56)
                int bgSize = ScaleValue(86);
                int portraitSize = ScaleValue(67);
                int portraitXOffset = ScaleValue(50); // Move portrait to the right a little more

                // Draw portrait background box
                Rectangle bgDest = new Rectangle(
                    actualBounds.X + portraitXOffset,
                    actualBounds.Y + (actualBounds.Height - bgSize) / 2,
                    bgSize,
                    bgSize);

                if (ModEntry.PortraitBackgroundTexture != null)
                {
                    b.Draw(ModEntry.PortraitBackgroundTexture, bgDest, Color.White);
                }

                // Draw NPC or Player portrait
                NPC? npc = Game1.getCharacterFromName(npcName);
                Rectangle portraitDest = new Rectangle(
                    bgDest.X + (bgSize - portraitSize) / 2,
                    bgDest.Y + (bgSize - portraitSize) / 2,
                    portraitSize,
                    portraitSize);

                if (npc != null)
                {
                    try
                    {
                        b.Draw(npc.Portrait, portraitDest, new Rectangle(0, 0, 64, 64), Color.White);
                    }
                    catch
                    {
                        b.Draw(Game1.staminaRect, portraitDest, Color.Gray);
                    }
                }
                else
                {
                    if (TryGetContactAvatarTexture(npcName, out Texture2D playerAvatar))
                    {
                        b.Draw(playerAvatar, portraitDest, Color.White);
                    }
                    else
                    {
                        // Draw player initials as fallback inside a nice background box
                        UI.CardDrawing.DrawCard(
                            b,
                            portraitDest.X,
                            portraitDest.Y,
                            portraitDest.Width,
                            portraitDest.Height,
                            new Color(230, 230, 230, 220),
                            1f,
                            false);

                        string initial = string.IsNullOrWhiteSpace(npcName) ? "P" : npcName.Trim()[0].ToString().ToUpperInvariant();
                        Vector2 initialSize = Game1.smallFont.MeasureString(initial) * this.phoneUiScale;
                        Vector2 initialPos = new Vector2(
                            portraitDest.X + (portraitDest.Width - initialSize.X) / 2f,
                            portraitDest.Y + (portraitDest.Height - initialSize.Y) / 2f);
                        b.DrawString(Game1.smallFont, initial, initialPos, Color.Gray, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
                    }
                }

                // Draw unread counts on the left of portrait in Stardew-style digits (increased size 20%)
                if (MessageManager.UnreadCounts.TryGetValue(npcName, out int unreadCount) && unreadCount > 0)
                {
                    string numberStr = Math.Min(unreadCount, 9).ToString();
                    int digitWidth = 8;
                    int digitHeight = 8;
                    int digitsPerRow = 6;

                    // Centered vertically to the left of the portrait
                    int numberSize = ScaleValue(38); // 20% larger than 32
                    int numberX = actualBounds.X + ScaleValue(12);
                    int numberY = actualBounds.Y + (actualBounds.Height - numberSize) / 2;

                    foreach (char c in numberStr)
                    {
                        if (char.IsDigit(c))
                        {
                            int digit = c - '0';
                            int row = digit / digitsPerRow;
                            int col = digit % digitsPerRow;

                            Rectangle digitSource = new Rectangle(
                                512 + col * digitWidth,
                                128 + row * digitHeight,
                                digitWidth,
                                digitHeight);

                            b.Draw(
                                Game1.mouseCursors,
                                new Rectangle(numberX, numberY, numberSize, numberSize),
                                digitSource,
                                Color.White);
                        }
                    }
                }

                // Draw NPC display name (scale increased by 15% to 0.9775f, shadow removed)
                string displayName = npc?.displayName ?? npcName;
                float textScale = 0.85f * 1.15f * this.phoneUiScale;
                Vector2 textSize = font.MeasureString(displayName) * textScale;
                Vector2 textPos = new Vector2(bgDest.Right + ScaleValue(18), actualBounds.Center.Y - (textSize.Y / 2f));

                b.DrawString(font, displayName, textPos, Color.Black, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);

                // Draw action buttons (reduced by 10%: size 31)
                int buttonSize = ScaleValue(31);
                int buttonY = actualBounds.Center.Y - (buttonSize / 2);

                var socialApi = ModEntry.Instance.Helper.ModRegistry.GetApi<IStardewSocialApi>("d5a1lamdtd.Smartphone-AppStardewSocial");

                Rectangle profileButtonBounds = Rectangle.Empty;
                Rectangle heartButtonBounds = Rectangle.Empty;

                if (socialApi != null)
                {
                    int profileButtonX = actualBounds.Right - ScaleValue(10) - buttonSize;
                    profileButtonBounds = new Rectangle(profileButtonX, buttonY, buttonSize, buttonSize);

                    int heartButtonX = profileButtonX - ScaleValue(8) - buttonSize;
                    heartButtonBounds = new Rectangle(heartButtonX, buttonY, buttonSize, buttonSize);

                    // Heart Button
                    bool isFavourited = MessageManager.FavouriteNpcs.Contains(npcName);
                    Rectangle heartSource = isFavourited ? new Rectangle(211, 428, 7, 7) : new Rectangle(218, 428, 7, 7);
                    b.Draw(Game1.mouseCursors, heartButtonBounds, heartSource, Color.White);

                    // Profile Button (dummy magnifier glass directly drawn, no background)
                    b.Draw(Game1.mouseCursors, profileButtonBounds, new Rectangle(80, 0, 13, 13), Color.White);
                }
                else
                {
                    // If Stardew Social is not installed, hide profile button and shift heart button to the rightmost position
                    int heartButtonX = actualBounds.Right - ScaleValue(10) - buttonSize;
                    heartButtonBounds = new Rectangle(heartButtonX, buttonY, buttonSize, buttonSize);

                    // Heart Button
                    bool isFavourited = MessageManager.FavouriteNpcs.Contains(npcName);
                    Rectangle heartSource = isFavourited ? new Rectangle(211, 428, 7, 7) : new Rectangle(218, 428, 7, 7);
                    b.Draw(Game1.mouseCursors, heartButtonBounds, heartSource, Color.White);
                }
            }
        }

        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);

            if (this.currentState != ScreenState.NpcList)
            {
                this.hoveredNpcName = null;
                return;
            }

            Rectangle contentRect = GetContentBounds();
            this.hoveredNpcName = null;

            Rectangle searchBox = GetSearchBoxBounds();
            int searchBoxAreaHeight = searchBox.Height + ScaleValue(25);
            Rectangle listClipRect = new Rectangle(
                contentRect.X,
                contentRect.Y,
                contentRect.Width,
                contentRect.Height - searchBoxAreaHeight);

            if (listClipRect.Contains(x, y))
            {
                foreach (var kvp in this.npcItemBounds)
                {
                    Rectangle localBounds = kvp.Value;
                    Rectangle actualBounds = new Rectangle(
                        localBounds.X,
                        contentRect.Y - this.scrollOffset + localBounds.Y,
                        localBounds.Width,
                        localBounds.Height);

                    if (actualBounds.Contains(x, y))
                    {
                        this.hoveredNpcName = kvp.Key;
                        break;
                    }
                }
            }
        }

        public bool IsAnyProfileFieldActive()
        {
            return this.currentState == ScreenState.ProfileEditor && this.activeProfileField != ProfileField.None;
        }

        private void NavigateBack()
        {
            if (this.currentState == ScreenState.AvatarPicker)
            {
                this.currentState = ScreenState.ProfileEditor;
                Game1.playSound("bigDeSelect");
            }
            else if (this.currentState == ScreenState.ProfileEditor)
            {
                this.currentState = ScreenState.NpcList;
                Game1.playSound("bigDeSelect");
            }
            else if (this.currentState == ScreenState.NpcDetailText)
            {
                this.currentState = ScreenState.ThemeDetail;
                this.scrollOffset = 0;
                CalculateThemeLayout();
                Game1.playSound("bigDeSelect");
            }
            else if (this.currentState == ScreenState.ThemeHelpText)
            {
                this.currentState = ScreenState.ThemeList;
                this.scrollOffset = 0;
                CalculateThemeLayout();
                Game1.playSound("bigDeSelect");
            }
            else if (this.currentState == ScreenState.ThemeDetail)
            {
                this.currentState = ScreenState.ThemeList;
                this.scrollOffset = 0;
                CalculateThemeLayout();
                Game1.playSound("bigDeSelect");
            }
            else if (this.currentState == ScreenState.ThemeList)
            {
                this.currentState = ScreenState.NpcList;
                this.scrollOffset = 0;
                CalculateLayout(rebuildList: false);
                Game1.playSound("bigDeSelect");
            }
            else
            {
                if (Game1.keyboardDispatcher.Subscriber == this)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                }
                this.onBack?.Invoke();
            }
        }

        public override void receiveKeyPress(Microsoft.Xna.Framework.Input.Keys key)
        {
            bool isTyping = KeyboardManager.IsTextInputActive(this) || this.activeProfileField != ProfileField.None;
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

            if (key == Microsoft.Xna.Framework.Input.Keys.Escape)
            {
                NavigateBack();
                return;
            }

            if (KeyboardManager.IsTextInputActive(this))
            {
                if (this.currentState == ScreenState.ProfileEditor)
                {
                    TryApplyProfileEditorKey(key);
                }
                return;
            }

            base.receiveKeyPress(key);
        }

        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            if (this.smartphoneApi.HandlePhoneSizeButtonsClick(x, y, this.xPositionOnScreen, this.yPositionOnScreen))
            {
                return;
            }

            if (this.smartphoneApi.HandlePhoneAppBottomNavClick(x, y, this.xPositionOnScreen, this.yPositionOnScreen, onBack: NavigateBack))
            {
                return;
            }

            if (this.currentState == ScreenState.NpcList ||
                this.currentState == ScreenState.ThemeList ||
                this.currentState == ScreenState.ThemeDetail ||
                this.currentState == ScreenState.NpcDetailText ||
                this.currentState == ScreenState.ThemeHelpText)
            {
                if (this.currentState == ScreenState.NpcList)
                {
                    // Always select and focus search box when clicking within app content
                    Rectangle contentRect = GetContentBounds();
                    if (contentRect.Contains(x, y))
                    {
                        this.Selected = true;
                        Game1.keyboardDispatcher.Subscriber = this;

                        Rectangle searchBox = GetSearchBoxBounds();
                        if (searchBox.Contains(x, y))
                        {
                            if (Constants.TargetPlatform == GamePlatform.Android)
                            {
                                TriggerAndroidKeyboard(this.filterTextBox.Text);
                            }
                            else
                            {
                                this.filterTextBox.SetCursorFromClick(x, searchBox, 0.8f * this.phoneUiScale);
                            }
                        }
                    }
                }

                this.lastScrollMouseY = y;
                this.touchScrollStartY = y;
                this.hasTouchScrolled = false;
                this.isScrolling = false;
            }
            else if (this.currentState == ScreenState.ProfileEditor)
            {
                this.activeProfileField = ProfileField.None;

                if (this.profileAgeFieldBounds.Contains(x, y))
                {
                    this.activeProfileField = ProfileField.Age;
                    if (Constants.TargetPlatform == GamePlatform.Android)
                    {
                        TriggerAndroidKeyboard(this.ageTextBox.Text);
                    }
                    else
                    {
                        this.ageTextBox.SetCursorFromClick(x, this.profileAgeFieldBounds, this.phoneUiScale);
                    }
                    Game1.playSound("smallSelect");
                }
                else if (this.profileBirthdayFieldBounds.Contains(x, y))
                {
                    this.activeProfileField = ProfileField.Birthday;
                    if (Constants.TargetPlatform == GamePlatform.Android)
                    {
                        TriggerAndroidKeyboard(this.birthdayTextBox.Text);
                    }
                    else
                    {
                        this.birthdayTextBox.SetCursorFromClick(x, this.profileBirthdayFieldBounds, this.phoneUiScale);
                    }
                    Game1.playSound("smallSelect");
                }
                else if (this.profileDescriptionFieldBounds.Contains(x, y))
                {
                    this.activeProfileField = ProfileField.AboutMe;
                    if (Constants.TargetPlatform == GamePlatform.Android)
                    {
                        TriggerAndroidKeyboard(this.aboutMeTextBox.Text);
                    }
                    else
                    {
                        this.aboutMeTextBox.CursorIndex = (this.aboutMeTextBox.Text ?? "").Length;
                        this.aboutMeTextBox.SelectionAnchorIndex = this.aboutMeTextBox.CursorIndex;
                    }
                    Game1.playSound("smallSelect");
                }
                else if (this.profileSeasonButtonBounds.Contains(x, y))
                {
                    this.birthdaySeason = this.birthdaySeason switch
                    {
                        "Spring" => "Summer",
                        "Summer" => "Fall",
                        "Fall" => "Winter",
                        "Winter" => "Spring",
                        _ => "Spring"
                    };
                    Game1.playSound("shwip");
                }
                else if (this.profileAvatarCameraButtonBounds.Contains(x, y))
                {
                    Game1.playSound("smallSelect");
                    Game1.activeClickableMenu = null;

                    var api = this.smartphoneApi;
                    var backAction = this.onBack;

                    api.RetrievePhotos(limit: 1, getTexture: true, getMetadata: false, onComplete: (jsonResult) =>
                    {
                        var screen = new MessengerAppScreen(api, backAction);
                        screen.OpenProfileEditor();
                        Game1.activeClickableMenu = screen;

                        List<SelectedPhotoResult>? results = null;
                        try
                        {
                            results = string.IsNullOrWhiteSpace(jsonResult)
                                ? null
                                : Newtonsoft.Json.JsonConvert.DeserializeObject<List<SelectedPhotoResult>>(jsonResult);
                        }
                        catch (Exception ex)
                        {
                            ModEntry.Instance.Monitor.Log($"Failed to deserialize avatar photo result: {ex.Message}", LogLevel.Error);
                        }

                        if (results != null && results.Count > 0 && results[0].TextureData != null)
                        {
                            try
                            {
                                string activeSave = MessageManager.GetActiveSaveFolderName();
                                string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                                Directory.CreateDirectory(photoSharedDir);

                                string id = Game1.player.UniqueMultiplayerID.ToString();
                                foreach (var oldFile in Directory.GetFiles(photoSharedDir, $"{id}_avatar.*"))
                                {
                                    try { File.Delete(oldFile); } catch { }
                                }

                                string destPath = Path.Combine(photoSharedDir, $"{id}_avatar.jpg");
                                File.WriteAllBytes(destPath, results[0].TextureData);

                                ClearAvatarCache();

                                MessageManager.currentPlayerAvatar = destPath;
                                screen.avatarDraft = destPath;
                                MessageManager.SavePlayerProfile(ModEntry.Instance.Helper);

                                if (!string.IsNullOrWhiteSpace(MessageManager.currentPlayerAvatar))
                                {
                                    TransferManager.SendSelectedAvatar(MessageManager.currentPlayerAvatar);
                                }
                            }
                            catch (Exception ex)
                            {
                                ModEntry.Instance.Monitor.Log($"Failed to save avatar photo: {ex.Message}", LogLevel.Error);
                            }
                        }
                    }, squareOnly: true);
                }
                else if (this.profileOkButtonBounds.Contains(x, y))
                {
                    SaveProfileData();
                    this.currentState = ScreenState.NpcList;
                    Game1.playSound("money");
                }
            }
            else if (this.currentState == ScreenState.AvatarPicker)
            {
                if (this.avatarPickerPrevBounds.Contains(x, y))
                {
                    this.avatarPhotoCandidateIndex--;
                    if (this.avatarPhotoCandidateIndex < 0)
                        this.avatarPhotoCandidateIndex = this.avatarPhotoCandidates.Count - 1;
                    Game1.playSound("shwip");
                }
                else if (this.avatarPickerNextBounds.Contains(x, y))
                {
                    this.avatarPhotoCandidateIndex++;
                    if (this.avatarPhotoCandidateIndex >= this.avatarPhotoCandidates.Count)
                        this.avatarPhotoCandidateIndex = 0;
                    Game1.playSound("shwip");
                }
                else if (this.avatarPickerToggleBounds.Contains(x, y))
                {
                    if (this.avatarPhotoCandidates.Count > 0 && this.avatarPhotoCandidateIndex >= 0)
                    {
                        string currentPath = this.avatarPhotoCandidates[this.avatarPhotoCandidateIndex];
                        if (string.Equals(this.avatarSelectedPhotoPath, currentPath, StringComparison.OrdinalIgnoreCase))
                            this.avatarSelectedPhotoPath = "";
                        else
                            this.avatarSelectedPhotoPath = currentPath;
                        Game1.playSound("smallSelect");
                    }
                }
                else if (this.avatarPickerCancelBounds.Contains(x, y))
                {
                    this.currentState = ScreenState.ProfileEditor;
                    Game1.playSound("bigDeSelect");
                }
                else if (this.avatarPickerOkBounds.Contains(x, y))
                {
                    SaveAvatarRightAway(this.avatarSelectedPhotoPath ?? "");
                    this.currentState = ScreenState.ProfileEditor;
                    Game1.playSound("smallSelect");
                }
            }
        }

        public override void receiveScrollWheelAction(int direction)
        {
            base.receiveScrollWheelAction(direction);
            if (this.currentState != ScreenState.NpcList &&
                this.currentState != ScreenState.ThemeList &&
                this.currentState != ScreenState.ThemeDetail &&
                this.currentState != ScreenState.NpcDetailText &&
                this.currentState != ScreenState.ThemeHelpText)
                return;

            int scrollAmount = ScaleValue(40);
            if (direction > 0)
            {
                this.scrollOffset -= scrollAmount;
            }
            else if (direction < 0)
            {
                this.scrollOffset += scrollAmount;
            }
            this.scrollOffset = Math.Clamp(this.scrollOffset, 0, this.maxScroll);
        }

        public override void releaseLeftClick(int x, int y)
        {
            base.releaseLeftClick(x, y);

            if (!this.hasTouchScrolled)
            {
                if (this.currentState == ScreenState.ThemeList)
                {
                    HandleThemeListClick(x, y);
                }
                else if (this.currentState == ScreenState.ThemeDetail)
                {
                    HandleThemeDetailClick(x, y);
                }
                else if (this.currentState == ScreenState.NpcDetailText)
                {
                    HandleNpcDetailTextClick(x, y);
                }
                else if (this.currentState == ScreenState.ThemeHelpText)
                {
                    HandleThemeHelpTextClick(x, y);
                }
                else if (this.currentState == ScreenState.NpcList)
                {
                    Rectangle contentRect = GetContentBounds();
                    if (contentRect.Contains(x, y))
                    {
                        Rectangle searchBox = GetSearchBoxBounds();
                        Rectangle bookBounds = GetBookButtonBounds();
                        Rectangle themeBounds = GetThemeButtonBounds();

                        if (searchBox.Contains(x, y))
                        {
                            return; // Handled in receiveLeftClick
                        }

                        ModEntry.Instance.Monitor.Log($"releaseLeftClick: x={x}, y={y}, hasTouchScrolled={this.hasTouchScrolled}, currentState={this.currentState}", LogLevel.Debug);
                        ModEntry.Instance.Monitor.Log($"themeBounds: {themeBounds}, bookBounds: {bookBounds}, searchBox: {searchBox}", LogLevel.Debug);

                        if (themeBounds.Contains(x, y))
                        {
                            ModEntry.Instance.Monitor.Log("Theme button clicked! Calling OpenThemeList...", LogLevel.Debug);
                            Game1.playSound("smallSelect");
                            OpenThemeList();
                            return;
                        }

                        if (bookBounds.Contains(x, y))
                        {
                            ModEntry.Instance.Monitor.Log("Book button clicked! Calling OpenProfileEditor...", LogLevel.Debug);
                            Game1.playSound("smallSelect");
                            OpenProfileEditor();
                            return;
                        }

                    Rectangle sBox = GetSearchBoxBounds();
                    int searchBoxAreaHeight = sBox.Height + ScaleValue(25);
                    Rectangle listClipRect = new Rectangle(
                        contentRect.X,
                        contentRect.Y,
                        contentRect.Width,
                        contentRect.Height - searchBoxAreaHeight);

                    foreach (var kvp in this.npcItemBounds)
                    {
                        string npcName = kvp.Key;
                        Rectangle localBounds = kvp.Value;
                        Rectangle actualBounds = new Rectangle(
                            localBounds.X,
                            contentRect.Y - this.scrollOffset + localBounds.Y,
                            localBounds.Width,
                            localBounds.Height);

                        if (actualBounds.Bottom < listClipRect.Top || actualBounds.Top > listClipRect.Bottom)
                            continue;

                        if (actualBounds.Contains(x, y))
                        {
                            var socialApi = ModEntry.Instance.Helper.ModRegistry.GetApi<IStardewSocialApi>("d5a1lamdtd.Smartphone-AppStardewSocial");

                            int buttonSize = ScaleValue(31);
                            int buttonY = actualBounds.Center.Y - (buttonSize / 2);

                            Rectangle profileButtonBounds = Rectangle.Empty;
                            Rectangle heartButtonBounds = Rectangle.Empty;

                            if (socialApi != null)
                            {
                                int profileButtonX = actualBounds.Right - ScaleValue(10) - buttonSize;
                                profileButtonBounds = new Rectangle(profileButtonX, buttonY, buttonSize, buttonSize);

                                int heartButtonX = profileButtonX - ScaleValue(8) - buttonSize;
                                heartButtonBounds = new Rectangle(heartButtonX, buttonY, buttonSize, buttonSize);
                            }
                            else
                            {
                                int heartButtonX = actualBounds.Right - ScaleValue(10) - buttonSize;
                                heartButtonBounds = new Rectangle(heartButtonX, buttonY, buttonSize, buttonSize);
                            }

                            if (heartButtonBounds.Contains(x, y))
                            {
                                Game1.playSound("coin");
                                if (MessageManager.FavouriteNpcs.Contains(npcName))
                                {
                                    MessageManager.FavouriteNpcs.Remove(npcName);
                                }
                                else
                                {
                                    MessageManager.FavouriteNpcs.Add(npcName);
                                }

                                // Start 0.5s delay timer before actual list sorting is updated
                                this.isSortPending = true;
                                this.sortDelayTimer = 500f;
                                return;
                            }

                            if (socialApi != null && profileButtonBounds.Contains(x, y))
                            {
                                Game1.playSound("smallSelect");
                                socialApi.OpenProfile(npcName);
                                return;
                            }

                            Game1.playSound("bigSelect");

                            // Open Chat Screen and reset unread count (do not call SaveMetadata immediately)
                            MessageManager.UnreadCounts[npcName] = 0;

                            Game1.activeClickableMenu = new MessengerChatScreen(
                                this.smartphoneApi,
                                npcName,
                                () =>
                                {
                                    MessageManager.UnreadCounts[npcName] = 0;
                                    Game1.activeClickableMenu = new MessengerAppScreen(this.smartphoneApi, this.onBack);
                                });

                            break;
                        }
                    }
                }
            }
        }

            this.isDragging = false;
            this.isScrolling = false;
        }

        public override void leftClickHeld(int x, int y)
        {
            base.leftClickHeld(x, y);

            if (!this.isDragging && !this.isScrolling)
            {
                Rectangle frameBounds = GetFrameBounds();
                Rectangle contentBounds = GetContentBounds();

                if ((this.currentState == ScreenState.NpcList ||
                     this.currentState == ScreenState.ThemeList ||
                     this.currentState == ScreenState.ThemeDetail ||
                     this.currentState == ScreenState.NpcDetailText ||
                     this.currentState == ScreenState.ThemeHelpText) && contentBounds.Contains(x, y))
                {
                    this.isScrolling = true;
                    this.lastScrollMouseY = y;
                }
                else if (frameBounds.Contains(x, y) && !contentBounds.Contains(x, y))
                {
                    this.isDragging = true;
                    this.dragOffsetX = x - this.xPositionOnScreen;
                    this.dragOffsetY = y - this.yPositionOnScreen;
                }
            }

            if (this.isScrolling)
            {
                if (Math.Abs(y - this.touchScrollStartY) > 5)
                    this.hasTouchScrolled = true;

                int deltaY = y - this.lastScrollMouseY;
                this.lastScrollMouseY = y;
                if (deltaY != 0)
                {
                    this.scrollOffset -= deltaY;
                    this.scrollOffset = Math.Clamp(this.scrollOffset, 0, this.maxScroll);
                }
            }
        }

        public override void update(GameTime time)
        {
            base.update(time);

            this.filterTextBox.Update(time, this.Selected && this.currentState == ScreenState.NpcList);
            this.ageTextBox.Update(time, this.Selected && this.currentState == ScreenState.ProfileEditor && this.activeProfileField == ProfileField.Age);
            this.birthdayTextBox.Update(time, this.Selected && this.currentState == ScreenState.ProfileEditor && this.activeProfileField == ProfileField.Birthday);
            this.aboutMeTextBox.Update(time, this.Selected && this.currentState == ScreenState.ProfileEditor && this.activeProfileField == ProfileField.AboutMe);

            UpdateAndroidKeyboard();

            // Always enforce keyboard focus for typing
            if (Game1.keyboardDispatcher.Subscriber != this)
            {
                Game1.keyboardDispatcher.Subscriber = this;
                this.Selected = true;
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

                CalculateLayout(rebuildList: false);

                if (this.currentState == ScreenState.ThemeList ||
                    this.currentState == ScreenState.ThemeDetail ||
                    this.currentState == ScreenState.NpcDetailText ||
                    this.currentState == ScreenState.ThemeHelpText)
                {
                    CalculateTabsBounds();
                    CalculateThemeLayout();
                }
            }

            // Handle sorting delay
            if (this.isSortPending)
            {
                this.sortDelayTimer -= (float)time.ElapsedGameTime.TotalMilliseconds;
                if (this.sortDelayTimer <= 0)
                {
                    this.isSortPending = false;
                    CalculateLayout(rebuildList: true);
                }
            }

            if (this.isDragging)
            {
                int oldX = this.xPositionOnScreen;
                int oldY = this.yPositionOnScreen;
                this.xPositionOnScreen = Game1.getMouseX() - this.dragOffsetX;
                this.yPositionOnScreen = Game1.getMouseY() - this.dragOffsetY;
                if (this.xPositionOnScreen != oldX || this.yPositionOnScreen != oldY)
                {
                    this.smartphoneApi.SetPhonePosition(this.xPositionOnScreen, this.yPositionOnScreen);
                    CalculateLayout(rebuildList: false);
                }
            }

            // Keep phone position synchronized if moved externally
            var (targetX, targetY) = this.smartphoneApi.GetPhonePosition();
            if (this.xPositionOnScreen != targetX || this.yPositionOnScreen != targetY)
            {
                this.xPositionOnScreen = targetX;
                this.yPositionOnScreen = targetY;
                CalculateLayout(rebuildList: false);
            }
        }

        protected override void cleanupBeforeExit()
        {
            if (Game1.keyboardDispatcher.Subscriber == this)
            {
                Game1.keyboardDispatcher.Subscriber = null;
            }
            base.cleanupBeforeExit();
        }

        // IKeyboardSubscriber Text Input Handling
        public void RecieveTextInput(char inputChar)
        {
            if (!Selected) return;

            if (this.currentState == ScreenState.ProfileEditor)
            {
                if (!char.IsControl(inputChar))
                {
                    ApplyTextInputToActiveField(inputChar.ToString());
                }
            }
            else if (this.currentState == ScreenState.NpcList)
            {
                if (!char.IsControl(inputChar))
                {
                    this.filterTextBox.RecieveTextInput(inputChar.ToString());
                    CalculateLayout(rebuildList: true);
                }
            }
        }

        public void RecieveTextInput(string text)
        {
            if (!Selected) return;

            if (this.currentState == ScreenState.ProfileEditor)
            {
                ApplyTextInputToActiveField(text);
            }
            else if (this.currentState == ScreenState.NpcList)
            {
                this.filterTextBox.RecieveTextInput(text);
                CalculateLayout(rebuildList: true);
            }
        }

        public void RecieveCommandInput(char command)
        {
            if (!Selected) return;

            if (command == '\b') // Backspace
            {
                if (this.currentState == ScreenState.ProfileEditor)
                {
                    ApplyBackspaceToActiveField();
                }
                else if (this.currentState == ScreenState.NpcList)
                {
                    this.filterTextBox.RecieveBackspace();
                    CalculateLayout(rebuildList: true);
                }
            }
        }

        public void RecieveSpecialInput(Keys key)
        {
            if (!Selected) return;
            if (this.currentState == ScreenState.NpcList)
            {
                if (this.filterTextBox.HandleKeyPress(key))
                {
                    CalculateLayout(rebuildList: true);
                }
            }
        }

        private void TriggerAndroidKeyboard(string currentText)
        {
            try
            {
                Type? keyboardInputType = typeof(Microsoft.Xna.Framework.Input.Keyboard).Assembly.GetType("Microsoft.Xna.Framework.Input.KeyboardInput");
                if (keyboardInputType != null)
                {
                    var showMethod = keyboardInputType.GetMethod("Show", new[] { typeof(string), typeof(string), typeof(string), typeof(bool) });
                    if (showMethod != null)
                    {
                        this.pendingKeyboardTask = (Task<string>)showMethod.Invoke(null, new object[] { ModEntry.GetTranslation("keyboard.filter.title"), ModEntry.GetTranslation("keyboard.filter.description"), currentText, false })!;
                    }
                }
            }
            catch (Exception)
            {
                this.pendingKeyboardTask = null;
            }
        }

        private void UpdateAndroidKeyboard()
        {
            if (this.pendingKeyboardTask != null && this.pendingKeyboardTask.IsCompleted)
            {
                if (!this.pendingKeyboardTask.IsFaulted && this.pendingKeyboardTask.Result != null)
                {
                    if (this.currentState == ScreenState.ProfileEditor)
                    {
                        if (this.activeProfileField == ProfileField.Age)
                        {
                            this.ageTextBox.Text = this.pendingKeyboardTask.Result;
                            this.ageTextBox.CursorIndex = this.ageTextBox.Text.Length;
                            this.ageTextBox.SelectionAnchorIndex = this.ageTextBox.Text.Length;
                        }
                        else if (this.activeProfileField == ProfileField.Birthday)
                        {
                            this.birthdayTextBox.Text = this.pendingKeyboardTask.Result;
                            this.birthdayTextBox.CursorIndex = this.birthdayTextBox.Text.Length;
                            this.birthdayTextBox.SelectionAnchorIndex = this.birthdayTextBox.Text.Length;
                        }
                        else if (this.activeProfileField == ProfileField.AboutMe)
                        {
                            this.aboutMeTextBox.Text = this.pendingKeyboardTask.Result;
                            this.aboutMeTextBox.CursorIndex = this.aboutMeTextBox.Text.Length;
                            this.aboutMeTextBox.SelectionAnchorIndex = this.aboutMeTextBox.Text.Length;
                        }
                    }
                    else if (this.currentState == ScreenState.NpcList)
                    {
                        this.filterTextBox.Text = this.pendingKeyboardTask.Result;
                        this.filterTextBox.CursorIndex = this.filterTextBox.Text.Length;
                        this.filterTextBox.SelectionAnchorIndex = this.filterTextBox.Text.Length;
                        CalculateLayout(rebuildList: true);
                    }
                }
                this.pendingKeyboardTask = null;
            }
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
                this.contentWidth = Math.Max(1, this.phoneFrameWidth - (this.phoneContentOffsetX * 2));
                this.contentHeight = Math.Max(1, this.phoneFrameHeight - this.phoneContentOffsetY - ScaleValue(80));

                this.xPositionOnScreen = -this.phoneContentOffsetX;
                this.yPositionOnScreen = -this.phoneContentOffsetY;

                CalculateLayout(rebuildList: true);

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

                if (this.currentState == ScreenState.ProfileEditor)
                {
                    DrawProfileEditor(b);
                }
                else if (this.currentState == ScreenState.AvatarPicker)
                {
                    DrawAvatarPicker(b);
                }
                else if (this.currentState == ScreenState.ThemeList)
                {
                    DrawThemeList(b);
                }
                else if (this.currentState == ScreenState.ThemeDetail)
                {
                    DrawThemeDetail(b);
                }
                else if (this.currentState == ScreenState.NpcDetailText)
                {
                    DrawNpcDetailText(b);
                }
                else if (this.currentState == ScreenState.ThemeHelpText)
                {
                    DrawThemeHelpText(b);
                }
                else
                {
                    DrawSearchArea(b);

                    Rectangle searchBox = GetSearchBoxBounds();
                    int searchBoxAreaHeight = searchBox.Height + ScaleValue(25);
                    Rectangle listClipRect = new Rectangle(
                        contentRect.X,
                        contentRect.Y + ScaleValue(5),
                        contentRect.Width,
                        contentRect.Height - searchBoxAreaHeight);

                    b.End();
                    b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
                    Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
                    Game1.graphics.GraphicsDevice.ScissorRectangle = listClipRect;

                    DrawScrollableContent(b, contentRect);

                    b.End();
                    Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
                    b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
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
                CalculateLayout(rebuildList: true);
            }
        }
    }
}
