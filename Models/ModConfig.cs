using System.Collections.Generic;
using StardewModdingAPI;
using StardewValley;

namespace SmartphoneAppMessenger
{
    public class ModConfig
    {


        public const string OpenAIModel_51 = "gpt-5.1";
        public const string OpenAIModel_5mini = "gpt-5-mini";
        public const string OpenAIModel_5nano = "gpt-5-nano";
        public const string OpenAIModel_54mini = "gpt-5.4-mini";
        public const string OpenAIModel_54nano = "gpt-5.4-nano";
        public const string GeminiModel_35Flash = "gemini-3.5-flash";
        public const string GeminiModel_31FlashLite = "gemini-3.1-flash-lite";
        public const string GeminiModel_3FlashPreview = "gemini-3-flash-preview";

        public static readonly List<string> geminiModels = new()
        {
            GeminiModel_35Flash,
            GeminiModel_31FlashLite,
            GeminiModel_3FlashPreview
        };

        public static readonly List<string> openAIModels = new()
        {
            OpenAIModel_51,
            OpenAIModel_5mini,
            OpenAIModel_5nano,
            OpenAIModel_54mini,
            OpenAIModel_54nano
        };

        public const string CharacteristicModeMinimal = "minimal";
        public const string CharacteristicModeShort = "short";
        public const string CharacteristicModeLong = "long";

        public const string NewMessageChanceDefault = "Default";
        public const string NewMessageChanceLow = "Low";


        public string NewMessageChance { get; set; } = NewMessageChanceDefault;
        public string Language { get; set; } = "English";

        public string AllowedNpc { get; set; } = "Abigail, Alex, Caroline, Clint, Demetrius, Dwarf, Elliott, Emily, Evelyn, George, Gus, Haley, Harvey, Jas, Jodi, Kent, Krobus, Leah, Leo, Lewis, Linus, Marnie, Maru, Pam, Penny, Pierre, Robin, Sam, Sandy, Sebastian, Shane, Vincent, Willy, Wizard";
        public string FriendshipRequirement { get; set; } = "Meet";

        public string Key { get; set; } = string.Empty;
        public string Model { get; set; } = OpenAIModel_54mini;


        // Legacy aliases for older config.json files.
        public string OpenAIKey { get => Key; set => Key = value; }
        public string OpenAIModel { get => Model; set => Model = value; }
        public string CharacteristicMode { get; set; } = CharacteristicModeShort;
        public string NpcProfileTheme { get; set; } = "vanilla";


        public int MaxSummaryWordCount { get; set; } = 350;

        public int MaxMessage { get; set; } = 500;
        public int PhotoShared { get; set; } = 100;
        public bool ShowMessageImageTags { get; set; } = false;
        public bool ShowAiCredit { get; set; } = true;

        public bool DisableDailyMessage { get; set; } = false;







        // advance
        public string CustomApiEndpoint { get; set; } = string.Empty;
        public string CustomApiKey { get; set; } = string.Empty;
        public string CustomApiKeyHeader { get; set; } = "Authorization";
        public string CustomApiKeyPrefix { get; set; } = "Bearer";
        public string CustomApiPayloadTemplate { get; set; } = "{\"model\":\"MODEL_HERE\",\"messages\":[{\"role\":\"system\",\"content\":\"SYSTEM_INPUT_HERE\"},{\"role\":\"user\",\"content\":\"USER_INPUT_HERE\"}]}";
        public string CustomApiResponseTextPath { get; set; } = "choices[0].message.content";
        public int CustomApiTimeoutSeconds { get; set; } = 45;
    }
}
