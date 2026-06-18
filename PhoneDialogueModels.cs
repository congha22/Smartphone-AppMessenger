using System.Collections.Generic;
using StardewValley;

namespace SmartphoneAppMessenger
{
    internal sealed class PhoneDialogueOption
    {
        public PhoneDialogueOption(Response response, string displayText)
        {
            Response = response;
            DisplayText = displayText;
        }

        public Response Response { get; }

        public string DisplayText { get; }
    }

    internal sealed class PhoneDialogueChoiceState
    {
        public PhoneDialogueChoiceState(
            string npcName,
            Dialogue dialogue,
            string promptText,
            List<PhoneDialogueOption> options)
        {
            NpcName = npcName;
            Dialogue = dialogue;
            PromptText = promptText;
            Options = options;
        }

        public string NpcName { get; }

        public Dialogue Dialogue { get; }

        public string PromptText { get; }

        public List<PhoneDialogueOption> Options { get; }
    }
}
