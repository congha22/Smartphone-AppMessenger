using System;
using StardewValley;
using StardewValley.Menus;

namespace SmartphoneAppMessenger
{
    public static class KeyboardManager
    {
        public static bool IsTextInputActive(IClickableMenu menu)
        {
            if (Game1.activeClickableMenu != menu)
            {
                return false;
            }

            if (menu is MessengerAppScreen appScreen)
            {
                if (appScreen.CurrentState == MessengerAppScreen.ScreenState.NpcList)
                {
                    return appScreen.Selected && Game1.keyboardDispatcher.Subscriber == appScreen;
                }
                else if (appScreen.CurrentState == MessengerAppScreen.ScreenState.ProfileEditor)
                {
                    return appScreen.IsAnyProfileFieldActive() && Game1.keyboardDispatcher.Subscriber == appScreen;
                }
            }
            else if (menu is MessengerChatScreen chatScreen)
            {
                return chatScreen.IsChatInputActive();
            }
            return false;
        }
    }
}
