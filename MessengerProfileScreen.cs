using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewModdingAPI;
using StardewValley.Menus;
using TextCopy;

namespace SmartphoneAppMessenger
{
    public partial class MessengerAppScreen
    {
        // ========================================================
        // PROFILE EDITOR & AVATAR PICKER IMPLEMENTATION
        // ========================================================

        private int PhoneX(int baseOffset) => this.xPositionOnScreen + ScaleValue(baseOffset);
        private int PhoneY(int baseOffset) => this.yPositionOnScreen + ScaleValue(baseOffset);
        private Rectangle PhoneRect(int baseX, int baseY, int baseWidth, int baseHeight) =>
            new Rectangle(PhoneX(baseX), PhoneY(baseY), ScaleValue(baseWidth), ScaleValue(baseHeight));

        private Rectangle GetUiViewportBounds()
        {
            return new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
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

        private static string GetActiveSaveFolderName()
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

        private bool TryGetPlayerAvatarTexture(out Texture2D texture)
        {
            texture = null!;
            string activeSave = GetActiveSaveFolderName();
            string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
            if (!Directory.Exists(photoSharedDir))
                return false;

            string id = Game1.player.UniqueMultiplayerID.ToString();
            string[] files = Directory.GetFiles(photoSharedDir, $"{id}_avatar.*");
            if (files.Length == 0)
                return false;

            string avatarPath = files[0];
            return TryGetImageTexture(avatarPath, out texture);
        }

        private bool TryGetImageTexture(string imagePath, out Texture2D texture)
        {
            texture = null!;
            if (string.IsNullOrWhiteSpace(imagePath))
                return false;

            if (this.avatarImageCache.TryGetValue(imagePath, out Texture2D? cachedTexture) && cachedTexture != null)
            {
                texture = cachedTexture;
                return true;
            }

            if (this.avatarFailedImagePaths.Contains(imagePath))
                return false;

            try
            {
                if (File.Exists(imagePath))
                {
                    using FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    Texture2D loadedTexture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                    this.avatarImageCache[imagePath] = loadedTexture;
                    texture = loadedTexture;
                    return true;
                }
                else if (this.smartphoneApi != null)
                {
                    Texture2D loadedTexture = this.smartphoneApi.GetPlayerPhotoTexture(imagePath);
                    if (loadedTexture != null)
                    {
                        this.avatarImageCache[imagePath] = loadedTexture;
                        texture = loadedTexture;
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }

            this.avatarFailedImagePaths.Add(imagePath);
            return false;
        }

        private void EnsureAvatarPhotoCandidatesLoaded()
        {
            this.avatarPhotoCandidates.Clear();
            if (this.smartphoneApi != null)
            {
                string smartphoneDir = Path.Combine(Directory.GetParent(ModEntry.Instance.Helper.DirectoryPath).FullName, "Smartphone");
                string activeSave = GetActiveSaveFolderName();
                string photoPlayerDir = Path.Combine(smartphoneDir, "userdata", activeSave, "photo_player");

                var names = this.smartphoneApi.GetPlayerPhotoNames();
                if (names != null)
                {
                    foreach (var name in names)
                    {
                        string absolutePath = Path.Combine(photoPlayerDir, name);
                        if (TryGetImageTexture(absolutePath, out Texture2D tex))
                        {
                            float ratio = (float)tex.Width / tex.Height;
                            if (ratio > 0.95f && ratio < 1.05f)
                            {
                                this.avatarPhotoCandidates.Add(absolutePath);
                            }
                        }
                    }
                }
            }

            if (this.avatarPhotoCandidates.Count == 0)
                this.avatarPhotoCandidateIndex = -1;
            else
                this.avatarPhotoCandidateIndex = Math.Clamp(this.avatarPhotoCandidateIndex, 0, this.avatarPhotoCandidates.Count - 1);
        }

        private void OpenProfileEditor()
        {
            this.currentState = ScreenState.ProfileEditor;
            this.activeProfileField = ProfileField.None;

            this.ageTextBox.Text = string.IsNullOrWhiteSpace(MessageManager.currentPlayerAge) ? "Adult" : MessageManager.currentPlayerAge;
            this.ageTextBox.CursorIndex = this.ageTextBox.Text.Length;
            this.ageTextBox.SelectionAnchorIndex = this.ageTextBox.Text.Length;

            this.birthdayTextBox.Text = string.IsNullOrWhiteSpace(MessageManager.currentPlayerBirthDate) ? "1" : MessageManager.currentPlayerBirthDate;
            this.birthdayTextBox.CursorIndex = this.birthdayTextBox.Text.Length;
            this.birthdayTextBox.SelectionAnchorIndex = this.birthdayTextBox.Text.Length;

            this.birthdaySeason = string.IsNullOrWhiteSpace(MessageManager.currentPlayerBirthSeason) ? "Spring" : MessageManager.currentPlayerBirthSeason;

            this.aboutMeTextBox.Text = MessageManager.currentPlayerProfile ?? "";
            this.aboutMeTextBox.CursorIndex = this.aboutMeTextBox.Text.Length;
            this.aboutMeTextBox.SelectionAnchorIndex = this.aboutMeTextBox.Text.Length;

            this.avatarDraft = MessageManager.currentPlayerAvatar ?? "";
        }

        private void SaveAvatarRightAway(string path)
        {
            this.avatarImageCache.Clear();
            this.avatarFailedImagePaths.Clear();

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    string activeSave = GetActiveSaveFolderName();
                    string photoSharedDir = Path.Combine(ModEntry.Instance.Helper.DirectoryPath, "userdata", activeSave, "photo_shared");
                    if (!Directory.Exists(photoSharedDir))
                    {
                        Directory.CreateDirectory(photoSharedDir);
                    }

                    string id = Game1.player.UniqueMultiplayerID.ToString();
                    foreach (var oldFile in Directory.GetFiles(photoSharedDir, $"{id}_avatar.*"))
                    {
                        try { File.Delete(oldFile); } catch { }
                    }

                    string extension = Path.GetExtension(path);
                    string destFileName = $"{id}_avatar{extension}";
                    string destPath = Path.Combine(photoSharedDir, destFileName);

                    File.Copy(path, destPath, overwrite: true);
                    MessageManager.currentPlayerAvatar = destPath;
                    this.avatarDraft = destPath;
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log($"Failed to copy avatar photo: {ex}", LogLevel.Error);
                    MessageManager.currentPlayerAvatar = path;
                    this.avatarDraft = path;
                }
            }
            else
            {
                MessageManager.currentPlayerAvatar = path ?? "";
                this.avatarDraft = path ?? "";
            }

            MessageManager.SavePlayerProfile(ModEntry.Instance.Helper);
        }

        private void SaveProfileData()
        {
            if (int.TryParse(this.birthdayTextBox.Text, out int day))
            {
                day = Math.Clamp(day, 1, 28);
                this.birthdayTextBox.Text = day.ToString();
            }
            else
            {
                this.birthdayTextBox.Text = "1";
            }

            MessageManager.currentPlayerAge = this.ageTextBox.Text;
            MessageManager.currentPlayerBirthDate = this.birthdayTextBox.Text;
            MessageManager.currentPlayerBirthSeason = this.birthdaySeason;
            MessageManager.currentPlayerProfile = this.aboutMeTextBox.Text;

            MessageManager.SavePlayerProfile(ModEntry.Instance.Helper);
        }

        private void DrawProfileEditor(SpriteBatch b)
        {
            Rectangle contentRect = GetContentBounds();

            int titleX = PhoneX(105);
            int titleY = PhoneY(65);
            b.DrawString(Game1.dialogueFont, "Profile", new Vector2(titleX, titleY), Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            Rectangle headerBounds = PhoneRect(50, 120, 500, 190);
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                headerBounds.X,
                headerBounds.Y,
                headerBounds.Width,
                headerBounds.Height,
                new Color(255, 255, 255, 230),
                1f,
                false);

            this.profileAvatarBounds = new Rectangle(
                headerBounds.X + ScaleValue(15),
                headerBounds.Y + ScaleValue(15),
                ScaleValue(150),
                ScaleValue(150));

            // Draw player avatar
            if (TryGetPlayerAvatarTexture(out Texture2D avatarTexture))
            {
                b.Draw(avatarTexture, this.profileAvatarBounds, Color.White);
            }
            else if (!string.IsNullOrWhiteSpace(this.avatarDraft) && TryGetImageTexture(this.avatarDraft, out Texture2D draftTexture))
            {
                b.Draw(draftTexture, this.profileAvatarBounds, Color.White);
            }
            else
            {
                IClickableMenu.drawTextureBox(
                    b,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    this.profileAvatarBounds.X,
                    this.profileAvatarBounds.Y,
                    this.profileAvatarBounds.Width,
                    this.profileAvatarBounds.Height,
                    new Color(235, 235, 235, 220),
                    1f,
                    false);

                string initial = string.IsNullOrWhiteSpace(Game1.player?.Name)
                    ? "Q"
                    : Game1.player.Name.Trim()[0].ToString().ToUpperInvariant();
                Vector2 initialSize = Game1.dialogueFont.MeasureString(initial) * this.phoneUiScale;
                Vector2 initialPos = new Vector2(
                    this.profileAvatarBounds.X + (this.profileAvatarBounds.Width - initialSize.X) / 2f,
                    this.profileAvatarBounds.Y + (this.profileAvatarBounds.Height - initialSize.Y) / 2f);
                b.DrawString(Game1.dialogueFont, initial, initialPos, Color.Gray, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
            }

            this.profileAvatarCameraButtonBounds = new Rectangle(
                this.profileAvatarBounds.Right - ScaleValue(48),
                this.profileAvatarBounds.Bottom - ScaleValue(48),
                ScaleValue(44),
                ScaleValue(44));
            DrawSocialProfileAvatarCameraButton(b, this.profileAvatarCameraButtonBounds);

            b.DrawString(
                Game1.dialogueFont,
                "Profile Settings",
                new Vector2(this.profileAvatarBounds.Right + ScaleValue(18), headerBounds.Y + ScaleValue(22)),
                Color.Black,
                0f,
                Vector2.Zero,
                this.phoneUiScale * 0.85f,
                SpriteEffects.None,
                1f);

            int fieldX = PhoneX(50);
            int fieldWidth = ScaleValue(500);
            int fieldHeight = Math.Max(1, ScaleValue(60));

            // Age Field
            int ageLabelY = PhoneY(315);
            b.DrawString(Game1.smallFont, "Age", new Vector2(fieldX, ageLabelY), Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
            this.profileAgeFieldBounds = new Rectangle(fieldX, PhoneY(350), fieldWidth, fieldHeight);
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                this.profileAgeFieldBounds.X,
                this.profileAgeFieldBounds.Y,
                this.profileAgeFieldBounds.Width,
                this.profileAgeFieldBounds.Height,
                new Color(255, 255, 255, 230),
                1f,
                false);
            this.ageTextBox.Draw(
                b,
                this.profileAgeFieldBounds,
                this.phoneUiScale,
                this.activeProfileField == ProfileField.Age);

            // Birthday Field
            int birthdayLabelY = PhoneY(420);
            b.DrawString(Game1.smallFont, "Birthday", new Vector2(fieldX, birthdayLabelY), Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
            int birthdayInputWidth = ScaleValue(115);
            int seasonButtonWidth = ScaleValue(150);
            this.profileBirthdayFieldBounds = new Rectangle(fieldX, PhoneY(450), birthdayInputWidth, fieldHeight);
            this.profileSeasonButtonBounds = new Rectangle(this.profileBirthdayFieldBounds.Right + ScaleValue(15), this.profileBirthdayFieldBounds.Y, seasonButtonWidth, fieldHeight);

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                this.profileBirthdayFieldBounds.X,
                this.profileBirthdayFieldBounds.Y,
                this.profileBirthdayFieldBounds.Width,
                this.profileBirthdayFieldBounds.Height,
                new Color(255, 255, 255, 230),
                1f,
                false);

            this.birthdayTextBox.Draw(
                b,
                this.profileBirthdayFieldBounds,
                this.phoneUiScale,
                this.activeProfileField == ProfileField.Birthday);

            // Season Button
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                this.profileSeasonButtonBounds.X,
                this.profileSeasonButtonBounds.Y,
                this.profileSeasonButtonBounds.Width,
                this.profileSeasonButtonBounds.Height,
                new Color(255, 255, 255, 230),
                1f,
                false);

            b.DrawString(
                Game1.smallFont,
                this.birthdaySeason,
                new Vector2(this.profileSeasonButtonBounds.X + ScaleValue(16), this.profileSeasonButtonBounds.Y + ScaleValue(16)),
                Color.Black,
                0f,
                Vector2.Zero,
                this.phoneUiScale,
                SpriteEffects.None,
                1f);

            Rectangle seasonArrowBounds = new Rectangle(
                this.profileSeasonButtonBounds.Right - ScaleValue(34),
                this.profileSeasonButtonBounds.Y + ScaleValue(10),
                ScaleValue(20),
                ScaleValue(20));
            b.Draw(
                Game1.mouseCursors,
                seasonArrowBounds,
                Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 33),
                Color.White);

            // About Me Field
            int descriptionLabelY = PhoneY(520);
            b.DrawString(Game1.smallFont, "About me", new Vector2(fieldX, descriptionLabelY), Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);
            this.profileDescriptionFieldBounds = new Rectangle(fieldX, PhoneY(555), fieldWidth, ScaleValue(285));
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                this.profileDescriptionFieldBounds.X,
                this.profileDescriptionFieldBounds.Y,
                this.profileDescriptionFieldBounds.Width,
                this.profileDescriptionFieldBounds.Height,
                new Color(255, 255, 255, 230),
                1f,
                false);

            if (string.IsNullOrEmpty(this.aboutMeTextBox.Text))
            {
                string placeholder = "Something about you.";
                int placeholderX = this.profileDescriptionFieldBounds.X + ScaleValue(15);
                int placeholderY = this.profileDescriptionFieldBounds.Y + ScaleValue(15);
                b.DrawString(
                    Game1.smallFont,
                    placeholder,
                    new Vector2(placeholderX, placeholderY),
                    Color.Gray * 0.6f,
                    0f,
                    Vector2.Zero,
                    this.phoneUiScale,
                    SpriteEffects.None,
                    1f);
            }

            this.aboutMeTextBox.Draw(
                b,
                this.profileDescriptionFieldBounds,
                this.phoneUiScale,
                this.activeProfileField == ProfileField.AboutMe);

            this.profileOkButtonBounds = new Rectangle(
                PhoneX(490),
                PhoneY(850),
                ScaleValue(64),
                ScaleValue(64));

            b.Draw(
                Game1.mouseCursors,
                this.profileOkButtonBounds,
                new Rectangle(128, 256, 64, 64),
                Color.White);
        }

        private void DrawAvatarPicker(SpriteBatch b)
        {
            Rectangle panelBounds = PhoneRect(65, 180, 470, 600);
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                panelBounds.X,
                panelBounds.Y,
                panelBounds.Width,
                panelBounds.Height,
                new Color(255, 255, 255, 240),
                1f,
                false);

            string title = "Select Avatar";
            b.DrawString(
                Game1.dialogueFont,
                title,
                new Vector2(panelBounds.X + ScaleValue(20), panelBounds.Y + ScaleValue(14)),
                Color.Black,
                0f,
                Vector2.Zero,
                this.phoneUiScale * 0.85f,
                SpriteEffects.None,
                1f);

            this.avatarPickerPrevBounds = Rectangle.Empty;
            this.avatarPickerNextBounds = Rectangle.Empty;
            this.avatarPickerToggleBounds = Rectangle.Empty;

            this.avatarPickerCancelBounds = new Rectangle(
                panelBounds.Right - ScaleValue(190),
                panelBounds.Bottom - ScaleValue(80),
                ScaleValue(96),
                ScaleValue(48));

            this.avatarPickerOkBounds = new Rectangle(
                panelBounds.Right - ScaleValue(84),
                panelBounds.Bottom - ScaleValue(86),
                ScaleValue(64),
                ScaleValue(64));

            Rectangle previewBounds = new Rectangle(
                panelBounds.X + ScaleValue(30),
                panelBounds.Y + ScaleValue(80),
                panelBounds.Width - ScaleValue(60),
                ScaleValue(330));

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                previewBounds.X,
                previewBounds.Y,
                previewBounds.Width,
                previewBounds.Height,
                new Color(255, 255, 255, 220),
                1f,
                false);

            if (this.avatarPhotoCandidates.Count == 0)
            {
                string noPhotosMsg = "Only square photo\ncan be used\nas avatar.";
                Vector2 msgSize = Game1.smallFont.MeasureString("Only square photo") * this.phoneUiScale;
                int startY = previewBounds.Y + (previewBounds.Height - (int)msgSize.Y * 3) / 2;

                string[] msgLines = noPhotosMsg.Split('\n');
                for (int i = 0; i < msgLines.Length; i++)
                {
                    Vector2 lineSize = Game1.smallFont.MeasureString(msgLines[i]) * this.phoneUiScale;
                    b.DrawString(
                        Game1.smallFont,
                        msgLines[i],
                        new Vector2(previewBounds.X + (previewBounds.Width - lineSize.X) / 2, startY + i * (int)(msgSize.Y + ScaleValue(5))),
                        Color.Black,
                        0f,
                        Vector2.Zero,
                        this.phoneUiScale,
                        SpriteEffects.None,
                        1f);
                }
            }
            else
            {
                this.avatarPhotoCandidateIndex = Math.Clamp(this.avatarPhotoCandidateIndex, 0, this.avatarPhotoCandidates.Count - 1);
                string currentPath = this.avatarPhotoCandidates[this.avatarPhotoCandidateIndex];

                if (TryGetImageTexture(currentPath, out Texture2D previewTexture))
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
                    b.DrawString(
                        Game1.smallFont,
                        "Unable to load this image.",
                        new Vector2(previewBounds.X + ScaleValue(20), previewBounds.Y + ScaleValue(20)),
                        Color.Black,
                        0f,
                        Vector2.Zero,
                        this.phoneUiScale,
                        SpriteEffects.None,
                        1f);
                }

                if (this.avatarPhotoCandidates.Count > 1)
                {
                    this.avatarPickerPrevBounds = new Rectangle(
                        previewBounds.X + ScaleValue(8),
                        previewBounds.Y + previewBounds.Height / 2 - ScaleValue(20),
                        ScaleValue(40),
                        ScaleValue(40));
                    this.avatarPickerNextBounds = new Rectangle(
                        previewBounds.Right - ScaleValue(48),
                        previewBounds.Y + previewBounds.Height / 2 - ScaleValue(20),
                        ScaleValue(40),
                        ScaleValue(40));

                    DrawPickerNavButton(b, this.avatarPickerPrevBounds, isNext: false);
                    DrawPickerNavButton(b, this.avatarPickerNextBounds, isNext: true);
                }

                bool selected = string.Equals(this.avatarSelectedPhotoPath, currentPath, StringComparison.OrdinalIgnoreCase);
                this.avatarPickerToggleBounds = new Rectangle(
                    panelBounds.X + ScaleValue(168),
                    previewBounds.Bottom + ScaleValue(18),
                    ScaleValue(132),
                    ScaleValue(46));

                IClickableMenu.drawTextureBox(
                    b,
                    Game1.menuTexture,
                    new Rectangle(0, 256, 60, 60),
                    this.avatarPickerToggleBounds.X,
                    this.avatarPickerToggleBounds.Y,
                    this.avatarPickerToggleBounds.Width,
                    this.avatarPickerToggleBounds.Height,
                    selected ? new Color(200, 240, 200, 230) : new Color(255, 255, 255, 220),
                    1f,
                    false);

                string toggleLabel = selected ? "Selected" : "Select";
                Vector2 toggleSize = Game1.smallFont.MeasureString(toggleLabel) * this.phoneUiScale;
                b.DrawString(
                    Game1.smallFont,
                    toggleLabel,
                    new Vector2(
                        this.avatarPickerToggleBounds.X + (this.avatarPickerToggleBounds.Width - toggleSize.X) / 2f,
                        this.avatarPickerToggleBounds.Y + ScaleValue(10)),
                    Color.Black,
                    0f,
                    Vector2.Zero,
                    this.phoneUiScale,
                    SpriteEffects.None,
                    1f);
            }

            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                this.avatarPickerCancelBounds.X,
                this.avatarPickerCancelBounds.Y,
                this.avatarPickerCancelBounds.Width,
                this.avatarPickerCancelBounds.Height,
                new Color(255, 255, 255, 220),
                1f,
                false);

            string cancelText = "Cancel";
            Vector2 cancelSize = Game1.smallFont.MeasureString(cancelText) * this.phoneUiScale;
            b.DrawString(
                Game1.smallFont,
                cancelText,
                new Vector2(
                    this.avatarPickerCancelBounds.X + (this.avatarPickerCancelBounds.Width - cancelSize.X) / 2f,
                    this.avatarPickerCancelBounds.Y + (this.avatarPickerCancelBounds.Height - cancelSize.Y) / 2f + ScaleValue(2)),
                Color.Black,
                0f,
                Vector2.Zero,
                this.phoneUiScale,
                SpriteEffects.None,
                1f);

            b.Draw(
                Game1.mouseCursors,
                this.avatarPickerOkBounds,
                new Rectangle(128, 256, 64, 64),
                Color.White);
        }

        private void DrawPickerNavButton(SpriteBatch b, Rectangle bounds, bool isNext)
        {
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                new Color(0, 0, 0, 140),
                1f,
                false);

            Rectangle source = isNext
                ? Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 33)
                : Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 44);

            b.Draw(
                Game1.mouseCursors,
                new Rectangle(bounds.X + ScaleValue(8), bounds.Y + ScaleValue(8), bounds.Width - ScaleValue(16), bounds.Height - ScaleValue(16)),
                source,
                Color.White);
        }

        private void DrawSocialProfileAvatarCameraButton(SpriteBatch b, Rectangle bounds)
        {
            IClickableMenu.drawTextureBox(
                b,
                Game1.menuTexture,
                new Rectangle(0, 256, 60, 60),
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                new Color(0, 0, 0, 140),
                1f,
                false);

            Rectangle iconSource = new Rectangle(72, 32, 18, 15);
            int iconW = ScaleValue(18);
            int iconH = ScaleValue(15);
            Rectangle dest = new Rectangle(
                bounds.X + (bounds.Width - iconW) / 2,
                bounds.Y + (bounds.Height - iconH) / 2,
                iconW,
                iconH);

            b.Draw(Game1.mouseCursors2, dest, iconSource, Color.White);
        }

        private static List<string> SplitTextIntoLines(string text, SpriteFont font, int maxWidth)
        {
            List<string> lines = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                lines.Add("");
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

        private static (int Start, int End) GetSelectionRange(int cursorIndex, int selectionAnchorIndex, int textLength)
        {
            int safeCursorIndex = Math.Clamp(cursorIndex, 0, textLength);
            int safeSelectionAnchorIndex = Math.Clamp(selectionAnchorIndex, 0, textLength);
            return safeCursorIndex < safeSelectionAnchorIndex
                ? (safeCursorIndex, safeSelectionAnchorIndex)
                : (safeSelectionAnchorIndex, safeCursorIndex);
        }

        private static int MeasureTextSubstringWidth(SpriteFont font, string text, int startIndex, int length)
        {
            if (length <= 0) return 0;
            string safeText = text ?? "";
            int safeStartIndex = Math.Clamp(startIndex, 0, safeText.Length);
            int safeLength = Math.Clamp(length, 0, safeText.Length - safeStartIndex);
            if (safeLength <= 0) return 0;
            return (int)Math.Round(font.MeasureString(safeText.Substring(safeStartIndex, safeLength)).X);
        }

        private static int GetVisibleWindowStart(string text, SpriteFont font, int maxWidth, int cursorIndex)
        {
            string safeText = text ?? "";
            int safeCursorIndex = Math.Clamp(cursorIndex, 0, safeText.Length);

            if (safeText.Length == 0 || font.MeasureString(safeText).X <= maxWidth)
                return 0;

            int visibleStart = safeCursorIndex;
            while (visibleStart > 0)
            {
                string candidate = safeText.Substring(visibleStart - 1, safeCursorIndex - (visibleStart - 1));
                if (font.MeasureString(candidate).X > maxWidth)
                    break;
                visibleStart--;
            }
            return visibleStart;
        }

        private static int GetVisibleWindowEnd(string text, SpriteFont font, int maxWidth, int visibleStart, int cursorIndex)
        {
            string safeText = text ?? "";
            int safeCursorIndex = Math.Clamp(cursorIndex, 0, safeText.Length);

            if (safeText.Length == 0 || font.MeasureString(safeText).X <= maxWidth)
                return safeText.Length;

            int visibleEnd = safeCursorIndex;
            while (visibleEnd < safeText.Length)
            {
                string candidate = safeText.Substring(visibleStart, visibleEnd - visibleStart + 1);
                if (font.MeasureString(candidate).X > maxWidth)
                    break;
                visibleEnd++;
            }
            return visibleEnd;
        }

        private (string VisibleText, int VisibleStartIndex, int CursorOffset) GetVisibleTextForInput(string text, SpriteFont font, int maxWidth, int cursorIndex)
        {
            string safeText = text ?? "";
            cursorIndex = Math.Clamp(cursorIndex, 0, safeText.Length);

            if (safeText.Length == 0 || font.MeasureString(safeText).X <= maxWidth)
                return (safeText, 0, (int)font.MeasureString(safeText[..cursorIndex]).X);

            int startIndex = GetVisibleWindowStart(safeText, font, maxWidth, cursorIndex);
            int endIndex = GetVisibleWindowEnd(safeText, font, maxWidth, startIndex, cursorIndex);

            string visibleText = safeText.Substring(startIndex, endIndex - startIndex);
            int cursorOffset = MeasureTextSubstringWidth(font, safeText, startIndex, cursorIndex - startIndex);
            return (visibleText, startIndex, cursorOffset);
        }

        private void DrawEditableTextInput(
            SpriteBatch b,
            Rectangle inputBounds,
            string text,
            int cursorIndex,
            int selectionAnchorIndex,
            bool isMultiline,
            bool isFocused)
        {
            SpriteFont font = Game1.smallFont;
            float textScale = this.phoneUiScale;
            int padding = ScaleValue(15);
            int maxWidth = inputBounds.Width - (padding * 2);
            maxWidth = (int)(maxWidth / textScale);

            string safeText = text ?? "";
            int safeCursorIndex = Math.Clamp(cursorIndex, 0, safeText.Length);
            int safeSelectionAnchorIndex = Math.Clamp(selectionAnchorIndex, 0, safeText.Length);

            (int selectionStart, int selectionEnd) = GetSelectionRange(safeCursorIndex, safeSelectionAnchorIndex, safeText.Length);
            bool hasSelection = selectionStart != selectionEnd;
            int lineHeight = Math.Max(1, (int)Math.Ceiling(((int)font.MeasureString("A").Y + 2) * textScale));

            if (isMultiline)
            {
                List<string> lines = SplitTextIntoLines(safeText, font, maxWidth);
                int currentY = inputBounds.Y + padding;

                int cursorLineIndex = 0;
                int cursorCharOffset = 0;
                int runningCharCount = 0;

                for (int i = 0; i < lines.Count; i++)
                {
                    string lineText = lines[i];
                    if (safeCursorIndex >= runningCharCount && safeCursorIndex <= runningCharCount + lineText.Length)
                    {
                        cursorLineIndex = i;
                        cursorCharOffset = safeCursorIndex - runningCharCount;
                    }
                    runningCharCount += lineText.Length;
                }

                if (safeCursorIndex >= runningCharCount && lines.Count > 0)
                {
                    cursorLineIndex = lines.Count - 1;
                    cursorCharOffset = safeCursorIndex - (runningCharCount - lines[^1].Length);
                }

                for (int i = 0; i < lines.Count; i++)
                {
                    string lineText = lines[i];
                    Vector2 linePos = new Vector2(inputBounds.X + padding, currentY);
                    b.DrawString(font, lineText, linePos, Color.Black, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);

                    if (isFocused && i == cursorLineIndex && (DateTime.UtcNow.Millisecond % 1000 < 500))
                    {
                        int safeOffset = Math.Clamp(cursorCharOffset, 0, lineText.Length);
                        float cursorOffset = font.MeasureString(lineText.Substring(0, safeOffset)).X * textScale;
                        int cursorX = inputBounds.X + padding + (int)Math.Round(cursorOffset);
                        b.Draw(Game1.staminaRect, new Rectangle(cursorX, currentY, 2, lineHeight), Color.Black);
                    }

                    currentY += lineHeight;
                }
            }
            else
            {
                (string visibleText, int visibleStartIndex, int cursorOffset) = GetVisibleTextForInput(safeText, font, maxWidth, safeCursorIndex);

                if (hasSelection)
                {
                    int visibleSelectionStart = Math.Clamp(selectionStart, visibleStartIndex, visibleStartIndex + visibleText.Length);
                    int visibleSelectionEnd = Math.Clamp(selectionEnd, visibleStartIndex, visibleStartIndex + visibleText.Length);
                    if (visibleSelectionEnd > visibleSelectionStart)
                    {
                        int highlightX = inputBounds.X + padding + (int)Math.Round(MeasureTextSubstringWidth(font, safeText, visibleStartIndex, visibleSelectionStart - visibleStartIndex) * textScale);
                        int highlightWidth = (int)Math.Round(MeasureTextSubstringWidth(font, safeText, visibleSelectionStart, visibleSelectionEnd - visibleSelectionStart) * textScale);
                        b.Draw(Game1.staminaRect, new Rectangle(highlightX, inputBounds.Y + ScaleValue(15), Math.Max(2, highlightWidth), lineHeight), new Color(80, 140, 255, 140));
                    }
                }

                Vector2 textPosition = new Vector2(inputBounds.X + padding, inputBounds.Y + ScaleValue(15));
                b.DrawString(font, visibleText, textPosition, Color.Black, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);

                if (isFocused && (DateTime.UtcNow.Millisecond % 1000 < 500))
                {
                    int cursorX = inputBounds.X + padding + (int)Math.Round(cursorOffset * textScale);
                    b.Draw(Game1.staminaRect, new Rectangle(cursorX, inputBounds.Y + ScaleValue(15), 2, lineHeight), Color.Black);
                }
            }
        }

        private bool TryApplyProfileEditorKey(Keys key)
        {
            if (this.activeProfileField == ProfileField.None)
                return false;

            if (this.activeProfileField == ProfileField.Age)
            {
                return this.ageTextBox.HandleKeyPress(key);
            }
            else if (this.activeProfileField == ProfileField.Birthday)
            {
                return this.birthdayTextBox.HandleKeyPress(key);
            }
            else if (this.activeProfileField == ProfileField.AboutMe)
            {
                return this.aboutMeTextBox.HandleKeyPress(key);
            }

            return false;
        }

        private void ApplyTextInputToActiveField(string textInput)
        {
            if (this.activeProfileField == ProfileField.None)
                return;

            if (this.activeProfileField == ProfileField.Age)
            {
                this.ageTextBox.RecieveTextInput(textInput);
            }
            else if (this.activeProfileField == ProfileField.Birthday)
            {
                this.birthdayTextBox.RecieveTextInput(textInput);
            }
            else if (this.activeProfileField == ProfileField.AboutMe)
            {
                this.aboutMeTextBox.RecieveTextInput(textInput);
            }
        }

        private void ApplyBackspaceToActiveField()
        {
            if (this.activeProfileField == ProfileField.None)
                return;

            if (this.activeProfileField == ProfileField.Age)
            {
                this.ageTextBox.RecieveBackspace();
            }
            else if (this.activeProfileField == ProfileField.Birthday)
            {
                this.birthdayTextBox.RecieveBackspace();
            }
            else if (this.activeProfileField == ProfileField.AboutMe)
            {
                this.aboutMeTextBox.RecieveBackspace();
            }
        }
    }
}
