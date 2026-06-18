using System;
using System.Collections.Generic;
using StardewValley;

namespace SmartphoneAppMessenger
{
    // public class NotificationManager
    // {
    //     public static void AddNotification(params string[] args) { }
    //     public static void addNotification(params string[] args) { }
    // }

    public class GiftMemory
    {
        public string GiftName = "";
        public int DaysRemaining = 0;
        public string Description = "";
    }

    public class RecentEvent
    {
        public string Description = "";
    }

    public partial class ModEntry
    {
        public static List<NPC> GetNpcsWithBirthdayToday()
        {
            return new List<NPC>();
        }
    }

    public static partial class MessageManager
    {
        public static bool IsPlayerBirthdayToday() { return false; }
    }
}
