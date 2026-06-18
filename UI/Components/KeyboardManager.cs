using System;
using StardewValley;
using StardewValley.Menus;

namespace SmartphoneAppMessenger
{
    public static class KeyboardManager
    {
        public static bool IsTextInputActive(IClickableMenu menu)
        {
            if (menu is MessengerAppScreen appScreen)
            {
                if (appScreen.CurrentState == MessengerAppScreen.ScreenState.NpcList)
                {
                    return appScreen.Selected;
                }
                else if (appScreen.CurrentState == MessengerAppScreen.ScreenState.ProfileEditor)
                {
                    return appScreen.IsAnyProfileFieldActive();
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
