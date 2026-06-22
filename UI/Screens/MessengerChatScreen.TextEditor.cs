using System;
using System.Collections.Generic;
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
    public partial class MessengerChatScreen : IClickableMenu
    {
        private sealed class MessengerTextInputSubscriber : IKeyboardSubscriber
        {
            private readonly MessengerChatScreen owner;

            public bool Selected { get; set; } = true; // Always selected when active

            public MessengerTextInputSubscriber(MessengerChatScreen owner)
            {
                this.owner = owner;
            }

            public void RecieveTextInput(char inputChar)
            {
                if (!Selected) return;
                owner.TryApplyComposedTextInput(inputChar.ToString());
            }

            public void RecieveTextInput(string text)
            {
                if (!Selected) return;
                owner.TryApplyComposedTextInput(text);
            }

            public void RecieveCommandInput(char command)
            {
                if (!Selected) return;
                if (command == '\b')
                {
                    owner.chatTextBox.RecieveBackspace();
                    ModEntry.RegisterTextInputActivity(owner.npcName);
                }
            }
            public void RecieveSpecialInput(Keys key) { }
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
                        this.pendingKeyboardTask = (Task<string>)showMethod.Invoke(null, new object[] { "Input", "Enter text", currentText, false })!;
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
                    string result = this.pendingKeyboardTask.Result;
                    this.chatTextBox.Text = result;
                    this.chatTextBox.CursorIndex = result.Length;
                    this.chatTextBox.SelectionAnchorIndex = result.Length;
                }
                this.pendingKeyboardTask = null;
            }
        }

        // ==========================================
        // TEXT EDITOR LOGIC PORTED FROM SMARTPHONE
        // ==========================================

        private void TryApplyComposedTextInput(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
                
            // Ignore control chars
            if (text.Length == 1 && char.IsControl(text[0]))
                return;

            this.chatTextBox.RecieveTextInput(text);
            ModEntry.RegisterTextInputActivity(this.npcName);
        }

        private bool TryApplyEditableTextKey(Keys key)
        {
            if (key == Keys.Back)
            {
                return true;
            }
            bool handled = this.chatTextBox.HandleKeyPress(key);
            if (handled)
            {
                ModEntry.RegisterTextInputActivity(this.npcName);
            }
            return handled;
        }
    }
}
