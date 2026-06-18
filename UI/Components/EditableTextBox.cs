using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using TextCopy;

namespace SmartphoneAppMessenger
{
    public class EditableTextBox
    {
        public string Text = "";
        public int CursorIndex = 0;
        public int SelectionAnchorIndex = 0;
        public bool IsMultiline = false;
        public bool IsNumericOnly = false;
        public int MinNumericValue = 1;
        public int MaxNumericValue = 28;

        public void Clear()
        {
            Text = "";
            CursorIndex = 0;
            SelectionAnchorIndex = 0;
        }

        public void SelectAll()
        {
            SelectionAnchorIndex = 0;
            CursorIndex = Text.Length;
        }

        public void RecieveTextInput(string textInput)
        {
            if (string.IsNullOrEmpty(textInput))
                return;

            string filteredInput = textInput;
            if (IsNumericOnly)
            {
                filteredInput = new string(textInput.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(filteredInput))
                    return;
            }

            string safeText = Text ?? "";
            int safeCursorIndex = Math.Clamp(CursorIndex, 0, safeText.Length);
            int safeSelectionAnchorIndex = Math.Clamp(SelectionAnchorIndex, 0, safeText.Length);
            (int selectionStart, int selectionEnd) = GetSelectionRange(safeCursorIndex, safeSelectionAnchorIndex, safeText.Length);

            if (selectionStart != selectionEnd)
                safeText = safeText.Remove(selectionStart, selectionEnd - selectionStart);

            int insertIndex = selectionStart;
            string newText = safeText.Insert(insertIndex, filteredInput);

            if (IsNumericOnly)
            {
                if (int.TryParse(newText, out int bdayValue))
                {
                    if (bdayValue < MinNumericValue || bdayValue > MaxNumericValue)
                        return;
                }
                else if (newText.Length > 0)
                {
                    return;
                }
            }

            Text = newText;
            CursorIndex = insertIndex + filteredInput.Length;
            SelectionAnchorIndex = CursorIndex;
        }

        public void RecieveBackspace()
        {
            ApplyDelete(deleteForward: false);
        }

        public bool ApplyDelete(bool deleteForward)
        {
            string safeText = Text ?? "";
            int safeCursorIndex = Math.Clamp(CursorIndex, 0, safeText.Length);
            int safeSelectionAnchorIndex = Math.Clamp(SelectionAnchorIndex, 0, safeText.Length);
            (int selectionStart, int selectionEnd) = GetSelectionRange(safeCursorIndex, safeSelectionAnchorIndex, safeText.Length);

            if (selectionStart != selectionEnd)
            {
                safeText = safeText.Remove(selectionStart, selectionEnd - selectionStart);
                Text = safeText;
                CursorIndex = selectionStart;
                SelectionAnchorIndex = selectionStart;
                return true;
            }

            if (!deleteForward)
            {
                if (safeCursorIndex <= 0) return false;
                safeText = safeText.Remove(safeCursorIndex - 1, 1);
                Text = safeText;
                CursorIndex = safeCursorIndex - 1;
                SelectionAnchorIndex = CursorIndex;
                return true;
            }

            if (safeCursorIndex >= safeText.Length) return false;
            safeText = safeText.Remove(safeCursorIndex, 1);
            Text = safeText;
            CursorIndex = safeCursorIndex;
            SelectionAnchorIndex = CursorIndex;
            return true;
        }

        public bool HandleKeyPress(Keys key)
        {
            KeyboardState keyboardState = Keyboard.GetState();
            bool ctrlDown = keyboardState.IsKeyDown(Keys.LeftControl) || keyboardState.IsKeyDown(Keys.RightControl);
            bool shiftDown = keyboardState.IsKeyDown(Keys.LeftShift) || keyboardState.IsKeyDown(Keys.RightShift);

            string safeText = Text ?? "";
            CursorIndex = Math.Clamp(CursorIndex, 0, safeText.Length);
            SelectionAnchorIndex = Math.Clamp(SelectionAnchorIndex, 0, safeText.Length);

            if (ctrlDown && key == Keys.A)
            {
                CursorIndex = safeText.Length;
                SelectionAnchorIndex = 0;
                return true;
            }

            if (ctrlDown && key == Keys.C)
            {
                if (TryGetSelection(out int selStart, out int selEnd))
                {
                    try { ClipboardService.SetText(safeText.Substring(selStart, selEnd - selStart)); } catch { }
                }
                return true;
            }

            if (ctrlDown && key == Keys.X)
            {
                if (TryGetSelection(out int selStart, out int selEnd))
                {
                    try { ClipboardService.SetText(safeText.Substring(selStart, selEnd - selStart)); } catch { }
                    ApplyDelete(deleteForward: false);
                }
                return true;
            }

            if (ctrlDown && key == Keys.V)
            {
                string clipboardText = "";
                try { clipboardText = ClipboardService.GetText() ?? ""; } catch { }
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    if (!IsMultiline)
                    {
                        clipboardText = clipboardText.Replace("\r", "").Replace("\n", "");
                    }
                    RecieveTextInput(clipboardText);
                }
                return true;
            }

            switch (key)
            {
                case Keys.Back:
                    ApplyDelete(deleteForward: false);
                    return true;

                case Keys.Delete:
                    ApplyDelete(deleteForward: true);
                    return true;

                case Keys.Left:
                    if (shiftDown)
                    {
                        if (CursorIndex == SelectionAnchorIndex)
                            SelectionAnchorIndex = CursorIndex;
                        if (CursorIndex > 0)
                            CursorIndex--;
                    }
                    else
                    {
                        if (CursorIndex > 0)
                            CursorIndex--;
                        SelectionAnchorIndex = CursorIndex;
                    }
                    return true;

                case Keys.Right:
                    if (shiftDown)
                    {
                        if (CursorIndex == SelectionAnchorIndex)
                            SelectionAnchorIndex = CursorIndex;
                        if (CursorIndex < safeText.Length)
                            CursorIndex++;
                    }
                    else
                    {
                        if (CursorIndex < safeText.Length)
                            CursorIndex++;
                        SelectionAnchorIndex = CursorIndex;
                    }
                    return true;

                case Keys.Home:
                    if (shiftDown)
                    {
                        if (CursorIndex == SelectionAnchorIndex)
                            SelectionAnchorIndex = CursorIndex;
                    }
                    else
                    {
                        CursorIndex = 0;
                        SelectionAnchorIndex = CursorIndex;
                        return true;
                    }
                    CursorIndex = 0;
                    return true;

                case Keys.End:
                    if (shiftDown)
                    {
                        if (CursorIndex == SelectionAnchorIndex)
                            SelectionAnchorIndex = CursorIndex;
                    }
                    else
                    {
                        CursorIndex = safeText.Length;
                        SelectionAnchorIndex = CursorIndex;
                        return true;
                    }
                    CursorIndex = safeText.Length;
                    return true;

                case Keys.Enter:
                    if (IsMultiline)
                    {
                        RecieveTextInput("\n");
                        return true;
                    }
                    break;
            }

            return false;
        }

        public bool TryGetSelection(out int selectionStart, out int selectionEnd)
        {
            (selectionStart, selectionEnd) = GetSelectionRange(CursorIndex, SelectionAnchorIndex, Text.Length);
            return selectionStart != selectionEnd;
        }

        private static (int Start, int End) GetSelectionRange(int cursorIndex, int selectionAnchorIndex, int textLength)
        {
            int safeCursorIndex = Math.Clamp(cursorIndex, 0, textLength);
            int safeSelectionAnchorIndex = Math.Clamp(selectionAnchorIndex, 0, textLength);
            return safeCursorIndex < safeSelectionAnchorIndex
                ? (safeCursorIndex, safeSelectionAnchorIndex)
                : (safeSelectionAnchorIndex, safeCursorIndex);
        }

        // --- DRAWING RENDER HELPERS ---

        public void Draw(SpriteBatch b, Rectangle inputBounds, float phoneUiScale, bool isFocused)
        {
            SpriteFont font = Game1.smallFont;
            float textScale = phoneUiScale;
            int padding = (int)Math.Round(15 * phoneUiScale);
            int maxWidth = inputBounds.Width - (padding * 2);
            maxWidth = (int)(maxWidth / textScale);

            string safeText = Text ?? "";
            int safeCursorIndex = Math.Clamp(CursorIndex, 0, safeText.Length);
            int safeSelectionAnchorIndex = Math.Clamp(SelectionAnchorIndex, 0, safeText.Length);

            (int selectionStart, int selectionEnd) = GetSelectionRange(safeCursorIndex, safeSelectionAnchorIndex, safeText.Length);
            bool hasSelection = selectionStart != selectionEnd;
            int lineHeight = Math.Max(1, (int)Math.Ceiling(((int)font.MeasureString("A").Y + 2) * textScale));

            if (IsMultiline)
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
                        b.Draw(Game1.staminaRect, new Rectangle(highlightX, inputBounds.Y + (int)Math.Round(15 * phoneUiScale), Math.Max(2, highlightWidth), lineHeight), new Color(80, 140, 255, 140));
                    }
                }

                Vector2 textPosition = new Vector2(inputBounds.X + padding, inputBounds.Y + (int)Math.Round(15 * phoneUiScale));
                b.DrawString(font, visibleText, textPosition, Color.Black, 0f, Vector2.Zero, textScale, SpriteEffects.None, 1f);

                if (isFocused && (DateTime.UtcNow.Millisecond % 1000 < 500))
                {
                    int cursorX = inputBounds.X + padding + (int)Math.Round(cursorOffset * textScale);
                    b.Draw(Game1.staminaRect, new Rectangle(cursorX, inputBounds.Y + (int)Math.Round(15 * phoneUiScale), 2, lineHeight), Color.Black);
                }
            }
        }

        public void SetCursorFromClick(int mouseX, Rectangle inputBounds, float phoneUiScale)
        {
            SpriteFont font = Game1.smallFont;
            float textScale = phoneUiScale;
            int padding = (int)Math.Round(15 * phoneUiScale);
            int maxWidth = inputBounds.Width - (padding * 2);
            maxWidth = (int)(maxWidth / textScale);

            (string visibleText, int visibleStartIndex, _) = GetVisibleTextForInput(Text, font, maxWidth, CursorIndex);

            int localX = mouseX - (inputBounds.X + padding);
            if (localX <= 0)
            {
                CursorIndex = visibleStartIndex;
                SelectionAnchorIndex = CursorIndex;
                return;
            }

            for (int i = 0; i <= visibleText.Length; i++)
            {
                float width = MeasureTextSubstringWidth(font, visibleText, 0, i) * textScale;
                if (width >= localX)
                {
                    CursorIndex = visibleStartIndex + i;
                    SelectionAnchorIndex = CursorIndex;
                    return;
                }
            }

            CursorIndex = visibleStartIndex + visibleText.Length;
            SelectionAnchorIndex = CursorIndex;
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

        private Keys lastHeldKey = Keys.None;
        private double heldKeyTime = 0;
        private double lastRepeatTime = 0;

        public void Update(GameTime time, bool isFocused)
        {
            if (!isFocused)
            {
                lastHeldKey = Keys.None;
                return;
            }

            KeyboardState state = Keyboard.GetState();
            Keys currentKey = Keys.None;
            if (state.IsKeyDown(Keys.Left)) currentKey = Keys.Left;
            else if (state.IsKeyDown(Keys.Right)) currentKey = Keys.Right;
            else if (state.IsKeyDown(Keys.Delete)) currentKey = Keys.Delete;

            if (currentKey == Keys.None)
            {
                lastHeldKey = Keys.None;
                return;
            }

            if (currentKey != lastHeldKey)
            {
                lastHeldKey = currentKey;
                heldKeyTime = 0;
                lastRepeatTime = 0;
            }
            else
            {
                heldKeyTime += time.ElapsedGameTime.TotalMilliseconds;
                if (heldKeyTime >= 500)
                {
                    double sinceLastRepeat = heldKeyTime - lastRepeatTime;
                    if (sinceLastRepeat >= 50)
                    {
                        HandleKeyPress(currentKey);
                        lastRepeatTime = heldKeyTime;
                    }
                }
            }
        }
    }
}
