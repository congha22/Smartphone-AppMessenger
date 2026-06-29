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
                    try
                    {
                        Instance.LoadIcons();
                        Instance.RegisterMessengerApp();
                    }
                    catch { }
                }
            );



            string[] newMessageChanceValues =
            {
                ModConfig.NewMessageChanceDefault,
                ModConfig.NewMessageChanceLow
            };

            configMenu.AddSectionTitle(mod: ModManifest, text: () => "Quick Setup");

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Language",
                tooltip: () => "Enter your prefered language and alphabet. However English is the best supported.",
                getValue: () => string.IsNullOrWhiteSpace(Config.Language) ? "English" : Config.Language,
                setValue: value => Config.Language = string.IsNullOrWhiteSpace(value) ? "English" : value.Trim()
            );


            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Disable Daily Message",
                tooltip: () => "If enabled, the `Good morning...` option will be disabled.\nYou should keep this option False which overtime will help the AI understand what is lore of the character.",
                getValue: () => Config.DisableDailyMessage,
                setValue: value => Config.DisableDailyMessage = value
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Show Message Image Tags",
                tooltip: () => "Shows attached image tags when hovering photo bubbles in the Text app.",
                getValue: () => Config.ShowMessageImageTags,
                setValue: value => Config.ShowMessageImageTags = value
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => "Show AI Credit",
                tooltip: () => "You have a limited number of usage of the AI features each day when no key is provided.\nThis option shows how many usages you have left. Check it at Messages -> NPC -> AI Credit.",
                getValue: () => Config.ShowAiCredit,
                setValue: value => Config.ShowAiCredit = value
            );

            // PAGES
            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "ai-settings",
                text: () => "AI Settings",
                tooltip: () => "API key, model choice, and limits setting."
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "storage-limits",
                text: () => "Storage and Limits",
                tooltip: () => "Storage settings."
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "advance-settings",
                text: () => "Advance - Custom API",
                tooltip: () => "Call to your own API endpoint. Check out Forum for instruction."
            );




            // AI Settings page
            configMenu.AddPage(mod: ModManifest, pageId: "ai-settings", pageTitle: () => "AI Settings");
            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => "These settings are only effective when an API key is provided. You can use your own OpenAI or Gemini key. When using custom API, key and model options here have no effect."
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Key",
                tooltip: () => "Use your own OpenAI or Gemini key to remove shared usage limits.\nOpenAI key: https://platform.openai.com/account/api-keys\nGemini key: https://aistudio.google.com/app/apikey\nRestart the game after changing this value.",
                getValue: () => Config.Key,
                setValue: value => Config.Key = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Model",
                tooltip: () => "Chooses the model.\nOf course if you using OpenAI key, you should choose OpenAI model and vice versa for Gemini key.",
                getValue: () => EnsureAllowedValue(Config.Model, ModConfig.OpenAIModel_54mini, aiModelValues),
                setValue: value => Config.Model = value,
                allowedValues: aiModelValues
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "NPC characteristic detail",
                tooltip: () => "NPC characteristic give the AI a background for each NPC during the chat and the post generation.\nHigher detail improves quality but uses more tokens per NPC.",
                getValue: () => EnsureAllowedValue(Config.CharacteristicMode, ModConfig.CharacteristicModeShort, characteristicValues),
                setValue: value => Config.CharacteristicMode = value,
                allowedValues: characteristicValues
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Summary max words",
                tooltip: () => "Conversation are summarized daily so the AI knows what the previous conversation is about.\nHigher limit means AI will `remember` more details but will use more tokens.\nMemory saved at \"Userdata\\SaveFolder\\summary\".",
                getValue: () => Config.MaxSummaryWordCount,
                setValue: value => Config.MaxSummaryWordCount = Math.Clamp(value, 0, 5000),
                min: 0,
                max: 5000
            );

            // Storage and Limits page
            configMenu.AddPage(mod: ModManifest, pageId: "storage-limits", pageTitle: () => "Storage and Limits");

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "New message chance",
                tooltip: () => "Chance to receive a new message from NPCs.",
                getValue: () => EnsureAllowedValue(Config.NewMessageChance, ModConfig.NewMessageChanceDefault, newMessageChanceValues),
                setValue: value => Config.NewMessageChance = value,
                allowedValues: newMessageChanceValues
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Messages per NPC",
                tooltip: () => "Older text messages are removed first when this limit is exceeded.",
                getValue: () => Config.MaxMessage,
                setValue: value => Config.MaxMessage = Math.Clamp(value, 100, 1000),
                min: 100,
                max: 1000
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Player photos to keep",
                tooltip: () => "Older player photos are deleted first.",
                getValue: () => Config.PhotoShared,
                setValue: value => Config.PhotoShared = Math.Clamp(value, 10, 500),
                min: 10,
                max: 500
            );




            configMenu.AddPage(mod: ModManifest, pageId: "advance-settings", pageTitle: () => "Advance - Custom API");
            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => "When using this advance setting, key and model selection on regular AI setting will be override. See mod page for instruction how to use these settings."
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Custom API Endpoint",
                tooltip: () => "URL endpoint for a custom API (leave empty to use default providers).",
                getValue: () => Config.CustomApiEndpoint,
                setValue: value => Config.CustomApiEndpoint = (value ?? string.Empty).Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Custom API Key",
                tooltip: () => "Key for your custom API endpoint.",
                getValue: () => Config.CustomApiKey,
                setValue: value => Config.CustomApiKey = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Custom API Key Header",
                tooltip: () => "HTTP Header name for the API Key.",
                getValue: () => Config.CustomApiKeyHeader,
                setValue: value => Config.CustomApiKeyHeader = (value ?? "Authorization").Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Custom API Key Prefix",
                tooltip: () => "Prefix for the key (e.g. Bearer).",
                getValue: () => Config.CustomApiKeyPrefix,
                setValue: value => Config.CustomApiKeyPrefix = (value ?? "Bearer").Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Custom API Payload Template",
                tooltip: () => "JSON payload template.",
                getValue: () => Config.CustomApiPayloadTemplate,
                setValue: value => Config.CustomApiPayloadTemplate = value ?? string.Empty
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => "Custom API Response JSON Path",
                tooltip: () => "JSON path to extract response text.",
                getValue: () => Config.CustomApiResponseTextPath,
                setValue: value => Config.CustomApiResponseTextPath = value ?? string.Empty
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => "Custom API Timeout Seconds",
                tooltip: () => "Request timeout in seconds.",
                getValue: () => Config.CustomApiTimeoutSeconds,
                setValue: value => Config.CustomApiTimeoutSeconds = Math.Clamp(value, 5, 300),
                min: 5,
                max: 300
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