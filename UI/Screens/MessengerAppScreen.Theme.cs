using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using StardewModdingAPI;

namespace SmartphoneAppMessenger
{
    public partial class MessengerAppScreen
    {
        // ========================================================
        // THEME BROWSER IMPLEMENTATION
        // ========================================================

        private void LoadAvailableThemes()
        {
            this.availableThemes.Clear();
            string npcProfilePath = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "npc_profile");
            if (Directory.Exists(npcProfilePath))
            {
                var dirs = Directory.GetDirectories(npcProfilePath);
                foreach (var dir in dirs)
                {
                    string name = Path.GetFileName(dir);
                    if (!string.IsNullOrEmpty(name))
                    {
                        this.availableThemes.Add(name);
                    }
                }
            }
            if (this.availableThemes.Count == 0)
            {
                this.availableThemes.Add("vanilla");
            }
        }

        private void LoadThemeData(string theme)
        {
            try
            {
                // Load description.json
                var descData = ModEntry.Instance.Helper.ModContent.Load<Dictionary<string, string>>($"npc_profile/{theme}/description.json");
                if (descData != null)
                {
                    if (descData.TryGetValue("description", out string? desc) && !string.IsNullOrWhiteSpace(desc))
                    {
                        this.themeOverviewText = desc;
                    }
                    else if (descData.TryGetValue("text", out string? txt) && !string.IsNullOrWhiteSpace(txt))
                    {
                        this.themeOverviewText = txt;
                    }
                    else
                    {
                        this.themeOverviewText = ModEntry.GetTranslation("theme.no-description");
                    }
                }
                else
                {
                    this.themeOverviewText = ModEntry.GetTranslation("theme.no-description");
                }
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Error loading description.json for theme {theme}: {ex.Message}", LogLevel.Error);
                this.themeOverviewText = ModEntry.GetTranslation("theme.error-description");
            }

            try
            {
                // Load npc_characteristics_long.json
                this.themeCharacteristicsLong = ModEntry.Instance.Helper.ModContent.Load<Dictionary<string, string>>($"npc_profile/{theme}/npc_characteristics_long.json")
                    ?? new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log($"Error loading npc_characteristics_long.json for theme {theme}: {ex.Message}", LogLevel.Error);
                this.themeCharacteristicsLong = new Dictionary<string, string>();
            }
        }

        private void OpenThemeList()
        {
            this.currentState = ScreenState.ThemeList;
            this.scrollOffset = 0;
            this.maxScroll = 0;
            LoadAvailableThemes();
            CalculateThemeLayout();
        }

        private void OpenThemeDetail(string theme)
        {
            this.selectedTheme = theme;
            this.currentState = ScreenState.ThemeDetail;
            this.scrollOffset = 0;
            this.maxScroll = 0;
            this.activeTab = 0; // Overview tab default
            LoadThemeData(theme);
            CalculateTabsBounds();
            CalculateThemeLayout();
        }

        private void OpenNpcDetailText(string npcName, string detailText)
        {
            this.selectedNpcName = npcName;
            this.selectedNpcDetailText = detailText;
            this.currentState = ScreenState.NpcDetailText;
            this.scrollOffset = 0;
            this.maxScroll = 0;
            CalculateThemeLayout();
        }

        private void CalculateTabsBounds()
        {
            Rectangle contentRect = GetContentBounds();
            int tabWidth = (contentRect.Width - ScaleValue(50)) / 2;
            int tabHeight = ScaleValue(45);
            int tabY = contentRect.Y + ScaleValue(20);

            this.overviewTabBounds = new Rectangle(
                contentRect.X + ScaleValue(20),
                tabY,
                tabWidth,
                tabHeight);

            this.npcDetailTabBounds = new Rectangle(
                contentRect.X + ScaleValue(20) + tabWidth + ScaleValue(10),
                tabY,
                tabWidth,
                tabHeight);
        }

        private void CalculateThemeLayout()
        {
            Rectangle contentRect = GetContentBounds();
            this.scrollOffset = Math.Clamp(this.scrollOffset, 0, this.maxScroll);

            if (this.currentState == ScreenState.ThemeList)
            {
                this.themeItemBounds.Clear();
                int itemHeight = ScaleValue(60);
                int currentY = ScaleValue(80); // Title leaves space at top
                int gap = ScaleValue(15);

                foreach (var theme in this.availableThemes)
                {
                    this.themeItemBounds[theme] = new Rectangle(
                        ScaleValue(20),
                        currentY,
                        contentRect.Width - ScaleValue(40),
                        itemHeight);
                    currentY += itemHeight + gap;
                }

                int totalHeight = currentY + ScaleValue(15);
                int clipHeight = contentRect.Height - ScaleValue(80);
                this.maxScroll = Math.Max(0, totalHeight - clipHeight);
            }
            else if (this.currentState == ScreenState.ThemeDetail)
            {
                if (this.activeTab == 0) // Overview
                {
                    string textToDraw = this.themeOverviewText;
                    SpriteFont font = Game1.smallFont;
                    int textWidth = (int)((contentRect.Width - ScaleValue(40)) / this.phoneUiScale);
                    string wrapped = Game1.parseText(textToDraw, font, textWidth);
                    Vector2 size = font.MeasureString(wrapped);

                    int totalHeight = (int)(size.Y * this.phoneUiScale) + ScaleValue(90);
                    int clipHeight = contentRect.Height - ScaleValue(90);
                    this.maxScroll = Math.Max(0, totalHeight - clipHeight);
                }
                else // NPC Detail
                {
                    this.themeNpcItemBounds.Clear();
                    int itemHeight = ScaleValue(50);
                    int currentY = ScaleValue(80); // starts below the tabs
                    int gap = ScaleValue(10);

                    foreach (var npcName in this.themeCharacteristicsLong.Keys)
                    {
                        this.themeNpcItemBounds[npcName] = new Rectangle(
                            ScaleValue(20),
                            currentY,
                            contentRect.Width - ScaleValue(40),
                            itemHeight);
                        currentY += itemHeight + gap;
                    }

                    int totalHeight = currentY + ScaleValue(15);
                    int clipHeight = contentRect.Height - ScaleValue(90);
                    this.maxScroll = Math.Max(0, totalHeight - clipHeight);
                }
            }
            else if (this.currentState == ScreenState.NpcDetailText)
            {
                string textToDraw = this.selectedNpcDetailText;
                SpriteFont font = Game1.smallFont;
                int textWidth = (int)((contentRect.Width - ScaleValue(40)) / this.phoneUiScale);
                string wrapped = Game1.parseText(textToDraw, font, textWidth);
                Vector2 size = font.MeasureString(wrapped);

                int totalHeight = (int)(size.Y * this.phoneUiScale) + ScaleValue(80);
                int clipHeight = contentRect.Height - ScaleValue(70);
                this.maxScroll = Math.Max(0, totalHeight - clipHeight);
            }
            else if (this.currentState == ScreenState.ThemeHelpText)
            {
                string textToDraw = ModEntry.GetTranslation("theme.help-content");
                SpriteFont font = Game1.smallFont;
                int textWidth = (int)((contentRect.Width - ScaleValue(40)) / this.phoneUiScale);
                string wrapped = Game1.parseText(textToDraw, font, textWidth);
                Vector2 size = font.MeasureString(wrapped);

                int totalHeight = (int)(size.Y * this.phoneUiScale) + ScaleValue(80);
                int clipHeight = contentRect.Height - ScaleValue(70);
                this.maxScroll = Math.Max(0, totalHeight - clipHeight);
            }
        }

        private void DrawThemeList(SpriteBatch b)
        {
            Rectangle contentRect = GetContentBounds();

            // Header title
            string title = ModEntry.GetTranslation("theme.select-theme");
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title) * this.phoneUiScale * 0.85f;
            Vector2 titlePos = new Vector2(
                contentRect.X + (contentRect.Width - titleSize.X) / 2f,
                contentRect.Y + ScaleValue(15)
            );
            b.DrawString(Game1.dialogueFont, title, titlePos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale * 0.85f, SpriteEffects.None, 1f);

            // Subtitle reminder
            string subtitle = ModEntry.GetTranslation("theme.change-config-note");
            Vector2 subtitleSize = Game1.smallFont.MeasureString(subtitle) * this.phoneUiScale * 0.75f;
            Vector2 subtitlePos = new Vector2(
                contentRect.X + (contentRect.Width - subtitleSize.X) / 2f,
                contentRect.Y + ScaleValue(10) + titleSize.Y
            );
            b.DrawString(Game1.smallFont, subtitle, subtitlePos, Color.DimGray, 0f, Vector2.Zero, this.phoneUiScale * 0.75f, SpriteEffects.None, 1f);

            // Draw Help Button on the right side of the title
            Rectangle helpRect = GetHelpButtonBounds();
            bool isHelpHovered = helpRect.Contains(Game1.getMouseX(), Game1.getMouseY());
            UI.CardDrawing.DrawCard(b, helpRect.X, helpRect.Y, helpRect.Width, helpRect.Height, isHelpHovered ? Color.Wheat : Color.LightGray, 1f, false);
            string questionMark = "?";
            Vector2 qSize = Game1.smallFont.MeasureString(questionMark) * this.phoneUiScale;
            Vector2 qPos = new Vector2(
                helpRect.X + (helpRect.Width - qSize.X) / 2f,
                helpRect.Y + (helpRect.Height - qSize.Y) / 2f
            );
            b.DrawString(Game1.smallFont, questionMark, qPos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            // Clip area
            Rectangle listClipRect = new Rectangle(
                contentRect.X,
                contentRect.Y + ScaleValue(70),
                contentRect.Width,
                contentRect.Height - ScaleValue(80)
            );

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
            Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
            Game1.graphics.GraphicsDevice.ScissorRectangle = listClipRect;

            foreach (var kvp in this.themeItemBounds)
            {
                string themeName = kvp.Key;
                Rectangle localBounds = kvp.Value;

                Rectangle actualBounds = new Rectangle(
                    contentRect.X + localBounds.X,
                    contentRect.Y - this.scrollOffset + localBounds.Y,
                    localBounds.Width,
                    localBounds.Height
                );

                if (actualBounds.Bottom < listClipRect.Top || actualBounds.Top > listClipRect.Bottom)
                    continue;

                bool isHovered = actualBounds.Contains(Game1.getMouseX(), Game1.getMouseY()) && listClipRect.Contains(Game1.getMouseX(), Game1.getMouseY());
                Color cardColor = isHovered ? Color.Wheat : Color.LightGray;

                UI.CardDrawing.DrawCard(b, actualBounds.X, actualBounds.Y, actualBounds.Width, actualBounds.Height, cardColor, 1f, false);

                // Highlight if active theme in config
                bool isActive = string.Equals(ModEntry.Config.NpcProfileTheme, themeName, StringComparison.OrdinalIgnoreCase);
                if (isActive)
                {
                    themeName += ModEntry.GetTranslation("theme.active-suffix");
                }

                Vector2 textSize = Game1.dialogueFont.MeasureString(themeName) * this.phoneUiScale * 0.7f;
                Vector2 textPos = new Vector2(
                    actualBounds.X + (actualBounds.Width - textSize.X) / 2f,
                    actualBounds.Y + (actualBounds.Height - textSize.Y) / 2f
                );

                b.DrawString(Game1.dialogueFont, themeName, textPos, isActive ? Color.DarkGreen : Color.Black, 0f, Vector2.Zero, this.phoneUiScale * 0.7f, SpriteEffects.None, 1f);
            }

            b.End();
            Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        private void DrawThemeDetail(SpriteBatch b)
        {
            Rectangle contentRect = GetContentBounds();

            // Draw Overview tab
            Color overviewColor = (this.activeTab == 0) ? Color.Wheat : Color.LightGray;
            UI.CardDrawing.DrawCard(b, this.overviewTabBounds.X, this.overviewTabBounds.Y, this.overviewTabBounds.Width, this.overviewTabBounds.Height, overviewColor, 1f, false);
            string tab0Text = ModEntry.GetTranslation("theme.tab.overview");
            Vector2 tab0Size = Game1.smallFont.MeasureString(tab0Text) * this.phoneUiScale;
            Vector2 tab0Pos = new Vector2(
                this.overviewTabBounds.X + (this.overviewTabBounds.Width - tab0Size.X) / 2f,
                this.overviewTabBounds.Y + (this.overviewTabBounds.Height - tab0Size.Y) / 2f
            );
            b.DrawString(Game1.smallFont, tab0Text, tab0Pos, (this.activeTab == 0) ? Color.Black : Color.DarkGray, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            // Draw NPC Detail tab
            Color npcDetailColor = (this.activeTab == 1) ? Color.Wheat : Color.LightGray;
            UI.CardDrawing.DrawCard(b, this.npcDetailTabBounds.X, this.npcDetailTabBounds.Y, this.npcDetailTabBounds.Width, this.npcDetailTabBounds.Height, npcDetailColor, 1f, false);
            string tab1Text = ModEntry.GetTranslation("theme.tab.supported-npc");
            Vector2 tab1Size = Game1.smallFont.MeasureString(tab1Text) * this.phoneUiScale;
            Vector2 tab1Pos = new Vector2(
                this.npcDetailTabBounds.X + (this.npcDetailTabBounds.Width - tab1Size.X) / 2f,
                this.npcDetailTabBounds.Y + (this.npcDetailTabBounds.Height - tab1Size.Y) / 2f
            );
            b.DrawString(Game1.smallFont, tab1Text, tab1Pos, (this.activeTab == 1) ? Color.Black : Color.DarkGray, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            // Clip rect for active tab content
            Rectangle clipRect = new Rectangle(
                contentRect.X,
                contentRect.Y + ScaleValue(80),
                contentRect.Width,
                contentRect.Height - ScaleValue(90)
            );

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
            Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
            Game1.graphics.GraphicsDevice.ScissorRectangle = clipRect;

            if (this.activeTab == 0)
            {
                // Overview Text
                int textWidth = (int)((contentRect.Width - ScaleValue(40)) / this.phoneUiScale);
                string wrapped = Game1.parseText(this.themeOverviewText, Game1.smallFont, textWidth);
                Vector2 textPos = new Vector2(
                    contentRect.X + ScaleValue(20),
                    contentRect.Y + ScaleValue(80) - this.scrollOffset
                );
                b.DrawString(Game1.smallFont, wrapped, textPos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
            }
            else
            {
                // NPC list of buttons
                foreach (var kvp in this.themeNpcItemBounds)
                {
                    string npcName = kvp.Key;
                    Rectangle localBounds = kvp.Value;

                    Rectangle actualBounds = new Rectangle(
                        contentRect.X + localBounds.X,
                        contentRect.Y - this.scrollOffset + localBounds.Y,
                        localBounds.Width,
                        localBounds.Height
                    );

                    if (actualBounds.Bottom < clipRect.Top || actualBounds.Top > clipRect.Bottom)
                        continue;

                    bool isHovered = actualBounds.Contains(Game1.getMouseX(), Game1.getMouseY()) && clipRect.Contains(Game1.getMouseX(), Game1.getMouseY());
                    Color cardColor = isHovered ? Color.Wheat : Color.LightGray;

                    UI.CardDrawing.DrawCard(b, actualBounds.X, actualBounds.Y, actualBounds.Width, actualBounds.Height, cardColor, 1f, false);

                    // Draw NPC Portrait
                    NPC? npc = Game1.getCharacterFromName(npcName);
                    int portraitSize = ScaleValue(36);
                    Rectangle portraitDest = new Rectangle(
                        actualBounds.X + ScaleValue(10),
                        actualBounds.Y + (actualBounds.Height - portraitSize) / 2,
                        portraitSize,
                        portraitSize
                    );

                    if (npc != null && npc.Portrait != null)
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
                        b.Draw(Game1.staminaRect, portraitDest, Color.Gray);
                    }

                    // NPC Display name
                    string displayName = npc?.displayName ?? npcName;
                    Vector2 nameSize = Game1.dialogueFont.MeasureString(displayName) * this.phoneUiScale * 0.65f;
                    Vector2 namePos = new Vector2(
                        portraitDest.Right + ScaleValue(15),
                        actualBounds.Y + (actualBounds.Height - nameSize.Y) / 2
                    );
                    b.DrawString(Game1.dialogueFont, displayName, namePos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale * 0.65f, SpriteEffects.None, 1f);
                }
            }

            b.End();
            Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        private void DrawNpcDetailText(SpriteBatch b)
        {
            Rectangle contentRect = GetContentBounds();

            // Header title
            string title = ModEntry.GetTranslation("theme.npc-profile-title", new { npc = this.selectedNpcName });
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title) * this.phoneUiScale * 0.8f;
            Vector2 titlePos = new Vector2(
                contentRect.X + (contentRect.Width - titleSize.X) / 2f,
                contentRect.Y + ScaleValue(15)
            );
            b.DrawString(Game1.dialogueFont, title, titlePos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale * 0.8f, SpriteEffects.None, 1f);

            // Clip area
            Rectangle clipRect = new Rectangle(
                contentRect.X,
                contentRect.Y + ScaleValue(60),
                contentRect.Width,
                contentRect.Height - ScaleValue(70)
            );

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
            Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
            Game1.graphics.GraphicsDevice.ScissorRectangle = clipRect;

            int textWidth = (int)((contentRect.Width - ScaleValue(40)) / this.phoneUiScale);
            string wrapped = Game1.parseText(this.selectedNpcDetailText, Game1.smallFont, textWidth);
            Vector2 textPos = new Vector2(
                contentRect.X + ScaleValue(20),
                contentRect.Y + ScaleValue(60) - this.scrollOffset
            );
            b.DrawString(Game1.smallFont, wrapped, textPos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            b.End();
            Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

        private void HandleThemeListClick(int x, int y)
        {
            Rectangle contentRect = GetContentBounds();
            Rectangle listClipRect = new Rectangle(
                contentRect.X,
                contentRect.Y + ScaleValue(70),
                contentRect.Width,
                contentRect.Height - ScaleValue(80)
            );

            Rectangle helpRect = GetHelpButtonBounds();
            if (helpRect.Contains(x, y))
            {
                Game1.playSound("smallSelect");
                OpenThemeHelpText();
                return;
            }

            if (listClipRect.Contains(x, y))
            {
                foreach (var kvp in this.themeItemBounds)
                {
                    string themeName = kvp.Key;
                    Rectangle localBounds = kvp.Value;

                    Rectangle actualBounds = new Rectangle(
                        contentRect.X + localBounds.X,
                        contentRect.Y - this.scrollOffset + localBounds.Y,
                        localBounds.Width,
                        localBounds.Height
                    );

                    if (actualBounds.Contains(x, y))
                    {
                        Game1.playSound("smallSelect");
                        OpenThemeDetail(themeName);
                        return;
                    }
                }
            }
        }

        private void HandleThemeDetailClick(int x, int y)
        {
            Rectangle contentRect = GetContentBounds();

            // Check tab clicks
            if (this.overviewTabBounds.Contains(x, y))
            {
                this.activeTab = 0;
                this.scrollOffset = 0;
                this.maxScroll = 0;
                CalculateThemeLayout();
                Game1.playSound("smallSelect");
                return;
            }
            if (this.npcDetailTabBounds.Contains(x, y))
            {
                this.activeTab = 1;
                this.scrollOffset = 0;
                this.maxScroll = 0;
                CalculateThemeLayout();
                Game1.playSound("smallSelect");
                return;
            }

            if (this.activeTab == 1) // NPC Detail tab
            {
                Rectangle clipRect = new Rectangle(
                    contentRect.X,
                    contentRect.Y + ScaleValue(80),
                    contentRect.Width,
                    contentRect.Height - ScaleValue(90)
                );

                if (clipRect.Contains(x, y))
                {
                    foreach (var kvp in this.themeNpcItemBounds)
                    {
                        string npcName = kvp.Key;
                        Rectangle localBounds = kvp.Value;

                        Rectangle actualBounds = new Rectangle(
                            contentRect.X + localBounds.X,
                            contentRect.Y - this.scrollOffset + localBounds.Y,
                            localBounds.Width,
                            localBounds.Height
                        );

                        if (actualBounds.Contains(x, y))
                        {
                            if (this.themeCharacteristicsLong.TryGetValue(npcName, out string? detailText))
                            {
                                Game1.playSound("smallSelect");
                                OpenNpcDetailText(npcName, detailText);
                            }
                            return;
                        }
                    }
                }
            }
        }

        private void HandleNpcDetailTextClick(int x, int y)
        {
            // Scrolling supported automatically via default update/dragging logic
        }

        private void HandleThemeHelpTextClick(int x, int y)
        {
            // Scrolling supported automatically via default update/dragging logic
        }

        private void OpenThemeHelpText()
        {
            this.currentState = ScreenState.ThemeHelpText;
            this.scrollOffset = 0;
            this.maxScroll = 0;
            CalculateThemeLayout();
        }

        private Rectangle GetHelpButtonBounds()
        {
            Rectangle contentRect = GetContentBounds();
            string title = ModEntry.GetTranslation("theme.select-theme");
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title) * this.phoneUiScale * 0.85f;
            int titleX = contentRect.X + (int)((contentRect.Width - titleSize.X) / 2f);
            int size = ScaleValue(30);
            return new Rectangle(
                titleX + (int)titleSize.X + ScaleValue(15),
                contentRect.Y + ScaleValue(15) + (int)((titleSize.Y - size) / 2f),
                size,
                size);
        }

        private void DrawThemeHelpText(SpriteBatch b)
        {
            Rectangle contentRect = GetContentBounds();

            // Header title
            string title = ModEntry.GetTranslation("theme.help-title");
            Vector2 titleSize = Game1.dialogueFont.MeasureString(title) * this.phoneUiScale * 0.8f;
            Vector2 titlePos = new Vector2(
                contentRect.X + (contentRect.Width - titleSize.X) / 2f,
                contentRect.Y + ScaleValue(15)
            );
            b.DrawString(Game1.dialogueFont, title, titlePos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale * 0.8f, SpriteEffects.None, 1f);

            // Clip area
            Rectangle clipRect = new Rectangle(
                contentRect.X,
                contentRect.Y + ScaleValue(60),
                contentRect.Width,
                contentRect.Height - ScaleValue(70)
            );

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, new RasterizerState() { ScissorTestEnable = true });
            Rectangle previousScissor = Game1.graphics.GraphicsDevice.ScissorRectangle;
            Game1.graphics.GraphicsDevice.ScissorRectangle = clipRect;

            int textWidth = (int)((contentRect.Width - ScaleValue(40)) / this.phoneUiScale);
            string wrapped = Game1.parseText(ModEntry.GetTranslation("theme.help-content"), Game1.smallFont, textWidth);
            Vector2 textPos = new Vector2(
                contentRect.X + ScaleValue(20),
                contentRect.Y + ScaleValue(60) - this.scrollOffset
            );
            b.DrawString(Game1.smallFont, wrapped, textPos, Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            b.End();
            Game1.graphics.GraphicsDevice.ScissorRectangle = previousScissor;
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        }

    }
}
