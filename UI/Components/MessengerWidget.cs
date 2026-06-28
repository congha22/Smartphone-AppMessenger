using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace SmartphoneAppMessenger
{
    public static class MessengerWidget
    {
        public static void Draw(SpriteBatch b, Rectangle rect, AppSize size, Texture2D appIcon, Texture2D? appBackgroundTexture, ISmartPhoneApi api, string compositeId)
        {
            // Default 1x1 size: draw normal icon
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

            // Draw widget background (Messenger style)
            if (appBackgroundTexture != null && !appBackgroundTexture.IsDisposed)
            {
                b.Draw(appBackgroundTexture, rect, Color.White);
            }
            else
            {
                // Fallback elegant dark blue/teal slate color matching Messenger style
                b.Draw(Game1.staminaRect, rect, new Color(30, 45, 70));
            }

            // Draw a subtle border frame to match the phone theme
            UI.CardDrawing.DrawCard(
                b,
                rect.X, rect.Y, rect.Width, rect.Height,
                Color.White * 0.5f, 0.5f, false);

            // Find the most recently active contact
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

            var latestContact = allContacts
                .Where(name => !string.Equals(name, Game1.player.Name, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(name => MessageManager.LatestMessageTimestamps.TryGetValue(name, out int ts) ? ts : 0)
                .FirstOrDefault();

            var messages = latestContact != null ? MessageManager.GetMessagesForNpc(latestContact) : null;
            var latestMessage = messages?.LastOrDefault();

            if (latestContact == null || latestMessage == null)
            {
                // Draw no messages text beautifully centered
                string text = "No messages yet.";
                Vector2 textSize = Game1.smallFont.MeasureString(text) * 0.75f;
                b.DrawString(Game1.smallFont, text, new Vector2(rect.X + (rect.Width - textSize.X) / 2f, rect.Y + (rect.Height - textSize.Y) / 2f), Color.White * 0.8f, 0f, Vector2.Zero, 0.75f, SpriteEffects.None, 1f);
                return;
            }

            bool isPlayer = Game1.getOnlineFarmers().Any(f => string.Equals(f.Name, latestContact, StringComparison.OrdinalIgnoreCase))
                            || MessageManager.PlayerConversations.ContainsKey(latestContact);

            NPC? npc = isPlayer ? null : Game1.getCharacterFromName(latestContact);
            string displayName = npc?.displayName ?? latestContact;

            // Draw widget content depending on size
            if (size == AppSize.Size2x1)
            {
                int iconPadding = 6;
                int actorIconSize = 28;
                Rectangle actorBounds = new Rectangle(rect.X + iconPadding, rect.Y + (rect.Height - actorIconSize) / 2, actorIconSize, actorIconSize);
                DrawWidgetActorIcon(b, latestContact, isPlayer, npc, actorBounds);

                // Author name
                Vector2 nameSize = Game1.smallFont.MeasureString(displayName) * 0.7f;
                b.DrawString(Game1.smallFont, displayName, new Vector2(actorBounds.Right + 6, rect.Y + 4), Color.White, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 1f);

                // Draw snippet of latest message text
                string snippet = CleanPrefixes(latestMessage, latestContact);
                if (!string.IsNullOrWhiteSpace(snippet))
                {
                    if (snippet.Length > 20) snippet = snippet.Substring(0, 18) + "...";
                    b.DrawString(Game1.smallFont, snippet, new Vector2(actorBounds.Right + 6, rect.Y + 4 + nameSize.Y + 2), Color.LightGray * 0.9f, 0f, Vector2.Zero, 0.65f, SpriteEffects.None, 1f);
                }
            }
            else
            {
                // For 2x2 or larger sizes
                int pad = 8;
                int actorIconSize = 32;
                Rectangle actorBounds = new Rectangle(rect.X + pad, rect.Y + pad, actorIconSize, actorIconSize);
                DrawWidgetActorIcon(b, latestContact, isPlayer, npc, actorBounds);

                // Draw unread counts on the top-right of portrait if there are unread messages
                if (MessageManager.UnreadCounts.TryGetValue(latestContact, out int unreadCount) && unreadCount > 0)
                {
                    string numberStr = Math.Min(unreadCount, 9).ToString();
                    Rectangle badgeBounds = new Rectangle(actorBounds.Right - 8, actorBounds.Y - 6, 14, 14);
                    b.Draw(Game1.staminaRect, badgeBounds, new Color(215, 48, 48, 235));

                    Vector2 numSize = Game1.smallFont.MeasureString(numberStr) * 0.5f;
                    Vector2 numPos = new Vector2(
                        badgeBounds.X + (badgeBounds.Width - numSize.X) / 2f,
                        badgeBounds.Y + (badgeBounds.Height - numSize.Y) / 2f);
                    b.DrawString(Game1.smallFont, numberStr, numPos, Color.White, 0f, Vector2.Zero, 0.5f, SpriteEffects.None, 1f);
                }

                // Author name
                b.DrawString(Game1.smallFont, displayName, new Vector2(actorBounds.Right + 6, rect.Y + pad + 2), Color.White, 0f, Vector2.Zero, 0.75f, SpriteEffects.None, 1f);

                // Draw message text snippet wrapped
                string snippet = CleanPrefixes(latestMessage, latestContact);
                int wrapWidth = (int)((rect.Width - pad * 2) / 0.65f);
                List<string> wrappedLines = SplitTextIntoLines(snippet, Game1.smallFont, wrapWidth);
                int yCursor = actorBounds.Bottom + 6;
                int maxLines = (rect.Height - yCursor - 8) / 14;
                for (int i = 0; i < wrappedLines.Count && i < maxLines; i++)
                {
                    b.DrawString(Game1.smallFont, wrappedLines[i], new Vector2(rect.X + pad, yCursor), Color.LightGray, 0f, Vector2.Zero, 0.65f, SpriteEffects.None, 1f);
                    yCursor += 14;
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

        private static void DrawWidgetActorIcon(SpriteBatch b, string name, bool isPlayer, NPC? npc, Rectangle bounds)
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

            Vector2 letterSize = Game1.smallFont.MeasureString(fallbackLetter) * 0.75f;
            Vector2 letterPos = new Vector2(
                bounds.X + (bounds.Width - letterSize.X) / 2f,
                bounds.Y + (bounds.Height - letterSize.Y) / 2f);
            b.DrawString(Game1.smallFont, fallbackLetter, letterPos, Color.White, 0f, Vector2.Zero, 0.75f, SpriteEffects.None, 1f);
        }

        private static List<string> SplitTextIntoLines(string text, SpriteFont font, int maxWidth)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return lines;
            }

            string[] paragraphs = text.Split('\n');
            foreach (var paragraph in paragraphs)
            {
                string[] words = paragraph.Split(' ');
                string currentLine = "";

                foreach (var word in words)
                {
                    string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                    float testWidth = font.MeasureString(testLine).X;

                    if (testWidth <= maxWidth)
                    {
                        currentLine = testLine;
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(currentLine))
                            lines.Add(currentLine);
                        currentLine = word;
                    }
                }

                if (!string.IsNullOrEmpty(currentLine) || words.Length == 0)
                    lines.Add(currentLine);
            }

            return lines;
        }
    }
}
