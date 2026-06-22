using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace SmartphoneAppMessenger
{
    internal static class PhoneDialogueRuntime
    {
        private const string InPersonFallbackMessage = "Can you see me some time today? I have something important to ask you!";

        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, PhoneDialogueChoiceState> PendingChoices =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] InlineEmotionTokens =
        {
            "$h",
            "$s",
            "$u",
            "$l",
            "$a",
            "$neutral"
        };

        public static void ClearDailyState()
        {
            lock (SyncRoot)
            {
                PendingChoices.Clear();
            }
        }

        public static bool HasPendingChoice(string npcName)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return false;

            lock (SyncRoot)
            {
                return PendingChoices.ContainsKey(npcName);
            }
        }

        public static bool TryGetPendingChoice(string npcName, out PhoneDialogueChoiceState? choiceState)
        {
            choiceState = null;
            if (string.IsNullOrWhiteSpace(npcName))
                return false;

            lock (SyncRoot)
            {
                if (!PendingChoices.TryGetValue(npcName, out PhoneDialogueChoiceState? existingState))
                    return false;

                choiceState = existingState;
                return true;
            }
        }

        public static async Task DeliverDialogueSequenceAsync(
            string npcName,
            IEnumerable<Dialogue> dialogues,
            bool useRandomDelay,
            int minDelayMs = 3000,
            int maxDelayMs = 5000,
            string? skipFirstLineIfMatches = null)
        {
            if (string.IsNullOrWhiteSpace(npcName) || dialogues == null)
                return;

            ClearPendingChoice(npcName);

            int safeMinDelayMs = Math.Max(0, minDelayMs);
            int safeMaxDelayMs = Math.Max(safeMinDelayMs + 1, maxDelayMs);
            Random? random = useRandomDelay ? new Random() : null;
            bool isFirstDialogue = true;

            foreach (Dialogue dialogue in dialogues)
            {
                await DeliverSingleDialogueAsync(
                    npcName,
                    dialogue,
                    useRandomDelay,
                    random,
                    safeMinDelayMs,
                    safeMaxDelayMs,
                    isFirstDialogue ? skipFirstLineIfMatches : null);

                isFirstDialogue = false;

                if (HasPendingChoice(npcName))
                    break;
            }
        }

        public static bool TrySelectChoice(string npcName, int optionIndex)
        {
            if (string.IsNullOrWhiteSpace(npcName))
                return false;

            PhoneDialogueChoiceState? selectedState;
            PhoneDialogueOption? selectedOption;

            lock (SyncRoot)
            {
                if (!PendingChoices.TryGetValue(npcName, out selectedState))
                    return false;

                if (optionIndex < 0 || optionIndex >= selectedState.Options.Count)
                    return false;

                selectedOption = selectedState.Options[optionIndex];
                PendingChoices.Remove(npcName);
            }

            if (selectedState == null || selectedOption == null)
                return false;

            AddPlayerLine(npcName, selectedOption.DisplayText);

            try
            {
                selectedState.Dialogue.chooseResponse(selectedOption.Response);
            }
            catch (Exception ex)
            {
                ModEntry.Instance.Monitor.Log(
                    $"Phone dialogue choice selection failed for {npcName}: {ex}",
                    LogLevel.Warn);
                return false;
            }

            // Removed PhoneMenu check since we're in AppMessenger

            _ = Task.Run(async () =>
            {
                await DeliverDialogueSequenceAsync(
                    npcName,
                    new[] { selectedState.Dialogue },
                    useRandomDelay: true,
                    minDelayMs: 1000,
                    maxDelayMs: 2000,
                    skipFirstLineIfMatches: selectedState.PromptText);
            });

            return true;
        }

        private static async Task DeliverSingleDialogueAsync(
            string npcName,
            Dialogue dialogue,
            bool useRandomDelay,
            Random? random,
            int minDelayMs,
            int maxDelayMs,
            string? skipFirstLineIfMatches)
        {
            if (dialogue == null)
                return;

            if (ContainsUnsafeCommand(dialogue))
            {
                AddNpcLine(npcName, InPersonFallbackMessage);
                return;
            }

            string lastSpokenLine = string.Empty;
            string normalizedSkipLine = NormalizeDialogueTextForPhone(skipFirstLineIfMatches ?? string.Empty);
            bool shouldCheckFirstLineSkip = !string.IsNullOrWhiteSpace(normalizedSkipLine);
            bool firstLineChecked = false;
            int guard = 0;
            while (!dialogue.isDialogueFinished() && guard++ < 256)
            {
                try
                {
                    dialogue.prepareCurrentDialogueForDisplay();
                }
                catch (Exception ex)
                {
                    ModEntry.Instance.Monitor.Log(
                        $"Phone dialogue prepare failed for {npcName}: {ex}",
                        LogLevel.Warn);
                    break;
                }

                bool isQuestion = dialogue.isCurrentDialogueAQuestion();
                string displayLine = NormalizeDialogueTextForPhone(dialogue.getCurrentDialogue());

                if (string.IsNullOrWhiteSpace(displayLine))
                {
                    dialogue.exitCurrentDialogue();
                    continue;
                }

                if (!firstLineChecked)
                {
                    firstLineChecked = true;

                    if (shouldCheckFirstLineSkip
                        && string.Equals(displayLine, normalizedSkipLine, StringComparison.OrdinalIgnoreCase))
                    {
                        dialogue.exitCurrentDialogue();
                        continue;
                    }
                }

                if (useRandomDelay && random != null)
                {
                    int delayMs = random.Next(minDelayMs, maxDelayMs);
                    await Task.Delay(delayMs);
                }

                AddNpcLine(npcName, displayLine);
                lastSpokenLine = displayLine;

                if (isQuestion)
                {
                    Response[] responseOptions = dialogue.getResponseOptions() ?? Array.Empty<Response>();
                    List<PhoneDialogueOption> mappedOptions = responseOptions
                        .Select(response => new PhoneDialogueOption(response, ExtractResponseText(response)))
                        .Where(option => !string.IsNullOrWhiteSpace(option.DisplayText))
                        .ToList();

                    if (mappedOptions.Count > 0)
                    {
                        string promptText = string.IsNullOrWhiteSpace(lastSpokenLine)
                            ? $"{npcName} is waiting for your response."
                            : lastSpokenLine;

                        SetPendingChoice(new PhoneDialogueChoiceState(npcName, dialogue, promptText, mappedOptions));
                        return;
                    }
                }

                dialogue.exitCurrentDialogue();
            }

            ClearPendingChoice(npcName);
        }

        private static bool ContainsUnsafeCommand(Dialogue dialogue)
        {
            foreach (DialogueLine dialogueLine in dialogue.dialogues)
            {
                string text = (dialogueLine?.Text ?? string.Empty).TrimStart();
                if (text.StartsWith("$v", StringComparison.OrdinalIgnoreCase)
                    || text.StartsWith("$action", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeDialogueTextForPhone(string rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return string.Empty;

            string text = rawLine;
            text = text.Replace("{", string.Empty, StringComparison.Ordinal);
            text = text.Replace("%noturn", string.Empty, StringComparison.OrdinalIgnoreCase);
            text = text.Replace("$k", string.Empty, StringComparison.OrdinalIgnoreCase);
            text = text.Replace("$e", string.Empty, StringComparison.OrdinalIgnoreCase);

            foreach (string token in InlineEmotionTokens)
                text = text.Replace(token, string.Empty, StringComparison.OrdinalIgnoreCase);

            for (int portraitIndex = 0; portraitIndex <= 32; portraitIndex++)
                text = text.Replace($"${portraitIndex}", string.Empty, StringComparison.Ordinal);

            text = string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return text.Trim();
        }

        private static string ExtractResponseText(Response response)
        {
            if (response == null)
                return string.Empty;

            try
            {
                if (!string.IsNullOrWhiteSpace(response.responseText))
                    return response.responseText.Trim();

                Type responseType = response.GetType();
                if (responseType.GetField("responseText")?.GetValue(response) is string reflectedResponseText
                    && !string.IsNullOrWhiteSpace(reflectedResponseText))
                {
                    return reflectedResponseText.Trim();
                }

                if (responseType.GetField("responseKey")?.GetValue(response) is string responseKey
                    && !string.IsNullOrWhiteSpace(responseKey))
                {
                    return responseKey.Trim();
                }
            }
            catch
            {
                // Fall through to empty text.
            }

            return string.Empty;
        }

        private static void AddNpcLine(string npcName, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            MessageManager.AddMessage(npcName, line, type: "response");
            ModEntry.lastTimeReceiveMessage = Game1.timeOfDay;
        }

        private static void AddPlayerLine(string npcName, string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            MessageManager.AddMessage(npcName, line, type: "sent");
        }

        private static void SetPendingChoice(PhoneDialogueChoiceState choiceState)
        {
            lock (SyncRoot)
            {
                PendingChoices[choiceState.NpcName] = choiceState;
            }
        }

        private static void ClearPendingChoice(string npcName)
        {
            lock (SyncRoot)
            {
                PendingChoices.Remove(npcName);
            }
        }

        public static void FirstDailyText(string npcName, string message)
        {
            NPC npc = Game1.getCharacterFromName(npcName);
            if (npc == null) return;

            bool talkedToToday = Game1.player.friendshipData.TryGetValue(npcName, out Friendship friendship) && friendship.TalkedToToday;
            if (talkedToToday)
            {
                Task.Run(async () =>
                {
                    if (!ModEntry.TryConsumeAiCallSlot())
                        return;

                    string reply = await ModEntry.SendMessageToAssistant(npcName, text: message);
                    if (!string.IsNullOrWhiteSpace(reply)
                        && !reply.StartsWith("SYSTEM:", StringComparison.OrdinalIgnoreCase))
                        MessageManager.AddMessage(npcName, reply, "response");
                    ModEntry.lastTimeReceiveMessage = Game1.timeOfDay;
                });
            }
            else
            {
                if (friendship != null)
                    friendship.TalkedToToday = true;

                npc.checkForNewCurrentDialogue(Game1.player.getFriendshipHeartLevelForNPC(npcName));
                if (npc.currentMarriageDialogue != null && npc.currentMarriageDialogue.Count > 0)
                {
                    if (npc.CurrentDialogue == null)
                        npc.CurrentDialogue = new Stack<Dialogue>();

                    for (int i = npc.currentMarriageDialogue.Count - 1; i >= 0; i--)
                    {
                        var dialogueRef = npc.currentMarriageDialogue[i];
                        Dialogue actualDialogue = dialogueRef.GetDialogue(npc);

                        if (actualDialogue != null)
                        {
                            npc.CurrentDialogue.Push(actualDialogue);
                        }
                    }

                    npc.currentMarriageDialogue.Clear();
                }

                if (npc.CurrentDialogue != null && npc.CurrentDialogue.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await DeliverDialogueSequenceAsync(
                            npcName,
                            npc.CurrentDialogue,
                            useRandomDelay: true,
                            minDelayMs: 3000,
                            maxDelayMs: 5000);

                        npc.CurrentDialogue?.Clear();
                    });
                }
            }
        }
    }
}
