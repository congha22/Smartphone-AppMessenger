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

        private int PhoneX(int baseOffset) => this.xPositionOnScreen + this.phoneContentOffsetX + ScaleValue(baseOffset - 40);
        private int PhoneY(int baseOffset) => this.yPositionOnScreen + this.phoneContentOffsetY + ScaleValue(baseOffset - 110);
        private Rectangle PhoneRect(int baseX, int baseY, int baseWidth, int baseHeight) =>
            new Rectangle(PhoneX(baseX), PhoneY(baseY), ScaleValue(baseWidth), ScaleValue(baseHeight));

        private Rectangle GetUiViewportBounds()
        {
            return new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height);
        }



        private bool TryGetPlayerAvatarTexture(out Texture2D texture)
        {
            texture = null!;
            string activeSave = MessageManager.GetActiveSaveFolderName();
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

        internal static bool TryGetImageTexture(string imagePath, out Texture2D texture)
        {
            texture = null!;
            if (string.IsNullOrWhiteSpace(imagePath))
                return false;

            if (avatarImageCache.TryGetValue(imagePath, out Texture2D? cachedTexture) && cachedTexture != null)
            {
                texture = cachedTexture;
                return true;
            }

            if (avatarFailedImagePaths.Contains(imagePath))
                return false;

            try
            {
                if (File.Exists(imagePath))
                {
                    using FileStream stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    Texture2D loadedTexture = Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
                    avatarImageCache[imagePath] = loadedTexture;
                    texture = loadedTexture;
                    return true;
                }
                else if (ModEntry.iSmartphoneApi != null)
                {
                    Texture2D loadedTexture = ModEntry.iSmartphoneApi.GetPlayerPhotoTexture(imagePath);
                    if (loadedTexture != null)
                    {
                        avatarImageCache[imagePath] = loadedTexture;
                        texture = loadedTexture;
                        return true;
                    }
                }
            }
            catch (Exception)
            {
                // ignore
            }

            avatarFailedImagePaths.Add(imagePath);
            return false;
        }

        private void EnsureAvatarPhotoCandidatesLoaded()
        {
            this.avatarPhotoCandidates.Clear();
            if (this.smartphoneApi != null)
            {
                string smartphoneDir = Path.Combine(Directory.GetParent(ModEntry.Instance.Helper.DirectoryPath).FullName, "Smartphone");
                string activeSave = MessageManager.GetActiveSaveFolderName();
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
            ClearAvatarCache();

            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try
                {
                    string activeSave = MessageManager.GetActiveSaveFolderName();
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

            if (!string.IsNullOrWhiteSpace(MessageManager.currentPlayerAvatar))
            {
                TransferManager.SendSelectedAvatar(MessageManager.currentPlayerAvatar);
            }
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
            int titleX = PhoneX(105);
            int titleY = PhoneY(65);
            b.DrawString(Game1.dialogueFont, "Profile", new Vector2(titleX, titleY), Color.Black, 0f, Vector2.Zero, this.phoneUiScale, SpriteEffects.None, 1f);

            Rectangle headerBounds = PhoneRect(50, 120, 500, 190);
            UI.CardDrawing.DrawCard(
                b,
                headerBounds.X,
                headerBounds.Y,
                headerBounds.Width,
                headerBounds.Height,
                Color.LightGray,
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
                UI.CardDrawing.DrawCard(
                    b,
                    this.profileAvatarBounds.X,
                    this.profileAvatarBounds.Y,
                    this.profileAvatarBounds.Width,
                    this.profileAvatarBounds.Height,
                    Color.White,
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
            UI.CardDrawing.DrawCard(
                b,
                this.profileAgeFieldBounds.X,
                this.profileAgeFieldBounds.Y,
                this.profileAgeFieldBounds.Width,
                this.profileAgeFieldBounds.Height,
                Color.White,
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

            UI.CardDrawing.DrawCard(
                b,
                this.profileBirthdayFieldBounds.X,
                this.profileBirthdayFieldBounds.Y,
                this.profileBirthdayFieldBounds.Width,
                this.profileBirthdayFieldBounds.Height,
                Color.White,
                1f,
                false);

            this.birthdayTextBox.Draw(
                b,
                this.profileBirthdayFieldBounds,
                this.phoneUiScale,
                this.activeProfileField == ProfileField.Birthday);

            // Season Button
            UI.CardDrawing.DrawCard(
                b,
                this.profileSeasonButtonBounds.X,
                this.profileSeasonButtonBounds.Y,
                this.profileSeasonButtonBounds.Width,
                this.profileSeasonButtonBounds.Height,
                Color.White,
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
            UI.CardDrawing.DrawCard(
                b,
                this.profileDescriptionFieldBounds.X,
                this.profileDescriptionFieldBounds.Y,
                this.profileDescriptionFieldBounds.Width,
                this.profileDescriptionFieldBounds.Height,
                Color.White,
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
            UI.CardDrawing.DrawCard(
                b,
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

            UI.CardDrawing.DrawCard(
                b,
                previewBounds.X,
                previewBounds.Y,
                previewBounds.Width,
                previewBounds.Height,
                Color.White,
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

                UI.CardDrawing.DrawCard(
                    b,
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

            UI.CardDrawing.DrawCard(
                b,
                this.avatarPickerCancelBounds.X,
                this.avatarPickerCancelBounds.Y,
                this.avatarPickerCancelBounds.Width,
                this.avatarPickerCancelBounds.Height,
                Color.White,
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
            UI.CardDrawing.DrawCard(
                b,
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
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            bool hovered = bounds.Contains(Game1.getMouseX(), Game1.getMouseY());
            Color boxColor = hovered
                ? new Color(95, 145, 185, 135)
                : new Color(20, 20, 20, 110);

            UI.CardDrawing.DrawCard(
                b,
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                boxColor, 1f, false);

            int iconPadding = ScaleValue(7);
            Rectangle iconBounds = new Rectangle(
                bounds.X + iconPadding,
                bounds.Y + iconPadding,
                Math.Max(1, bounds.Width - (iconPadding * 2)),
                Math.Max(1, bounds.Height - (iconPadding * 2)));

            b.Draw(Game1.mouseCursors2, iconBounds, new Rectangle(72, 32, 18, 15), Color.White);
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
