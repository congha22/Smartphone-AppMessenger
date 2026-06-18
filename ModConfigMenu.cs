using System;
using StardewModdingAPI;

namespace SmartphoneAppMessenger
{
    public partial class ModEntry
    {
        public static void ConfigMenu(IManifest ModManifest, IModHelper Helper)
        {
            var configMenu = Helper.ModRegistry.GetApi<Smartphone.Data.IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null)
                return;

            string[] aiModelValues =
            {
                ModConfig.OpenAIModel_51,
                ModConfig.OpenAIModel_5mini,
                ModConfig.OpenAIModel_5nano,
                ModConfig.OpenAIModel_54mini,
                ModConfig.OpenAIModel_54nano,
                ModConfig.GeminiModel_35Flash,
                ModConfig.GeminiModel_31FlashLite,
                ModConfig.GeminiModel_3FlashPreview
            };

            string[] characteristicValues =
            {
                ModConfig.CharacteristicModeMinimal,
                ModConfig.CharacteristicModeShort,
                ModConfig.CharacteristicModeLong
            };

            static string EnsureAllowedValue(string? value, string fallback, string[] allowedValues)
            {
                if (string.IsNullOrWhiteSpace(value))
                    return fallback;
                return Array.IndexOf(allowedValues, value) >= 0 ? value : fallback;
            }

            configMenu.Register(
                mod: ModManifest,
                reset: () =>
                {
                    Config = new ModConfig();
                },
                save: () =>
                {
                    Helper.WriteConfig(Config);
                }
            );

            string[] npcRequirementValues =
            {
                ModConfig.NpcRequirementMeet,
                ModConfig.NpcRequirementFriend
            };

            string[] newMessageChanceValues =
            {
                ModConfig.NewMessageChanceDefault,
                ModConfig.NewMessageChanceLow
            };

            configMenu.AddSectionTitle(mod: ModManifest, text: () => "Quick Setup");

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Language",
                tooltip: () => "Choose prompt and response language.",
                getValue: () => string.IsNullOrWhiteSpace(Config.Language) ? "English" : Config.Language,
                setValue: value => Config.Language = string.IsNullOrWhiteSpace(value) ? "English" : value.Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Who can message",
                tooltip: () => "Friendship requirement for NPCs to message you.",
                getValue: () => EnsureAllowedValue(Config.NpcMessageRequirement, ModConfig.NpcRequirementMeet, npcRequirementValues),
                setValue: value => Config.NpcMessageRequirement = value,
                allowedValues: npcRequirementValues
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Disable Daily Message",
                tooltip: () => "If true, the Good Morning button is hidden and the text box is always available.",
                getValue: () => Config.DisableDailyMessage,
                setValue: value => Config.DisableDailyMessage = value
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Show Message Image Tags",
                tooltip: () => "Show tags for attached photos in message tooltips.",
                getValue: () => Config.ShowMessageImageTags,
                setValue: value => Config.ShowMessageImageTags = value
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "ai-settings",
                text: () => "AI Settings",
                tooltip: () => "Configure API keys and models."
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "storage-limits",
                text: () => "Limits & Storage",
                tooltip: () => "Configure message retention and photo limits."
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "misc-settings",
                text: () => "Miscellaneous",
                tooltip: () => "Configure ignored NPCs and messaging chances."
            );

            // AI Settings page
            configMenu.AddPage(mod: ModManifest, pageId: "ai-settings", pageTitle: () => "AI Settings");
            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => "Setup your AI provider credentials here."
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "API Key",
                tooltip: () => "Your OpenAI or Gemini API Key.",
                getValue: () => Config.Key,
                setValue: value => Config.Key = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Model",
                tooltip: () => "Select the AI model to use.",
                getValue: () => EnsureAllowedValue(Config.Model, ModConfig.OpenAIModel_54mini, aiModelValues),
                setValue: value => Config.Model = value,
                allowedValues: aiModelValues
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "NPC Characteristic Detail",
                tooltip: () => "How much background information to send to the AI.",
                getValue: () => EnsureAllowedValue(Config.CharacteristicMode, ModConfig.CharacteristicModeShort, characteristicValues),
                setValue: value => Config.CharacteristicMode = value,
                allowedValues: characteristicValues
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Max Summary Word Count",
                tooltip: () => "Limit on summary length (only used if you use your own API Key).",
                getValue: () => Config.MaxSummaryWordCount,
                setValue: value => Config.MaxSummaryWordCount = Math.Clamp(value, 0, 5000),
                min: 0,
                max: 5000
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Show AI Credit",
                tooltip: () => "Show an AI credit indicator when receiving an AI message.",
                getValue: () => Config.ShowAiCredit,
                setValue: value => Config.ShowAiCredit = value
            );

            // Limits & Storage page
            configMenu.AddPage(mod: ModManifest, pageId: "storage-limits", pageTitle: () => "Limits & Storage");

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Messages per NPC/Player",
                tooltip: () => "Max number of messages to keep per conversation. Old messages are removed when limit is reached.",
                getValue: () => Config.MaxMessage,
                setValue: value => Config.MaxMessage = Math.Clamp(value, 100, 1000),
                min: 100,
                max: 1000
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Shared Photos Limit",
                tooltip: () => "Max photos to keep in the photo_shared folder.",
                getValue: () => Config.PhotoShared,
                setValue: value => Config.PhotoShared = Math.Clamp(value, 10, 500),
                min: 10,
                max: 500
            );

            // Miscellaneous page
            configMenu.AddPage(mod: ModManifest, pageId: "misc-settings", pageTitle: () => "Miscellaneous");

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "New Message Chance",
                tooltip: () => "Adjust chance of getting spontaneous new messages.",
                getValue: () => EnsureAllowedValue(Config.NewMessageChance, ModConfig.NewMessageChanceDefault, newMessageChanceValues),
                setValue: value => Config.NewMessageChance = value,
                allowedValues: newMessageChanceValues
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Ignored NPCs",
                tooltip: () => "Comma-separated list of NPCs who cannot chat.",
                getValue: () => Config.IgnoredNpc ?? string.Empty,
                setValue: value => Config.IgnoredNpc = value ?? string.Empty
            );
        }
    }

    namespace Smartphone.Data
    {
        public interface IGenericModConfigMenuApi
        {
            void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
            void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
            void AddParagraph(IManifest mod, Func<string> text);
            void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
            void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
            void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string> formatValue = null, string fieldId = null);
            void AddNumberOption(IManifest mod, Func<float> getValue, Action<float> setValue, Func<string> name, Func<string> tooltip = null, float? min = null, float? max = null, float? interval = null, Func<float, string> formatValue = null, string fieldId = null);
            void AddPage(IManifest mod, string pageId, Func<string> pageTitle = null);
            void AddPageLink(IManifest mod, string pageId, Func<string> text, Func<string> tooltip = null);
        }
    }
}