using System;
using System.IO;
using System.Linq;
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
                },
                titleScreenOnly: false
            );



            string[] newMessageChanceValues =
            {
                ModConfig.NewMessageChanceDefault,
                ModConfig.NewMessageChanceLow
            };

            configMenu.AddSectionTitle(mod: ModManifest, text: () => GetTranslation("config.section.quick-setup"));

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.language.name"),
                tooltip: () => GetTranslation("config.language.tooltip"),
                getValue: () => string.IsNullOrWhiteSpace(Config.Language) ? "English" : Config.Language,
                setValue: value => Config.Language = string.IsNullOrWhiteSpace(value) ? "English" : value.Trim()
            );

            string npcProfilePath = Path.Combine(Helper.DirectoryPath, "npc_profile");
            string[] themeOptions = Directory.Exists(npcProfilePath)
                ? Directory.GetDirectories(npcProfilePath).Select(Path.GetFileName).Where(name => !string.IsNullOrEmpty(name)).ToArray()!
                : new[] { "vanilla" };
            if (themeOptions.Length == 0)
            {
                themeOptions = new[] { "vanilla" };
            }

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.theme.name"),
                tooltip: () => GetTranslation("config.theme.tooltip"),
                getValue: () => string.IsNullOrWhiteSpace(Config.NpcProfileTheme) ? "vanilla" : Config.NpcProfileTheme,
                setValue: value => Config.NpcProfileTheme = string.IsNullOrWhiteSpace(value) ? "vanilla" : value.Trim(),
                allowedValues: themeOptions
            );


            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => GetTranslation("config.disable-daily-message.name"),
                tooltip: () => GetTranslation("config.disable-daily-message.tooltip"),
                getValue: () => Config.DisableDailyMessage,
                setValue: value => Config.DisableDailyMessage = value
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => GetTranslation("config.show-image-tags.name"),
                tooltip: () => GetTranslation("config.show-image-tags.tooltip"),
                getValue: () => Config.ShowMessageImageTags,
                setValue: value => Config.ShowMessageImageTags = value
            );

            configMenu.AddBoolOption(
                mod: ModManifest,
                name: () => GetTranslation("config.show-ai-credit.name"),
                tooltip: () => GetTranslation("config.show-ai-credit.tooltip"),
                getValue: () => Config.ShowAiCredit,
                setValue: value => Config.ShowAiCredit = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.allowed-npc.name"),
                tooltip: () => GetTranslation("config.allowed-npc.tooltip"),
                getValue: () => Config.AllowedNpc,
                setValue: value => Config.AllowedNpc = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.friendship-requirement.name"),
                tooltip: () => GetTranslation("config.friendship-requirement.tooltip"),
                getValue: () => Config.FriendshipRequirement,
                setValue: value => Config.FriendshipRequirement = value,
                allowedValues: new[] { "Meet", "Friend" }
            );

            // PAGES
            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "ai-settings",
                text: () => GetTranslation("config.page.ai-settings.link"),
                tooltip: () => GetTranslation("config.page.ai-settings.tooltip")
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "storage-limits",
                text: () => GetTranslation("config.page.storage-limits.link"),
                tooltip: () => GetTranslation("config.page.storage-limits.tooltip")
            );

            configMenu.AddPageLink(
                mod: ModManifest,
                pageId: "advance-settings",
                text: () => GetTranslation("config.page.custom-api.link"),
                tooltip: () => GetTranslation("config.page.custom-api.tooltip")
            );




            // AI Settings page
            configMenu.AddPage(mod: ModManifest, pageId: "ai-settings", pageTitle: () => GetTranslation("config.page.ai-settings.title"));
            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => GetTranslation("config.page.ai-settings.desc")
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.key.name"),
                tooltip: () => GetTranslation("config.key.tooltip"),
                getValue: () => Config.Key,
                setValue: value => Config.Key = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.model.name"),
                tooltip: () => GetTranslation("config.model.tooltip"),
                getValue: () => EnsureAllowedValue(Config.Model, ModConfig.OpenAIModel_54mini, aiModelValues),
                setValue: value => Config.Model = value,
                allowedValues: aiModelValues
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.characteristic.name"),
                tooltip: () => GetTranslation("config.characteristic.tooltip"),
                getValue: () => EnsureAllowedValue(Config.CharacteristicMode, ModConfig.CharacteristicModeShort, characteristicValues),
                setValue: value => Config.CharacteristicMode = value,
                allowedValues: characteristicValues
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => GetTranslation("config.max-summary.name"),
                tooltip: () => GetTranslation("config.max-summary.tooltip"),
                getValue: () => Config.MaxSummaryWordCount,
                setValue: value => Config.MaxSummaryWordCount = Math.Clamp(value, 0, 5000),
                min: 0,
                max: 5000
            );

            // Storage and Limits page
            configMenu.AddPage(mod: ModManifest, pageId: "storage-limits", pageTitle: () => GetTranslation("config.page.storage-limits.title"));

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.new-message-chance.name"),
                tooltip: () => GetTranslation("config.new-message-chance.tooltip"),
                getValue: () => EnsureAllowedValue(Config.NewMessageChance, ModConfig.NewMessageChanceDefault, newMessageChanceValues),
                setValue: value => Config.NewMessageChance = value,
                allowedValues: newMessageChanceValues
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => GetTranslation("config.max-message.name"),
                tooltip: () => GetTranslation("config.max-message.tooltip"),
                getValue: () => Config.MaxMessage,
                setValue: value => Config.MaxMessage = Math.Clamp(value, 100, 1000),
                min: 100,
                max: 1000
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => GetTranslation("config.photos-to-keep.name"),
                tooltip: () => GetTranslation("config.photos-to-keep.tooltip"),
                getValue: () => Config.PhotoShared,
                setValue: value => Config.PhotoShared = Math.Clamp(value, 10, 500),
                min: 10,
                max: 500
            );




            configMenu.AddPage(mod: ModManifest, pageId: "advance-settings", pageTitle: () => GetTranslation("config.page.custom-api.title"));
            configMenu.AddParagraph(
                mod: ModManifest,
                text: () => GetTranslation("config.page.custom-api.desc")
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-endpoint.name"),
                tooltip: () => GetTranslation("config.custom-endpoint.tooltip"),
                getValue: () => Config.CustomApiEndpoint,
                setValue: value => Config.CustomApiEndpoint = (value ?? string.Empty).Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-key.name"),
                tooltip: () => GetTranslation("config.custom-key.tooltip"),
                getValue: () => Config.CustomApiKey,
                setValue: value => Config.CustomApiKey = value
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-header.name"),
                tooltip: () => GetTranslation("config.custom-header.tooltip"),
                getValue: () => Config.CustomApiKeyHeader,
                setValue: value => Config.CustomApiKeyHeader = (value ?? "Authorization").Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-prefix.name"),
                tooltip: () => GetTranslation("config.custom-prefix.tooltip"),
                getValue: () => Config.CustomApiKeyPrefix,
                setValue: value => Config.CustomApiKeyPrefix = (value ?? "Bearer").Trim()
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-payload.name"),
                tooltip: () => GetTranslation("config.custom-payload.tooltip"),
                getValue: () => Config.CustomApiPayloadTemplate,
                setValue: value => Config.CustomApiPayloadTemplate = value ?? string.Empty
            );

            configMenu.AddTextOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-path.name"),
                tooltip: () => GetTranslation("config.custom-path.tooltip"),
                getValue: () => Config.CustomApiResponseTextPath,
                setValue: value => Config.CustomApiResponseTextPath = value ?? string.Empty
            );

            configMenu.AddNumberOption(
                mod: ModManifest,
                name: () => GetTranslation("config.custom-timeout.name"),
                tooltip: () => GetTranslation("config.custom-timeout.tooltip"),
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