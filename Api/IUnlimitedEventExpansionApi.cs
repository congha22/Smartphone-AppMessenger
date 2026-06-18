using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Smartphone
{
    public interface IUnlimitedEventExpansionApi
    {
        /// ======================================
        /// API for Smartphone only
        /// ======================================

        /// <summary>
        /// Sends a summary of the player's conversations with NPCs to the mod. This method is used to provide the mod with information about which NPCs the player has interacted with and what topics were discussed.
        /// </summary>
        /// <param name="npcConversationSummary">A dictionary containing the NPC names as keys and the conversation summaries as values.</param>
        void SendNpcConversationSummary(Dictionary<string, string> npcConversationSummary);

        /// <summary>
        /// Opens the schedule event time menu for a specific NPC and event type. This method is used to allow players to schedule events with NPCs at specific times.
        /// </summary>
        /// <param name="eventNpcName">The name of the NPC for whom the event is being scheduled.</param>
        /// <param name="eventType">The type of event being scheduled.</param>
        /// <param name="npcResponse">The confirmation message for the NPC's response.</param>
        void OpenScheduleEventTimeMenu(string eventNpcName,
            string eventType,
            string? npcResponse = null
        );

        /// <summary>
        /// Checks if the player can schedule a new event. This method is used to enforce any limitations on the number of events that can be scheduled, such as allowing only one event per day without an OpenAI key.
        /// </summary>
        /// <returns>True if the player can schedule a new event; otherwise, false.</returns>
        bool CanScheduleNewEvent();
    }
}
