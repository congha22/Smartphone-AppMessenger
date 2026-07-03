using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Smartphone;
using StardewValley;
using StardewValley.Menus;

namespace SmartphoneAppMessenger
{
    public static class MessengerWidget
    {
        private static Texture2D? widgetTexture2x1;
        private static Texture2D? widgetTexture2x2;
        private static string? lastIconStyle;

        public static void Draw(SpriteBatch b, Rectangle rect, AppSize size, Texture2D appIcon, Texture2D? appBackgroundTexture, ISmartPhoneApi api, string compositeId)
        {
            // --- 1x1 Size: Works as it is (just an icon) ---
            if (size == AppSize.Size1x1 || appIcon == null)
            {
                Texture2D? currentIcon = null;
                if (api != null)
                {
                    try
                    {
                        currentIcon = api.GetAppIconTexture(compositeId);
                    }
                    catch { }
                }
                if (currentIcon == null)
                {
                    currentIcon = appIcon;
                }

                if (currentIcon != null)
                {
                    b.Draw(currentIcon, rect, Color.White);
                }
                return;
            }

            // --- Optimized Dynamic Asset Caching Mechanism ---
            string currentStyle = "default";
            if (api != null)
            {
                try
                {
                    Texture2D? currentIcon = api.GetAppIconTexture(compositeId);
                    if (currentIcon != null && currentIcon.Name != null && currentIcon.Name.Contains("v2", StringComparison.OrdinalIgnoreCase))
                    {
                        currentStyle = "v2";
                    }
                }
                catch { }
            }

            if (lastIconStyle != currentStyle)
            {
                lastIconStyle = currentStyle;
                try { widgetTexture2x1?.Dispose(); widgetTexture2x1 = null; } catch { }
                try { widgetTexture2x2?.Dispose(); widgetTexture2x2 = null; } catch { }

                try
                {
                    string path2x1 = $"assets/{currentStyle}/2x1.png";
                    widgetTexture2x1 = ModEntry.Instance.Helper.ModContent.Load<Texture2D>(path2x1);
                }
                catch { }

                try
                {
                    string path2x2 = $"assets/{currentStyle}/2x2.png";
                    widgetTexture2x2 = ModEntry.Instance.Helper.ModContent.Load<Texture2D>(path2x2);
                }
                catch { }
            }

            // Determine appropriate cached background texture to draw
            Texture2D? bgTex = null;
            if (size == AppSize.Size2x1 && widgetTexture2x1 != null && !widgetTexture2x1.IsDisposed)
                bgTex = widgetTexture2x1;
            else if (size == AppSize.Size2x2 && widgetTexture2x2 != null && !widgetTexture2x2.IsDisposed)
                bgTex = widgetTexture2x2;
            else if (appBackgroundTexture != null && !appBackgroundTexture.IsDisposed)
                bgTex = appBackgroundTexture;

            if (bgTex != null)
            {
                b.Draw(bgTex, rect, Color.White);
            }
            else
            {
                // Fallback clean light-gray theme canvas
                b.Draw(Game1.staminaRect, rect, new Color(242, 242, 242));
            }

            // --- Find and rank conversations with new unread messages ---
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

            var newMcs = allContacts
                .Where(name => !string.Equals(name, Game1.player.Name, StringComparison.OrdinalIgnoreCase))
                .Where(name => MessageManager.UnreadCounts.TryGetValue(name, out int count) && count > 0)
                .OrderByDescending(name => MessageManager.LatestMessageTimestamps.TryGetValue(name, out int ts) ? ts : 0)
                .ToList();

            // Handle scenario where there are no unread messages
            float uiScale = api?.GetPhoneUiScale() ?? 1f;

            if (newMcs.Count == 0)
            {
                string noMsgText = ModEntry.GetTranslation("widget.no-new-messages");
                float noMsgScale = 0.85f * uiScale;
                Vector2 textSize = Game1.smallFont.MeasureString(noMsgText) * noMsgScale;
                b.DrawString(
                    Game1.smallFont,
                    noMsgText,
                    new Vector2(rect.X + (rect.Width - textSize.X) / 2f, rect.Y + (rect.Height - textSize.Y) / 2f),
                    Color.Black,
                    0f,
                    Vector2.Zero,
                    noMsgScale,
                    SpriteEffects.None,
                    1f
                );
                return;
            }

            int actorIconSize = (int)Math.Round(38 * uiScale); // Upgraded portrait layout footprint bounding size

            // --- Draw layout rows based on active widget size ---
            if (size == AppSize.Size2x1)
            {
                int yPos = rect.Y + (rect.Height - actorIconSize) / 2;
                DrawContactRow(b, rect, size, newMcs[0], yPos, actorIconSize, uiScale);
            }
            else
            {
                // Size 2x2: Displays up to 2 items
                if (newMcs.Count == 1)
                {
                    // Perfectly centered vertically if only 1 NPC has new incoming messages (shifted up slightly for balance)
                    int yPos = rect.Y + (rect.Height - actorIconSize) / 2 - (int)Math.Round(10 * uiScale);
                    DrawContactRow(b, rect, size, newMcs[0], yPos, actorIconSize, uiScale);
                }
                else
                {
                    // Symmetrically stacked row layout with adjusted spacing
                    int halfHeight = rect.Height / 2;
                    int y1 = rect.Y + (halfHeight - actorIconSize) / 2 - (int)Math.Round(8 * uiScale);
                    int y2 = rect.Y + halfHeight + (halfHeight - actorIconSize) / 2 - (int)Math.Round(22 * uiScale);

                    DrawContactRow(b, rect, size, newMcs[0], y1, actorIconSize, uiScale);
                    DrawContactRow(b, rect, size, newMcs[1], y2, actorIconSize, uiScale);

                    // Draw a thin black line at the middle
                    int linePadding = (int)Math.Round(8 * uiScale);
                    int lineWidth = (int)Math.Round(1 * uiScale);
                    b.Draw(Game1.staminaRect, new Rectangle(rect.X + linePadding, rect.Y + halfHeight, rect.Width - (linePadding * 2), lineWidth), Color.Black);
                }
            }
        }

        private static void DrawContactRow(SpriteBatch b, Rectangle rect, AppSize size, string contactName, int yPos, int actorIconSize, float uiScale)
        {
            bool isPlayer = Game1.getOnlineFarmers().Any(f => string.Equals(f.Name, contactName, StringComparison.OrdinalIgnoreCase))
                            || MessageManager.PlayerConversations.ContainsKey(contactName);

            NPC? npc = isPlayer ? null : Game1.getCharacterFromName(contactName);
            string displayName = npc?.displayName ?? contactName;

            int iconPadding = (int)Math.Round(8 * uiScale);
            Rectangle actorBounds = new Rectangle(rect.X + iconPadding, yPos, actorIconSize, actorIconSize);
            DrawWidgetActorIcon(b, contactName, isPlayer, npc, actorBounds, uiScale);

            // --- Number badge removed completely as requested ---

            float nameScale = 0.85f * uiScale;
            float textScale = 0.7f * uiScale;
            Vector2 nameSize = Game1.smallFont.MeasureString(displayName) * nameScale;
            int textX = actorBounds.Right + (int)Math.Round(8 * uiScale);
            int nameY = (size == AppSize.Size2x1) ? (rect.Y + (int)Math.Round(6 * uiScale)) : actorBounds.Y;

            // Draw Display Name in solid crisp Black text color
            b.DrawString(Game1.smallFont, displayName, new Vector2(textX, nameY), Color.Black, 0f, Vector2.Zero, nameScale, SpriteEffects.None, 1f);

            // Fetch text content string, clean system flags, word wrap and clip to max 2 lines
            var messages = MessageManager.GetMessagesForNpc(contactName);
            var latestMessage = messages?.LastOrDefault();
            string snippet = latestMessage != null ? CleanPrefixes(latestMessage, contactName) : "";

            if (!string.IsNullOrWhiteSpace(snippet))
            {
                int wrapWidth = (int)((rect.Right - textX - (int)Math.Round(8 * uiScale)) / textScale);
                List<string> wrappedLines = SplitTextIntoLines(snippet, Game1.smallFont, wrapWidth);

                int yCursor = nameY + (int)nameSize.Y + (int)Math.Round(2 * uiScale);
                int linesDrawn = 0;
                for (int i = 0; i < wrappedLines.Count && linesDrawn < 2; i++)
                {
                    if (yCursor + (int)Math.Round(12 * uiScale) <= rect.Bottom)
                    {
                        b.DrawString(Game1.smallFont, wrappedLines[i], new Vector2(textX, yCursor), Color.Gray, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);
                        yCursor += (int)(Game1.smallFont.MeasureString(wrappedLines[i]).Y * textScale) - (int)Math.Round(1 * uiScale);
                        linesDrawn++;
                    }
                }
            }
        }

        private static string CleanPrefixes(string message, string contactName)
        {
            if (string.IsNullOrEmpty(message)) return "";
            if (message.StartsWith("PLAYER: "))
                return message.Substring("PLAYER: ".Length);
            if (message.StartsWith("SYSTEM: "))
                return message.Substring("SYSTEM: ".Length);
            if (message.StartsWith($"{contactName}: "))
                return message.Substring(contactName.Length + 2);
            return message;
        }

        private static void DrawWidgetActorIcon(SpriteBatch b, string name, bool isPlayer, NPC? npc, Rectangle bounds, float uiScale)
        {
            if (!isPlayer && npc?.Portrait != null)
            {
                b.Draw(npc.Portrait, bounds, new Rectangle(0, 0, 64, 64), Color.White);
                return;
            }
            else if (isPlayer)
            {
                string? avatarPath = MessageManager.GetPlayerAvatarPath(name);
                if (!string.IsNullOrEmpty(avatarPath) && MessengerAppScreen.TryGetImageTexture(avatarPath, out Texture2D avatarTexture))
                {
                    b.Draw(avatarTexture, bounds, Color.White);
                    return;
                }
            }

            b.Draw(Game1.staminaRect, bounds, new Color(65, 95, 135, 220));

            string fallbackLetter = "P";
            if (!string.IsNullOrWhiteSpace(name))
                fallbackLetter = name.Trim()[0].ToString().ToUpperInvariant();

            Vector2 letterSize = Game1.smallFont.MeasureString(fallbackLetter) * 0.75f * uiScale;
            Vector2 letterPos = new Vector2(
                bounds.X + (bounds.Width - letterSize.X) / 2f,
                bounds.Y + (bounds.Height - letterSize.Y) / 2f);
            b.DrawString(Game1.smallFont, fallbackLetter, letterPos, Color.White, 0f, Vector2.Zero, 0.75f * uiScale, SpriteEffects.None, 1f);
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

        private static List<string> SplitTextIntoLines(string text, SpriteFont font, int maxWidth)
        {
            List<string> allLines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return allLines;
            }

            string[] paragraphs = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
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
    }
}