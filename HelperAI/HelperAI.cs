using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Extensions;
using StardewValley.Menus;

namespace SmartphoneAppMessenger
{
    public partial class ModEntry
    {

        private const string AiProviderOpenAi = "openai";
        private const string AiProviderGemini = "gemini";
        private const string AiProviderCustom = "custom";
        private const string GeminiThinkingLevelMinimal = "MINIMAL";
        private const string CustomPayloadTokenModel = "MODEL_HERE";
        private const string CustomPayloadTokenInput = "INPUT_HERE";
        private const string CustomPayloadTokenSystemInput = "SYSTEM_INPUT_HERE";
        private const string CustomPayloadTokenUserInput = "USER_INPUT_HERE";
        private const string CustomPayloadTokenSystemMessage = "SYSTEM_MESSAGE_HERE";
        private const string CustomPayloadTokenUserMessage = "USER_MESSAGE_HERE";
        private const string CustomDefaultPayloadTemplate = "{\"model\":\"MODEL_HERE\",\"messages\":[{\"role\":\"system\",\"content\":\"SYSTEM_INPUT_HERE\"},{\"role\":\"user\",\"content\":\"USER_INPUT_HERE\"}]}";
        private static readonly string[] CustomResponseFallbackPaths =
        {
            "choices[0].message.content",
            "choices[0].text",
            "output_text",
            "output[0].content[0].text",
            "candidates[0].content.parts[0].text",
            "text"
        };

        public static bool IsMaxedLimit = false;
        public static bool IsReducedQuality = false;
        public static int totalFailedCheck = 0;


        // gpt-5.4 variant support reasoning effor: none, low, medium, high, xhigh
        // gpt-5 variant support reasoning effort: minimal, low, medium, high
        public static string chatModel = "gpt-5.4-mini";
        public static object chatReasoningEffort = new { effort = "none" };
        public static string chatGeminiThinkingLevel = GeminiThinkingLevelMinimal;


        public static string summaryModel = "gpt-5.4-mini";
        public static object summaryReasoningEffort = new { effort = "low" };
        public static string summaryGeminiThinkingLevel = GeminiThinkingLevelMinimal;

        private static bool HasUserProvidedAiKey()
        {
            return !string.IsNullOrWhiteSpace(Config?.Key);
        }

        private static bool HasCustomProviderConfigured()
        {
            return !string.IsNullOrWhiteSpace((Config?.CustomApiEndpoint ?? string.Empty).Trim());
        }

        internal static bool IsBringYourOwnAiProviderMode()
        {
            return HasUserProvidedAiKey() || HasCustomProviderConfigured();
        }

        internal static bool IsSharedAiProviderMode()
        {
            return !IsBringYourOwnAiProviderMode();
        }

        private static string ResolveAiRuntimeKey(string provider)
        {
            if (string.Equals(provider, AiProviderCustom, StringComparison.OrdinalIgnoreCase))
                return (Config?.CustomApiKey ?? string.Empty).Trim();

            if (HasUserProvidedAiKey())
                return (Config.Key ?? string.Empty).Trim();

            return EmbeddedAiSecrets.SharedOpenAiRuntimeKey ?? string.Empty;
        }

        private static string ResolveOpenAiAdminKey()
        {
            return EmbeddedAiSecrets.SharedOpenAiAdminKey ?? string.Empty;
        }

        private static bool IsGeminiModel(string? model)
        {
            return !string.IsNullOrWhiteSpace(model)
                && ModConfig.geminiModels.Contains(model.Trim(), StringComparer.OrdinalIgnoreCase);
        }

        private static string GetProviderForModel(string? model)
        {
            if (HasCustomProviderConfigured())
                return AiProviderCustom;

            return IsGeminiModel(model) ? AiProviderGemini : AiProviderOpenAi;
        }

        private static string ResolveAiInstructionLanguage()
        {
            string configuredLanguage = (Config?.Language ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(configuredLanguage) ? "English" : configuredLanguage;
        }

        private static string AppendLanguageInstruction(string instructionText)
        {
            string normalizedInstruction = (instructionText ?? string.Empty).TrimEnd();
            string language = ResolveAiInstructionLanguage();

            if (string.Equals(language, "english", StringComparison.OrdinalIgnoreCase))
                return normalizedInstruction;

            return $"{normalizedInstruction}\nUse {language} language and alphabet";
        }

        private static string ResolveCustomApiEndpoint()
        {
            return (Config?.CustomApiEndpoint ?? string.Empty).Trim();
        }

        private static string ResolveCustomApiPayloadTemplate()
        {
            string configuredTemplate = (Config?.CustomApiPayloadTemplate ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(configuredTemplate)
                ? CustomDefaultPayloadTemplate
                : configuredTemplate;
        }

        private static string ResolveCustomApiResponseTextPath()
        {
            return (Config?.CustomApiResponseTextPath ?? string.Empty).Trim();
        }

        private static int ResolveCustomApiTimeoutSeconds()
        {
            return Math.Clamp(Config?.CustomApiTimeoutSeconds ?? 45, 5, 300);
        }

        private static bool TryResolveCustomApiEndpointUri(out Uri endpointUri, out string errorMessage)
        {
            endpointUri = null!;
            errorMessage = string.Empty;

            string endpoint = ResolveCustomApiEndpoint();
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                errorMessage = "Custom API endpoint is empty.";
                return false;
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? parsedUri) || parsedUri == null)
            {
                errorMessage = $"Custom API endpoint '{endpoint}' is not a valid absolute URL.";
                return false;
            }

            if (string.Equals(parsedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                endpointUri = parsedUri;
                return true;
            }

            if (!string.Equals(parsedUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = $"Custom API endpoint scheme '{parsedUri.Scheme}' is not supported. Use HTTPS, or HTTP only for localhost.";
                return false;
            }

            endpointUri = parsedUri;
            return true;
        }

        private static bool IsValidHttpHeaderName(string headerName)
        {
            if (string.IsNullOrWhiteSpace(headerName))
                return false;

            foreach (char character in headerName)
            {
                bool isLetterOrDigit = char.IsLetterOrDigit(character);
                if (!isLetterOrDigit && character != '-')
                    return false;
            }

            return true;
        }

        private static string BuildCustomAuthHeaderValue(string apiKey)
        {
            string normalizedApiKey = (apiKey ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedApiKey))
                return string.Empty;

            string prefix = (Config?.CustomApiKeyPrefix ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(prefix))
                return normalizedApiKey;

            return $"{prefix} {normalizedApiKey}";
        }

        private static string BuildCombinedCustomInputPrompt(string systemMessage, string userMessage)
        {
            string normalizedSystemMessage = (systemMessage ?? string.Empty).Trim();
            string normalizedUserMessage = (userMessage ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(normalizedSystemMessage))
                return normalizedUserMessage;

            if (string.IsNullOrWhiteSpace(normalizedUserMessage))
                return normalizedSystemMessage;

            return $"SYSTEM:\n{normalizedSystemMessage}\n\nUSER:\n{normalizedUserMessage}";
        }

        private static bool ContainsCustomPayloadInputToken(string payloadTemplate)
        {
            string template = payloadTemplate ?? string.Empty;

            return template.Contains(CustomPayloadTokenInput, StringComparison.Ordinal)
                || template.Contains(CustomPayloadTokenSystemInput, StringComparison.Ordinal)
                || template.Contains(CustomPayloadTokenUserInput, StringComparison.Ordinal)
                || template.Contains(CustomPayloadTokenSystemMessage, StringComparison.Ordinal)
                || template.Contains(CustomPayloadTokenUserMessage, StringComparison.Ordinal)
                || template.Contains($"{{{{{CustomPayloadTokenInput}}}}}", StringComparison.Ordinal)
                || template.Contains($"{{{{{CustomPayloadTokenSystemInput}}}}}", StringComparison.Ordinal)
                || template.Contains($"{{{{{CustomPayloadTokenUserInput}}}}}", StringComparison.Ordinal)
                || template.Contains($"{{{{{CustomPayloadTokenSystemMessage}}}}}", StringComparison.Ordinal)
                || template.Contains($"{{{{{CustomPayloadTokenUserMessage}}}}}", StringComparison.Ordinal);
        }

        private static string ReplaceTemplateToken(string source, string token, string replacementValue)
        {
            string value = replacementValue ?? string.Empty;
            return (source ?? string.Empty)
                .Replace(token, value, StringComparison.Ordinal)
                .Replace($"{{{{{token}}}}}", value, StringComparison.Ordinal);
        }

        private static string ReplaceCustomPayloadTokens(string source, string model, string systemMessage, string userMessage, string combinedInput)
        {
            string resolvedSource = source ?? string.Empty;
            resolvedSource = ReplaceTemplateToken(resolvedSource, CustomPayloadTokenModel, model ?? string.Empty);
            resolvedSource = ReplaceTemplateToken(resolvedSource, CustomPayloadTokenSystemInput, systemMessage ?? string.Empty);
            resolvedSource = ReplaceTemplateToken(resolvedSource, CustomPayloadTokenUserInput, userMessage ?? string.Empty);
            resolvedSource = ReplaceTemplateToken(resolvedSource, CustomPayloadTokenSystemMessage, systemMessage ?? string.Empty);
            resolvedSource = ReplaceTemplateToken(resolvedSource, CustomPayloadTokenUserMessage, userMessage ?? string.Empty);
            resolvedSource = ReplaceTemplateToken(resolvedSource, CustomPayloadTokenInput, combinedInput ?? string.Empty);
            return resolvedSource;
        }

        private static void ReplaceCustomPayloadTokensInPlace(JToken token, string model, string systemMessage, string userMessage, string combinedInput)
        {
            if (token == null)
                return;

            if (token.Type == JTokenType.Object)
            {
                foreach (JProperty property in ((JObject)token).Properties())
                    ReplaceCustomPayloadTokensInPlace(property.Value, model, systemMessage, userMessage, combinedInput);

                return;
            }

            if (token.Type == JTokenType.Array)
            {
                foreach (JToken child in (JArray)token)
                    ReplaceCustomPayloadTokensInPlace(child, model, systemMessage, userMessage, combinedInput);

                return;
            }

            if (token.Type == JTokenType.String && token is JValue value)
            {
                value.Value = ReplaceCustomPayloadTokens(value.Value?.ToString() ?? string.Empty, model, systemMessage, userMessage, combinedInput);
            }
        }

        private static bool TryBuildCustomApiPayload(string model, string systemMessage, string userMessage, out string payloadJson, out string errorMessage)
        {
            payloadJson = string.Empty;
            errorMessage = string.Empty;

            string payloadTemplate = ResolveCustomApiPayloadTemplate();
            if (!ContainsCustomPayloadInputToken(payloadTemplate))
            {
                errorMessage = "Custom payload template must include INPUT_HERE, SYSTEM_INPUT_HERE, or USER_INPUT_HERE placeholders.";
                return false;
            }

            try
            {
                JToken payloadToken = JToken.Parse(payloadTemplate);
                string combinedInput = BuildCombinedCustomInputPrompt(systemMessage, userMessage);
                ReplaceCustomPayloadTokensInPlace(payloadToken, model, systemMessage, userMessage, combinedInput);

                payloadJson = payloadToken.ToString(Formatting.None);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Custom payload template is invalid JSON: {ex.Message}";
                return false;
            }
        }

        private static async Task<HttpResponseMessage?> SendCustomProviderRequestAsync(HttpClient httpClient, string model, string systemMessage, string userMessage, string runtimeKey, string operationName)
        {
            if (httpClient == null)
                return null;

            if (!TryResolveCustomApiEndpointUri(out Uri endpointUri, out string endpointError))
            {
                SMonitor.Log($"Unable to call custom AI endpoint for {operationName}. {endpointError}", LogLevel.Warn);
                return null;
            }

            if (!TryBuildCustomApiPayload(model, systemMessage, userMessage, out string payloadJson, out string payloadError))
            {
                SMonitor.Log($"Unable to build custom AI payload for {operationName}. {payloadError}", LogLevel.Warn);
                return null;
            }

            string headerName = (Config?.CustomApiKeyHeader ?? string.Empty).Trim();
            string headerValue = BuildCustomAuthHeaderValue(runtimeKey);

            if (!string.IsNullOrWhiteSpace(headerValue))
            {
                if (string.IsNullOrWhiteSpace(headerName))
                    headerName = "Authorization";

                if (!IsValidHttpHeaderName(headerName))
                {
                    SMonitor.Log($"Unable to call custom AI endpoint for {operationName}. Header name '{headerName}' is invalid.", LogLevel.Warn);
                    return null;
                }

                if (!httpClient.DefaultRequestHeaders.TryAddWithoutValidation(headerName, headerValue))
                {
                    SMonitor.Log($"Unable to call custom AI endpoint for {operationName}. Failed to attach header '{headerName}'.", LogLevel.Warn);
                    return null;
                }
            }

            httpClient.Timeout = TimeSpan.FromSeconds(ResolveCustomApiTimeoutSeconds());

            var httpContent = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            return await httpClient.PostAsync(endpointUri, httpContent);
        }

        internal static void HandleAiModelSettingTimeChanged(int newTime)
        {
            if (IsAiTemporarilyDisabledForPhoneInactivity())
                return;

            if (IsBringYourOwnAiProviderMode())
            {
                chatModel = Config.Model;
                summaryModel = Config.Model;

                string provider = GetProviderForModel(chatModel);

                if (string.Equals(provider, AiProviderGemini, StringComparison.OrdinalIgnoreCase))
                {
                    chatGeminiThinkingLevel = GeminiThinkingLevelMinimal;
                    summaryGeminiThinkingLevel = GeminiThinkingLevelMinimal;
                    chatReasoningEffort = new { effort = "minimal" };
                    summaryReasoningEffort = new { effort = "minimal" };
                    return;
                }

                if (string.Equals(provider, AiProviderCustom, StringComparison.OrdinalIgnoreCase))
                {
                    chatGeminiThinkingLevel = GeminiThinkingLevelMinimal;
                    summaryGeminiThinkingLevel = GeminiThinkingLevelMinimal;
                    chatReasoningEffort = new { effort = "none" };
                    summaryReasoningEffort = new { effort = "none" };
                    IsReducedQuality = false;
                    IsMaxedLimit = false;
                    totalFailedCheck = 0;
                    return;
                }

                switch (chatModel)
                {
                    case ModConfig.OpenAIModel_51:
                        chatReasoningEffort = new { effort = "none" };
                        summaryReasoningEffort = new { effort = "none" };
                        break;
                    case ModConfig.OpenAIModel_5mini:
                        chatReasoningEffort = new { effort = "minimal" };
                        summaryReasoningEffort = new { effort = "minimal" };
                        break;
                    case ModConfig.OpenAIModel_5nano:
                        chatReasoningEffort = new { effort = "minimal" };
                        summaryReasoningEffort = new { effort = "minimal" };
                        break;
                    case ModConfig.OpenAIModel_54mini:
                        chatReasoningEffort = new { effort = "none" };
                        summaryReasoningEffort = new { effort = "none" };
                        break;
                    case ModConfig.OpenAIModel_54nano:
                        chatReasoningEffort = new { effort = "none" };
                        summaryReasoningEffort = new { effort = "none" };
                        break;
                    default:
                        chatReasoningEffort = new { effort = "minimal" };
                        summaryReasoningEffort = new { effort = "minimal" };
                        break;
                }
                return;
            }


            if (IsSharedAiProviderMode() && newTime % 300 == 0)
            {
                Task.Run(async () =>
                {
                    var (premium, regular) = await GetOpenAIUsage();
                    if (regular == -1 || premium == -1)
                    {
                        totalFailedCheck += 1;
                        if (totalFailedCheck >= 3)
                        {
                            IsMaxedLimit = true;
                            iSmartphoneApi?.SendSmartphoneNotification("=== Smartphone ===^^Failed to check AI usage for 3 times in a row, AI usage is temporarily disabled.^^Try restart your game and check mod page for support. HaPyke!");
                            return;
                        }
                    }


                    if (regular > 25000000)
                    {
                        IsMaxedLimit = true;
                        iSmartphoneApi?.SendSmartphoneNotification("=== Smartphone ===^^AI usage reached its limit. Chat and StardewSocial are temporarily disabled.^^This will be reset the next day in timezone UTC+0. HaPyke!");
                        return;
                    }


                    IsMaxedLimit = false;
                    if (regular > 15000000 && chatModel != "gpt-5-nano")
                    {
                        chatModel = "gpt-5-nano";
                        chatReasoningEffort = new { effort = "minimal" };

                        summaryModel = "gpt-5-nano";
                        summaryReasoningEffort = new { effort = "minimal" };

                        IsReducedQuality = true;

                        iSmartphoneApi?.SendSmartphoneNotification("=== Smartphone ===^^Usage is very high today, AI quality is temporarily downgraded so I won't go bankrupt lol.^^This will be reset the next day in timezone UTC+0. HaPyke!");
                    }
                    else if (regular > 10000000)
                    {
                        IsReducedQuality = false;
                        chatModel = "gpt-5-mini";
                        chatReasoningEffort = new { effort = "minimal" };

                        summaryModel = "gpt-5-mini";
                        summaryReasoningEffort = new { effort = "minimal" };
                    }
                    else
                    {
                        IsReducedQuality = false;
                        chatModel = "gpt-5.4-mini";
                        chatReasoningEffort = new { effort = "none" };

                        summaryModel = "gpt-5.4-mini";
                        summaryReasoningEffort = new { effort = "none" };
                    }
                });
            }
        }


        private static string GetNpcCharacteristicForPrompt(NPC npc, bool getMinimal = false)
        {
            if (npc is null)
                return string.Empty;

            string npcAge = npc.Age == 0 ? "adult" : npc.Age == 1 ? "teens" : npc.Age == 2 ? "child" : "adult";
            string npcManner = npc.Manners == 0 ? "a typical neutral manner" : npc.Manners == 1 ? "a polite and courteous manner" : npc.Manners == 2 ? "a distant and reserved manner" : "a typical neutral manner";
            string npcSocial = npc.SocialAnxiety == 0 ? "an outgoing person" : npc.SocialAnxiety == 1 ? "a little shy person" : "neither too outgoing nor shy";

            string npcCharacteristic = $" {npc.Name} is {npcAge}, {npcManner}, and is {npcSocial}";

            if (getMinimal && IsSharedAiProviderMode())
                return npcCharacteristic;

            // CUSTOM CHARACTERISTIC OVERRIDE
            if (IsBringYourOwnAiProviderMode())
            {
                if (Config.CharacteristicMode == ModConfig.CharacteristicModeLong && NpcCharacteristicsLong.TryGetValue(npc.Name, out string? customCharacteristicLong) && !string.IsNullOrWhiteSpace(customCharacteristicLong) && !getMinimal)
                {
                    npcCharacteristic = customCharacteristicLong;
                }
                else if (Config.CharacteristicMode == ModConfig.CharacteristicModeShort && NpcCharacteristicsShort.TryGetValue(npc.Name, out string? customCharacteristic) && !string.IsNullOrWhiteSpace(customCharacteristic) && !getMinimal)
                {
                    npcCharacteristic = customCharacteristic;
                }
                else if (NpcCharacteristicsMinimal.TryGetValue(npc.Name, out string? customCharacteristicMinimal) && !string.IsNullOrWhiteSpace(customCharacteristicMinimal)
                && (Config.CharacteristicMode == ModConfig.CharacteristicModeMinimal || getMinimal))
                {
                    npcCharacteristic = customCharacteristicMinimal;
                }
                return npcCharacteristic.Trim();
            }
            // DEFAULT CHARACTERISTIC
            else
            {
                if (NpcCharacteristicsShort.TryGetValue(npc.Name, out string? customCharacteristic) && !string.IsNullOrWhiteSpace(customCharacteristic) && !IsReducedQuality)
                {
                    npcCharacteristic = customCharacteristic;
                }
                else if (NpcCharacteristicsMinimal.TryGetValue(npc.Name, out string? customCharacteristicMinimal) && !string.IsNullOrWhiteSpace(customCharacteristicMinimal))
                {
                    npcCharacteristic = customCharacteristicMinimal;
                }

                return npcCharacteristic.Trim();
            }
        }

        /// <summary>Call AI provider to generate a response</summary>
        public static async Task<string> SendMessageToAssistant(string npcName, string text = "", string type = "response")
        {

            SMonitor.Log(text, LogLevel.Error);
            NPC npc = Game1.getCharacterFromName(npcName);
            if (npc is null || IsMaxedLimit || IsAiTemporarilyDisabledForPhoneInactivity())
            {
                return "SYSTEM: ---Got an error---";
            }

            string provider = GetProviderForModel(chatModel);
            string key = ResolveAiRuntimeKey(provider);
            if (!string.Equals(provider, AiProviderCustom, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(key))
            {
                SMonitor.Log("AI key is missing. Add a Key in config or provide shared OpenAI keys in .env before building.", LogLevel.Warn);
                return "SYSTEM: ---Got an error---";
            }

            string user = type == "response"
                ? ModEntry.BuildResponseConversationUserInput(npc, text)
                : text;
            string system = GetSystemMessage(npc, type, allowToolCalling: !string.Equals(provider, AiProviderCustom, StringComparison.OrdinalIgnoreCase));

            SMonitor.Log(system, LogLevel.Error);

            string responseMessage = "";
            using (var httpClient = new HttpClient())
            {
                HttpResponseMessage httpResponse;

                if (provider == AiProviderGemini)
                {
                    string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(chatModel)}:generateContent";
                    httpClient.DefaultRequestHeaders.Add("X-goog-api-key", key);

                    var requestBody = new Dictionary<string, object>
                    {
                        {
                            "systemInstruction", new
                            {
                                parts = new object[]
                                {
                                    new { text = system }
                                }
                            }
                        },
                        {
                            "contents", new object[]
                            {
                                new
                                {
                                    role = "user",
                                    parts = new object[]
                                    {
                                        new { text = user }
                                    }
                                }
                            }
                        },
                        {
                            "generationConfig", new
                            {
                                thinkingConfig = new
                                {
                                    thinkingLevel = chatGeminiThinkingLevel
                                }
                            }
                        }
                    };

                    if (type == "response")
                    {
                        var tools = GetToolList(npc);
                        if (tools.Length > 0)
                        {
                            object[] functionDeclarations = BuildGeminiFunctionDeclarations(tools);
                            if (functionDeclarations.Length > 0)
                            {
                                requestBody.Add("tools", new object[]
                                {
                                    new
                                    {
                                        functionDeclarations = functionDeclarations
                                    }
                                });

                                requestBody.Add("toolConfig", new
                                {
                                    functionCallingConfig = new
                                    {
                                        mode = "AUTO"
                                    }
                                });
                            }
                        }
                    }

                    var jsonRequest = JsonConvert.SerializeObject(requestBody);
                    var httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    httpResponse = await httpClient.PostAsync(endpoint, httpContent);
                }
                else if (provider == AiProviderCustom)
                {
                    HttpResponseMessage? customResponse = await SendCustomProviderRequestAsync(
                        httpClient,
                        chatModel,
                        system,
                        user,
                        key,
                        "SendMessageToAssistant");

                    if (customResponse == null)
                        return "SYSTEM: ---Got an error---";

                    httpResponse = customResponse;
                }
                else
                {
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

                    var requestBody = new Dictionary<string, object>
                    {
                        { "model", chatModel },
                        { "max_output_tokens", 100 },
                        {
                            "input", new object[]
                            {
                                new
                                {
                                    role = "developer",
                                    content = new object[]
                                    {
                                        new { type = "input_text", text = system }
                                    }
                                },
                                new
                                {
                                    role = "user",
                                    content = new object[]
                                    {
                                        new { type = "input_text", text = user }
                                    }
                                }
                            }
                        },
                        { "text", new { format = new { type = "text" }, verbosity = "low" } },
                        { "reasoning", chatReasoningEffort }
                    };

                    if (type == "response")
                    {
                        var tools = GetToolList(npc);
                        if (tools.Length > 0)
                        {
                            requestBody.Add("tools", tools);
                            requestBody.Add("tool_choice", "auto");
                        }
                    }

                    var jsonRequest = JsonConvert.SerializeObject(requestBody);
                    var httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    httpResponse = await httpClient.PostAsync("https://api.openai.com/v1/responses", httpContent);
                }

                if (httpResponse.IsSuccessStatusCode)
                {
                    var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
                    RegisterSuccessfulAiCall();

                    JToken parsedToken;
                    try
                    {
                        parsedToken = JToken.Parse(jsonResponse);
                    }
                    catch (JsonReaderException)
                    {
                        if (provider == AiProviderCustom)
                        {
                            responseMessage = (jsonResponse ?? string.Empty).Trim();
                            if (responseMessage.StartsWith($"{npc.Name}:", StringComparison.OrdinalIgnoreCase))
                                responseMessage = responseMessage.Substring(npc.Name.Length + 1).TrimStart();
                            return responseMessage;
                        }

                        SMonitor.Log($"Unable to parse AI response payload from {provider}: {jsonResponse}", LogLevel.Trace);
                        return "SYSTEM: ---Got an error---";
                    }

                    JArray toolCalls = new JArray();

                    if (provider == AiProviderGemini)
                    {
                        JObject geminiJson = parsedToken as JObject ?? new JObject();
                        toolCalls = GetGeminiResponseFunctionCalls(geminiJson);
                        responseMessage = GetGeminiResponseOutputText(geminiJson);
                    }
                    else if (provider == AiProviderOpenAi)
                    {
                        JObject openAiJson = parsedToken as JObject ?? new JObject();
                        toolCalls = GetResponseFunctionCalls(openAiJson);
                        responseMessage = GetResponseOutputText(openAiJson);
                    }
                    else
                    {
                        responseMessage = GetCustomResponseOutputText(parsedToken);
                        if (string.IsNullOrWhiteSpace(responseMessage))
                            responseMessage = (jsonResponse ?? string.Empty).Trim();
                    }


                    SMonitor.Log(jsonResponse.ToString(), LogLevel.Error);
                    SMonitor.Log("system-----", LogLevel.Error);
                    SMonitor.Log(system, LogLevel.Error);
                    SMonitor.Log("user-----", LogLevel.Error);
                    SMonitor.Log(user, LogLevel.Error);
                    SMonitor.Log("response-----", LogLevel.Error);
                    SMonitor.Log(responseMessage, LogLevel.Error);
                    SMonitor.Log("\n\n", LogLevel.Error);

                    if (toolCalls.Count > 0)
                    {
                        foreach (var call in toolCalls)
                        {
                            string functionName = call["name"]?.ToString() ?? string.Empty;
                            string argumentsJson = call["arguments"]?.ToString() ?? "{}";

                            if (functionName == "schedule_event")
                            {
                                JObject args;
                                try
                                {
                                    args = JObject.Parse(argumentsJson);
                                }
                                catch (Exception ex)
                                {
                                    SMonitor.Log($"Unable to parse schedule_event arguments: {ex}", LogLevel.Trace);
                                    continue;
                                }

                                var eventNpcName = args["npc"]?.ToString();
                                var eventType = args["event_type"]?.ToString();
                                var textResponse = args["npc_response"]?.ToString();

                                if (string.IsNullOrWhiteSpace(eventNpcName)
                                    || string.IsNullOrWhiteSpace(eventType))
                                {
                                    continue;
                                }

                                if (!TryGetRegisteredUnlimitedEvent(eventType, out var registeredEvent) || registeredEvent == null)
                                {
                                    SMonitor.Log($"schedule_event ignored because event type '{eventType}' is not registered.", LogLevel.Warn);
                                    continue;
                                }

                                string npcDisplayName = string.IsNullOrWhiteSpace(npc.displayName)
                                    ? npc.Name
                                    : npc.displayName;

                                Game1.activeClickableMenu = new ConfirmationDialog(
                                    $"Schedule {registeredEvent.EventType} event with {npcDisplayName}?",
                                    onConfirm: (Farmer who) =>
                                    {
                                        Game1.activeClickableMenu = null;
                                        iUnlimitedEventExpansionApi.OpenScheduleEventTimeMenu(
                                            eventNpcName.Trim(),
                                            registeredEvent.EventType,
                                            textResponse);

                                    },
                                    onCancel: (Farmer who) =>
                                    {
                                        Game1.activeClickableMenu = null;
                                    }
                                );
                            }
                        }
                    }

                    if (responseMessage.StartsWith($"{npc.Name}:"))
                        responseMessage = responseMessage.Substring(npc.Name.Length + 1).TrimStart();
                    return responseMessage;
                }
                else
                {
                    // Get the status code
                    var statusCode = (int)httpResponse.StatusCode; // Convert to int for switch
                    string errorMessage = "Check for mod update";
                    switch (statusCode)
                    {
                        case 400:
                            errorMessage = "Bad request sent to AI provider.";
                            break;
                        case 403:
                            errorMessage = "Country, region, or territory not supported.";
                            break;
                        case 429:
                            errorMessage = "Please try again in a few minutes. If not work, then total AI usage for all players has passed the limit set by OpenAI. This will be reset the next day in timezone UTC+0";
                            break;
                        case 500:
                            errorMessage = "Server Error: The server had an issue while processing your request. Please try again.";
                            break;
                        case 503:
                            errorMessage = "Server Overload: The server is experiencing high traffic. Please try again later.";
                            break;
                    }

                    SMonitor.Log($"Unable to receive AI content from {provider}. {statusCode}, {errorMessage}\n\n", LogLevel.Error);
                    // SMonitor.Log(httpResponse.ToString(), LogLevel.Error);
                    return "SYSTEM: ---Got an error---";
                }
            }
        }

        private const string PlayerPhotoPrefix = "PlayerPhoto:";
        private const string PlayerPhotoTagPrefix = "PlayerPhotoTag:";
        private const string NpcPhotoPrefix = "NpcPhoto:";
        private const string NpcPhotoTagPrefix = "NpcPhotoTag:";

        private static string BuildConversationPreviewForAi(IEnumerable<string> rawMessages, string npcName, int maxLines)
        {
            List<string> sanitizedMessages = SanitizeConversationMessagesForAi(rawMessages, npcName);

            int safeMaxLines = Math.Max(1, maxLines);
            int skipCount = Math.Max(0, sanitizedMessages.Count - safeMaxLines);
            int takeCount = Math.Min(safeMaxLines, Math.Max(0, sanitizedMessages.Count));

            return string.Join("\n", sanitizedMessages.Skip(skipCount).Take(takeCount));
        }

        private static List<string> SanitizeConversationMessagesForAi(IEnumerable<string> rawMessages, string npcName)
        {
            var source = (rawMessages ?? Enumerable.Empty<string>())
                .Select(message => (message ?? string.Empty).Trim())
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToList();

            var sanitized = new List<string>();

            for (int i = 0; i < source.Count; i++)
            {
                string line = source[i];

                if (line.StartsWith(PlayerPhotoPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string tags = TryConsumeFollowingPhotoTag(source, ref i, isPlayerPhoto: true);
                    sanitized.Add(FormatPhotoSummaryForAi(isPlayerPhoto: true, tags, npcName));
                    continue;
                }

                if (line.StartsWith(NpcPhotoPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string tags = TryConsumeFollowingPhotoTag(source, ref i, isPlayerPhoto: false);
                    sanitized.Add(FormatPhotoSummaryForAi(isPlayerPhoto: false, tags, npcName));
                    continue;
                }

                if (line.StartsWith(PlayerPhotoTagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string tags = NormalizePhotoTagText(line.Substring(PlayerPhotoTagPrefix.Length));
                    sanitized.Add(FormatPhotoSummaryForAi(isPlayerPhoto: true, tags, npcName));
                    continue;
                }

                if (line.StartsWith(NpcPhotoTagPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    string tags = NormalizePhotoTagText(line.Substring(NpcPhotoTagPrefix.Length));
                    sanitized.Add(FormatPhotoSummaryForAi(isPlayerPhoto: false, tags, npcName));
                    continue;
                }

                sanitized.Add(line);
            }

            return sanitized;
        }

        private static string TryConsumeFollowingPhotoTag(List<string> source, ref int currentIndex, bool isPlayerPhoto)
        {
            if (currentIndex + 1 >= source.Count)
                return string.Empty;

            string nextLine = source[currentIndex + 1];
            string expectedTagPrefix = isPlayerPhoto ? PlayerPhotoTagPrefix : NpcPhotoTagPrefix;

            if (!nextLine.StartsWith(expectedTagPrefix, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            currentIndex++;
            return NormalizePhotoTagText(nextLine.Substring(expectedTagPrefix.Length));
        }

        private static string NormalizePhotoTagText(string rawTagText)
        {
            return string.Join("; ",
                (rawTagText ?? string.Empty)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(tag => tag.Trim())
                    .Where(tag => !string.IsNullOrWhiteSpace(tag))
                    .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        private static string FormatPhotoSummaryForAi(bool isPlayerPhoto, string tags, string npcName)
        {
            string normalizedTags = NormalizePhotoTagText(tags);

            if (isPlayerPhoto)
            {
                if (string.IsNullOrWhiteSpace(normalizedTags))
                    return "";

                return $"PLAYER: [Attached photo tags: {normalizedTags}]";
            }

            string speaker = string.IsNullOrWhiteSpace(npcName) ? "NPC" : npcName;
            if (string.IsNullOrWhiteSpace(normalizedTags))
                return $"";

            return $"{speaker}: [Sent photo tags: {normalizedTags}]";
        }

        private static string GetSystemMessage(NPC npc, string type, bool allowToolCalling = true)
        {
            try
            {
                int heartLevel = 0;
                if (Game1.player.friendshipData.ContainsKey(npc.Name)) heartLevel = (int)Game1.player.friendshipData[npc.Name].Points / 250;


                string relation = heartLevel <= 2 ? "stranger" : heartLevel <= 4 ? "acquaintance" : heartLevel <= 6 ? "close friend" : "best friend";
                bool isDating = Game1.player.friendshipData.TryGetValue(npc.Name, out Friendship friendship) && friendship.IsDating();
                bool isRoommate = friendship != null && friendship.IsRoommate();
                bool isMarried = friendship != null && friendship.IsMarried();
                bool isEngaged = friendship != null && friendship.IsEngaged();
                bool isDivorced = friendship != null && friendship.IsDivorced();

                if (isDivorced) relation = "divorced";
                else if (isRoommate) relation = "roommate";
                else if (isMarried) relation = "married";
                else if (isEngaged) relation = "engaged";
                else if (isDating) relation = "dating";

                string npcLocation = npc.currentLocation.DisplayName;
                if (npc.currentLocation.Name == npc.DefaultMap)
                    npcLocation = $"{npc.Name} home";

                int time = Game1.timeOfDay;
                int hour24 = (time / 100) % 24;
                int minute = time % 100;
                string period = hour24 >= 12 ? "PM" : "AM";
                int hour12 = hour24 % 12;
                if (hour12 == 0) hour12 = 12;
                string timeFormatted = $"{hour12}:{minute:00} {period}";

                string planting = (FarmCropNames.Count + FarmTreeNames.Count) == 0
                    ? ""
                    : (Game1.random.NextBool() && FarmCropNames.Count > 0
                        ? FarmCropNames[Game1.random.Next(FarmCropNames.Count)]
                        : FarmTreeNames.Count > 0
                            ? FarmTreeNames[Game1.random.Next(FarmTreeNames.Count)]
                            : FarmCropNames[Game1.random.Next(FarmCropNames.Count)]);


                string data = @$"{npc.Name} currently at {npcLocation}; Today weather: {Game1.currentLocation.GetWeather().Weather}; Tomorrow weather: {Game1.weatherForTomorrow};  Time: {timeFormatted}, day {Game1.dayOfMonth} {Game1.currentLocation.GetSeason()};";


                bool isPlayerBirthdayToday = MessageManager.IsPlayerBirthdayToday();
                if (isPlayerBirthdayToday)
                    MessageManager.AddMessage(npc.Name, "It is PLAYER **" + Game1.player.Name + "**'s birthday today.", type: "system");

                if (planting != "")
                    data += $"\nPlayer planting some {planting} on the farm";

                string latestGift = "";
                var dayAgo = 0;
                if (GiftMemories.TryGetValue(npc.Name, out GiftMemory? memory) && memory != null)
                {
                    latestGift = memory.GiftName;
                    dayAgo = 3 - memory.DaysRemaining;
                }

                if (latestGift != "")
                    data += $"\nPlayer gifted {npc.Name} a {latestGift} {dayAgo} days ago.";

                foreach (var recentEvent in RecentEvents)
                {
                    data += $"\n{recentEvent.Description}";
                }

                var npcBirthdayTmr = GetNpcsWithBirthdayToday();
                if (npcBirthdayTmr.Count > 0)
                {
                    data += "It is " + string.Join(", ", npcBirthdayTmr.Select(n => n.Name)) + " birthday today.";
                }

                var messages_history = MessageManager.GetMessagesForNpc(npc.Name);
                string combined = BuildConversationPreviewForAi(messages_history, npc.Name, maxLines: 12);

                string summary = "";
                if (npcConversationSummary.ContainsKey(npc.Name))
                    summary = npcConversationSummary[npc.Name];

                var npcCharacteristic = GetNpcCharacteristicForPrompt(npc);
                string playerProfile = MessageManager.currentPlayerProfile;

                playerProfile = playerProfile.Length > 150 && string.IsNullOrEmpty(Config.OpenAIKey) ? playerProfile.Substring(0, 150) : playerProfile;

                if (type == "response")
                {
                    object[] possibleEvent = allowToolCalling
                        ? GetToolList(npc, listOnly: true)
                        : Array.Empty<object>();
                    var systemMessage = $@"
                            **Context**
                            * You are roleplaying as NPC **{npc.Name}** in Stardew Valley, responding to a conversation with PLAYER **{Game1.player.Name}**
                            PLAYER profile: {(Game1.player.IsMale ? "Male" : "Female")}, {MessageManager.currentPlayerAge} age, {playerProfile}

                            **Response Instructions**
                            1. **Response:** Reply to the user with a message of <30 words.
                            2. **Slice of Life:** You may occasionally invent new small, random dynamic details happening around you for your response. Be creative and do not be repetitive with your responses. 

                            * **{npc.Name} personality:** {npcCharacteristic}
                            * **Relationship with player:** {relation}
                            * **World Context:** {data}
                            * **Conversation History:** {summary}
                            ";

                    if (allowToolCalling && possibleEvent.Any())
                        systemMessage = $@"
                            **Context**
                            * You are roleplaying as NPC **{npc.Name}** in Stardew Valley, responding to a conversation with PLAYER **{Game1.player.Name}**
                            PLAYER profile: {(Game1.player.IsMale ? "Male" : "Female")}, {playerProfile}

                            **Response Instructions**
                            1. **Function call:** When the player intends to invite the NPC for an event that is in the possible event list below, then you must return the function schedule_event and finish.
                            2. **Response:** If player do not intend to invite the NPC for an event, then reply to the user with a message of <30 words.
                            3. **Slice of Life:** You may occasionally invent new small, random dynamic details happening around you for your response. Be creative and do not be repetitive with your responses. Do not invite or suggest the player for an event.
                            
                            * **{npc.Name} personality:** {npcCharacteristic}
                            * **Relationship with player:** {relation}
                            * **Possible events:** {string.Join(", ", possibleEvent)}
                            * **World Context:** {data}
                            * **Conversation History:** {summary}
                            ";



                    return AppendLanguageInstruction(systemMessage);
                }
                else if (type == "text")
                {
                    var systemMessage = $@"
                        **Context**
                        * You are roleplaying as NPC {npc.Name} in Stardew Valley. Send a text message to the PLAYER {Game1.player.Name} ({Game1.player.Gender}, teen/young adult) to start a new random conversation.

                        **Response Instructions**
                        1. Keep the text under 30 words. Be creative, in-character, and match your relationship level.
                        3. **Slice of Life:** You may occasionally invent new small, random dynamic details happening around you for your response. Be creative and not repetitive with your responses.

                        * **{npc.Name} personality:** {npcCharacteristic}
                        * **Relationship with player:** {relation}
                        * **World Context:** {data}
                        * **Conversation History:** {summary}
                        ";
                    return AppendLanguageInstruction(systemMessage);
                }
                else if (type == "invite")
                {
                    object[] possibleEvent = GetToolList(npc, listOnly: true, ignoreBirthdayEvent: true);
                    var systemMessage = $@"
                        **Context**
                        * You are roleplaying as NPC {npc.Name} in Stardew Valley. Send a text message to the PLAYER {Game1.player.Name} ({Game1.player.Gender}, teen/young adult) to invite them to hang out.

                        **Response Instructions**
                        * Base on the relationship with player and the world context, if it's appropriate to invite the player for an event today, then send a text invitation under 20 words. You will select one of these events: {string.Join(", ", possibleEvent)}. Otherwise, do not send any message.

                        * **{npc.Name} personality:** {npcCharacteristic}
                        * **Relationship with player:** {relation}
                        * **World Context:** {data}
                        * **Conversation History:** {summary}
                        ";

                    return AppendLanguageInstruction(systemMessage);
                }

                return "";
            }
            catch (Exception)
            {
                return "";
            }
        }

        public static async Task<Dictionary<string, string>> SummaryConversationsBatch(Dictionary<string, string> conversationsByNpc, bool bypassAiLimit = false)
        {
            var parsedSummaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (conversationsByNpc == null || conversationsByNpc.Count == 0)
                return parsedSummaries;

            if (IsAiTemporarilyDisabledForPhoneInactivity())
                return parsedSummaries;

            if (!bypassAiLimit && !TryConsumeAiCallSlot())
            {
                SMonitor.Log("SummaryConversationsBatch skipped because AI limit is reached.", LogLevel.Trace);
                return parsedSummaries;
            }

            string provider = GetProviderForModel(summaryModel);
            var key = ResolveAiRuntimeKey(provider);
            if (!string.Equals(provider, AiProviderCustom, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(key))
            {
                SMonitor.Log("SummaryConversationsBatch skipped because no AI key is available.", LogLevel.Trace);
                return parsedSummaries;
            }

            var maxAiSummaryLength = 200;
            bool hasDedicatedProvider = IsBringYourOwnAiProviderMode();
            var maxConversationsToSummarize = hasDedicatedProvider ? 6 : 3;
            if (hasDedicatedProvider && Config.MaxSummaryWordCount != 0)
            {
                maxAiSummaryLength = Config.MaxSummaryWordCount;
            }

            var currentMemories = new List<object>();
            var todayConversations = new List<object>();
            var expectedNpcNames = new List<string>();
            var summaryCandidates = new List<(string NpcName, string Conversation, string PreviousSummary, int ConversationLength)>();

            foreach (var kvp in conversationsByNpc)
            {
                string npcName = (kvp.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(npcName))
                    continue;

                NPC npc = Game1.getCharacterFromName(npcName, mustBeVillager: false);
                if (npc is null)
                    continue;

                string sanitizedMessageList = BuildConversationPreviewForAi(
                    (kvp.Value ?? string.Empty).Split('\n', StringSplitOptions.None),
                    npcName,
                    maxLines: 20);

                if (string.IsNullOrWhiteSpace(sanitizedMessageList))
                    continue;

                string previousSummary = npcConversationSummary.TryGetValue(npcName, out string? existingSummary)
                    ? existingSummary
                    : string.Empty;

                summaryCandidates.Add((npcName, sanitizedMessageList, previousSummary, sanitizedMessageList.Length));
            }

            if (summaryCandidates.Count == 0)
                return parsedSummaries;

            foreach (var candidate in summaryCandidates
                .Where(item => item.Conversation.Contains("PLAYER", StringComparison.OrdinalIgnoreCase) && item.ConversationLength > 200)
                .OrderByDescending(item => item.ConversationLength)
                .ThenBy(item => item.NpcName, StringComparer.OrdinalIgnoreCase)
                .Take(maxConversationsToSummarize))
            {
                currentMemories.Add(new { npc = candidate.NpcName, summary = candidate.PreviousSummary });
                todayConversations.Add(new { npc = candidate.NpcName, conversation = candidate.Conversation });
                expectedNpcNames.Add(candidate.NpcName);
            }

            var system = "You are the memory manager for multiple NPCs in Stardew Valley. "
                + "You are provided with two arrays: current memory summaries and today's new conversations with the PLAYER. "
                + "For each NPC, read the previous memory and the new conversation, then summarize it into an updated memory bank. "
                + "Focus on factual lore, player preferences, important life events, and the current emotional standing/relationship with the PLAYER. "
                + $"Keep each NPC summary under {maxAiSummaryLength} words. Remove outdated, trivial, non-memory-value details. "
                + "Return only one valid JSON object with this format: {\"summaries\":{\"NPC Name\":\"updated summary\"}}. ";

            system = AppendLanguageInstruction(system);


            var userPayload = new
            {
                currentMemory = currentMemories,
                todayConversation = todayConversations
            };

            using (var httpClient = new HttpClient())
            {
                HttpResponseMessage httpResponse;

                if (provider == AiProviderGemini)
                {
                    string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(summaryModel)}:generateContent";
                    httpClient.DefaultRequestHeaders.Add("X-goog-api-key", key);

                    var requestBody = new
                    {
                        systemInstruction = new
                        {
                            parts = new object[]
                            {
                                new { text = system }
                            }
                        },
                        contents = new object[]
                        {
                            new
                            {
                                role = "user",
                                parts = new object[]
                                {
                                    new { text = JsonConvert.SerializeObject(userPayload) }
                                }
                            }
                        },
                        generationConfig = new
                        {
                            responseMimeType = "application/json",
                            thinkingConfig = new
                            {
                                thinkingLevel = summaryGeminiThinkingLevel
                            }
                        }
                    };

                    var jsonRequest = JsonConvert.SerializeObject(requestBody);
                    var httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    httpResponse = await httpClient.PostAsync(endpoint, httpContent);
                }
                else if (provider == AiProviderCustom)
                {
                    HttpResponseMessage? customResponse = await SendCustomProviderRequestAsync(
                        httpClient,
                        summaryModel,
                        system,
                        JsonConvert.SerializeObject(userPayload),
                        key,
                        "SummaryConversationsBatch");

                    if (customResponse == null)
                        return parsedSummaries;

                    httpResponse = customResponse;
                }
                else
                {
                    httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key);

                    var requestBody = new
                    {
                        model = summaryModel,
                        input = new object[]
                        {
                            new
                            {
                                role = "developer",
                                content = new object[]
                                {
                                    new { type = "input_text", text = system }
                                }
                            },
                            new
                            {
                                role = "user",
                                content = new object[]
                                {
                                    new { type = "input_text", text = JsonConvert.SerializeObject(userPayload) }
                                }
                            },
                        },
                        text = new { format = new { type = "json_object" }, verbosity = "low" },
                        reasoning = summaryReasoningEffort
                    };

                    var jsonRequest = JsonConvert.SerializeObject(requestBody);
                    var httpContent = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                    httpResponse = await httpClient.PostAsync("https://api.openai.com/v1/responses", httpContent);
                }

                if (httpResponse.IsSuccessStatusCode)
                {
                    var jsonResponse = await httpResponse.Content.ReadAsStringAsync();
                    RegisterSuccessfulAiCall();

                    JToken parsedToken;
                    try
                    {
                        parsedToken = JToken.Parse(jsonResponse);
                    }
                    catch (JsonReaderException)
                    {
                        SMonitor.Log($"Unable to parse summary response payload from {provider}: {jsonResponse}", LogLevel.Trace);
                        return parsedSummaries;
                    }

                    string responseText;
                    if (provider == AiProviderGemini)
                    {
                        JObject geminiJson = parsedToken as JObject ?? new JObject();
                        responseText = GetGeminiResponseOutputText(geminiJson);
                    }
                    else if (provider == AiProviderOpenAi)
                    {
                        JObject openAiJson = parsedToken as JObject ?? new JObject();
                        responseText = GetResponseOutputText(openAiJson);
                    }
                    else
                    {
                        responseText = GetCustomResponseOutputText(parsedToken);
                    }

                    if (TryParseBatchConversationSummaries(responseText, expectedNpcNames, out Dictionary<string, string> summaries))
                        return summaries;

                    SMonitor.Log($"Unable to parse batch conversation summaries: {responseText}", LogLevel.Trace);
                    return parsedSummaries;
                }
                else
                {
                    // Get the status code
                    var statusCode = (int)httpResponse.StatusCode; // Convert to int for switch
                    string errorMessage = "Check for mod update";
                    switch (statusCode)
                    {
                        case 400:
                            errorMessage = "Bad request sent to AI provider.";
                            break;
                        case 403:
                            errorMessage = "Country, region, or territory not supported.";
                            break;
                        case 429:
                            errorMessage = "Please try again in a few minutes. If not work, then total AI usage for all players has passed the limit set by OpenAI. This will be reset the next day in timezone UTC+0";
                            break;
                        case 500:
                            errorMessage = "Server Error: The server had an issue while processing your request. Please try again.";
                            break;
                        case 503:
                            errorMessage = "Server Overload: The server is experiencing high traffic. Please try again later.";
                            break;
                    }

                    SMonitor.Log($"Unable to receive AI content from {provider}. {statusCode}, {errorMessage}\n\n", LogLevel.Error);
                    return parsedSummaries;
                }
            }
        }

        public static object[] GetToolList(NPC npc, bool listOnly = false, bool ignoreBirthdayEvent = false)
        {
            var functionList = new List<object>();

            if (Game1.timeOfDay > 2200 || iUnlimitedEventExpansionApi == null || !iUnlimitedEventExpansionApi.CanScheduleNewEvent())
                return functionList.ToArray();

            int heartLevel = 0;
            if (Game1.player.friendshipData.ContainsKey(npc.Name)) heartLevel = (int)Game1.player.friendshipData[npc.Name].Points / 250;
            var registeredEvents = GetRegisteredUnlimitedEventsForHeartLevel(heartLevel);

            if (registeredEvents.Count > 0)
            {
                var allowedEvents = registeredEvents
                    .Select(evt => evt.EventType)
                    .ToArray();

                // Get list of all event only, exclude Birthday for invitation type
                if (listOnly)
                {
                    if (!ignoreBirthdayEvent)
                        return allowedEvents;

                    return registeredEvents
                    .Where(evt => !evt.EventType.Equals("Birthday", StringComparison.OrdinalIgnoreCase))
                    .Select(evt => evt.EventType)
                    .ToArray();
                }

                var extraToolDescriptions = string.Join(
                    " ",
                    registeredEvents
                        .Select(evt => evt.ToolDescription)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .Distinct(StringComparer.OrdinalIgnoreCase));

                string scheduleToolDescription = "Schedule an event for NPC and PLAYER, specifying the event type, and NPC response message.";
                if (!string.IsNullOrWhiteSpace(extraToolDescriptions))
                    scheduleToolDescription += $" {extraToolDescriptions}";

                functionList.Add(
                    new
                    {
                        type = "function",
                        name = "schedule_event",
                        description = scheduleToolDescription,
                        parameters = new
                        {
                            type = "object",
                            properties = new
                            {
                                npc = new { type = "string", description = "Name of the NPC" },
                                event_type = new
                                {
                                    type = "string",
                                    description = "The specific type of event agreed upon.",
                                    @enum = allowedEvents.ToArray()
                                },
                                npc_response = new { type = "string", description = "A message from the NPC to invite or confirm the event" }
                            },
                            required = new[] { "npc", "event_type", "npc_response" },
                            additionalProperties = false
                        },
                        strict = true
                    }
                );
            }
            return functionList.ToArray();
        }

        private static JArray GetResponseFunctionCalls(JObject responseJson)
        {
            var calls = new JArray();
            var output = responseJson?["output"] as JArray;
            if (output == null)
                return calls;

            foreach (var item in output)
            {
                if (item?["type"]?.ToString() == "function_call")
                    calls.Add(item);
            }

            return calls;
        }

        private static string GetResponseOutputText(JObject responseJson)
        {
            var output = responseJson?["output"] as JArray;
            if (output != null)
            {
                foreach (var item in output.Reverse())
                {
                    if (item?["type"]?.ToString() != "message")
                        continue;

                    var content = item["content"] as JArray;
                    if (content == null)
                        continue;

                    foreach (var part in content)
                    {
                        if (part?["type"]?.ToString() == "output_text")
                        {
                            var text = part["text"]?.ToString().Trim();
                            if (!string.IsNullOrWhiteSpace(text))
                                return text;
                        }
                    }
                }
            }

            var fallbackText = responseJson?["output_text"]?.ToString()?.Trim();
            return fallbackText ?? string.Empty;
        }

        private static object[] BuildGeminiFunctionDeclarations(object[] openAiTools)
        {
            if (openAiTools == null || openAiTools.Length == 0)
                return Array.Empty<object>();

            var declarations = new List<object>();
            foreach (object tool in openAiTools)
            {
                if (tool == null)
                    continue;

                JObject toolObject;
                try
                {
                    toolObject = JObject.FromObject(tool);
                }
                catch
                {
                    continue;
                }

                string name = toolObject["name"]?.ToString()?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string description = toolObject["description"]?.ToString()?.Trim() ?? string.Empty;

                JToken schema = toolObject["parameters"]?.DeepClone() ?? new JObject { ["type"] = "OBJECT" };
                schema = ConvertJsonSchemaTypeValuesToGemini(schema);

                var declaration = new JObject
                {
                    ["name"] = name,
                    ["parameters"] = schema
                };

                if (!string.IsNullOrWhiteSpace(description))
                    declaration["description"] = description;

                declarations.Add(declaration);
            }

            return declarations.ToArray();
        }

        private static JToken ConvertJsonSchemaTypeValuesToGemini(JToken token)
        {
            if (token == null)
                return new JObject { ["type"] = "OBJECT" };

            if (token.Type == JTokenType.Object)
            {
                var sourceObject = (JObject)token;
                var resultObject = new JObject();

                foreach (JProperty property in sourceObject.Properties())
                {
                    if (property.Name.Equals("strict", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("additionalProperties", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (property.Name.Equals("type", StringComparison.OrdinalIgnoreCase)
                        && property.Value.Type == JTokenType.String)
                    {
                        resultObject[property.Name] = property.Value.ToString().ToUpperInvariant();
                        continue;
                    }

                    resultObject[property.Name] = ConvertJsonSchemaTypeValuesToGemini(property.Value);
                }

                return resultObject;
            }

            if (token.Type == JTokenType.Array)
            {
                var resultArray = new JArray();
                foreach (JToken item in (JArray)token)
                {
                    resultArray.Add(ConvertJsonSchemaTypeValuesToGemini(item));
                }

                return resultArray;
            }

            return token.DeepClone();
        }

        private static JArray GetGeminiResponseFunctionCalls(JObject responseJson)
        {
            var calls = new JArray();
            var candidates = responseJson?["candidates"] as JArray;
            if (candidates == null)
                return calls;

            foreach (var candidate in candidates)
            {
                var parts = candidate?["content"]?["parts"] as JArray;
                if (parts == null)
                    continue;

                foreach (var part in parts)
                {
                    var functionCall = part?["functionCall"];
                    if (functionCall == null)
                        continue;

                    string functionName = functionCall["name"]?.ToString()?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(functionName))
                        continue;

                    JToken? argsToken = functionCall["args"];
                    string argumentsJson = argsToken == null
                        ? "{}"
                        : argsToken.Type == JTokenType.String
                            ? argsToken.ToString()
                            : argsToken.ToString(Formatting.None);

                    calls.Add(new JObject
                    {
                        ["name"] = functionName,
                        ["arguments"] = string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson
                    });
                }
            }

            return calls;
        }

        private static string GetGeminiResponseOutputText(JObject responseJson)
        {
            var candidates = responseJson?["candidates"] as JArray;
            if (candidates != null)
            {
                foreach (var candidate in candidates)
                {
                    var parts = candidate?["content"]?["parts"] as JArray;
                    if (parts == null)
                        continue;

                    var textParts = parts
                        .Select(part => part?["text"]?.ToString()?.Trim() ?? string.Empty)
                        .Where(text => !string.IsNullOrWhiteSpace(text))
                        .ToArray();

                    if (textParts.Length > 0)
                        return string.Join("\n", textParts).Trim();
                }
            }

            return string.Empty;
        }

        private static JToken? TrySelectJsonPathToken(JToken responseToken, string path)
        {
            if (responseToken == null)
                return null;

            string normalizedPath = (path ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return null;

            try
            {
                JToken? selectedToken = responseToken.SelectToken(normalizedPath, errorWhenNoMatch: false);
                if (selectedToken != null)
                    return selectedToken;

                string rootedPath = normalizedPath.StartsWith("$", StringComparison.Ordinal)
                    ? normalizedPath
                    : normalizedPath.StartsWith("[", StringComparison.Ordinal)
                        ? $"${normalizedPath}"
                        : $"$.{normalizedPath}";

                return responseToken.SelectToken(rootedPath, errorWhenNoMatch: false);
            }
            catch
            {
                return null;
            }
        }

        private static string ExtractTextFromCustomResponseToken(JToken? token)
        {
            if (token == null)
                return string.Empty;

            if (token.Type == JTokenType.String)
                return token.ToString().Trim();

            if (token.Type == JTokenType.Array)
            {
                string[] values = ((JArray)token)
                    .Select(ExtractTextFromCustomResponseToken)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();

                return values.Length == 0 ? string.Empty : string.Join("\n", values).Trim();
            }

            if (token.Type == JTokenType.Object)
            {
                JObject tokenObject = (JObject)token;
                string[] preferredKeys = { "text", "content", "message", "output_text", "response", "result" };
                foreach (string preferredKey in preferredKeys)
                {
                    if (!TryGetJsonPropertyValue(tokenObject, preferredKey, out JToken? preferredValue))
                        continue;

                    string extracted = ExtractTextFromCustomResponseToken(preferredValue);
                    if (!string.IsNullOrWhiteSpace(extracted))
                        return extracted;
                }

                string[] fallbackValues = tokenObject.Properties()
                    .Select(property => ExtractTextFromCustomResponseToken(property.Value))
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray();

                return fallbackValues.Length == 0 ? string.Empty : string.Join("\n", fallbackValues).Trim();
            }

            return token.ToString().Trim();
        }

        private static string GetCustomResponseOutputText(JToken responseToken)
        {
            if (responseToken == null)
                return string.Empty;

            string configuredPath = ResolveCustomApiResponseTextPath();
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                JToken? configuredToken = TrySelectJsonPathToken(responseToken, configuredPath);
                string configuredText = ExtractTextFromCustomResponseToken(configuredToken);
                if (!string.IsNullOrWhiteSpace(configuredText))
                    return configuredText;
            }

            foreach (string fallbackPath in CustomResponseFallbackPaths)
            {
                JToken? fallbackToken = TrySelectJsonPathToken(responseToken, fallbackPath);
                string fallbackText = ExtractTextFromCustomResponseToken(fallbackToken);
                if (!string.IsNullOrWhiteSpace(fallbackText))
                    return fallbackText;
            }

            return ExtractTextFromCustomResponseToken(responseToken);
        }

        private static bool TryParseBatchConversationSummaries(string responseText, IEnumerable<string> expectedNpcNames, out Dictionary<string, string> summaries)
        {
            summaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] normalizedNpcNames = (expectedNpcNames ?? Enumerable.Empty<string>())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (normalizedNpcNames.Length == 0)
                return false;

            string jsonPayload = ExtractJsonPayload(responseText);
            if (string.IsNullOrWhiteSpace(jsonPayload))
                return false;

            try
            {
                JToken token = JToken.Parse(jsonPayload);
                return TryPopulateBatchConversationSummaries(token, normalizedNpcNames, summaries);
            }
            catch (JsonReaderException)
            {
                return false;
            }
        }

        private static bool TryPopulateBatchConversationSummaries(JToken token, IReadOnlyCollection<string> expectedNpcNames, Dictionary<string, string> summaries)
        {
            if (token == null)
                return false;

            if (token.Type == JTokenType.Object)
            {
                JObject obj = (JObject)token;
                if ((TryGetJsonPropertyValue(obj, "summaries", out JToken? summariesToken)
                        || TryGetJsonPropertyValue(obj, "summary", out summariesToken)
                        || TryGetJsonPropertyValue(obj, "memory", out summariesToken))
                    && summariesToken != null)
                {
                    return TryPopulateBatchConversationSummaries(summariesToken, expectedNpcNames, summaries);
                }

                foreach (string npcName in expectedNpcNames)
                {
                    if (!TryGetJsonPropertyValue(obj, npcName, out JToken? summaryToken))
                        continue;

                    string summaryText = NormalizeGeneratedConversationSummaryText(npcName, ExtractConversationSummaryText(summaryToken));
                    if (!string.IsNullOrWhiteSpace(summaryText))
                        summaries[npcName] = summaryText;
                }

                return summaries.Count > 0;
            }

            if (token.Type != JTokenType.Array)
                return false;

            foreach (JToken item in (JArray)token)
            {
                if (item.Type != JTokenType.Object)
                    continue;

                JObject itemObject = (JObject)item;
                string npcName = string.Empty;
                if (TryGetJsonPropertyValue(itemObject, "npc", out JToken? npcToken)
                    || TryGetJsonPropertyValue(itemObject, "name", out npcToken)
                    || TryGetJsonPropertyValue(itemObject, "character", out npcToken))
                {
                    npcName = (npcToken?.ToString() ?? string.Empty).Trim();
                }

                if (string.IsNullOrWhiteSpace(npcName) || !expectedNpcNames.Contains(npcName, StringComparer.OrdinalIgnoreCase))
                    continue;

                JToken? summaryTokenValue = null;
                if (!TryGetJsonPropertyValue(itemObject, "summary", out summaryTokenValue)
                    && !TryGetJsonPropertyValue(itemObject, "memory", out summaryTokenValue)
                    && !TryGetJsonPropertyValue(itemObject, "text", out summaryTokenValue))
                {
                    continue;
                }

                string summaryText = NormalizeGeneratedConversationSummaryText(npcName, ExtractConversationSummaryText(summaryTokenValue));
                if (!string.IsNullOrWhiteSpace(summaryText))
                    summaries[npcName] = summaryText;
            }

            return summaries.Count > 0;
        }

        private static string ExtractConversationSummaryText(JToken? summaryToken)
        {
            if (summaryToken == null)
                return string.Empty;

            if (summaryToken.Type == JTokenType.Object)
            {
                JObject summaryObject = (JObject)summaryToken;
                if (TryGetJsonPropertyValue(summaryObject, "summary", out JToken? nestedSummary)
                    || TryGetJsonPropertyValue(summaryObject, "memory", out nestedSummary)
                    || TryGetJsonPropertyValue(summaryObject, "text", out nestedSummary))
                {
                    return nestedSummary?.ToString() ?? string.Empty;
                }
            }

            if (summaryToken.Type == JTokenType.Array)
            {
                return string.Join(" ",
                    ((JArray)summaryToken)
                        .Select(item => item?.ToString() ?? string.Empty)
                        .Where(text => !string.IsNullOrWhiteSpace(text)));
            }

            return summaryToken.ToString();
        }

        private static string NormalizeGeneratedConversationSummaryText(string npcName, string? summaryText)
        {
            string normalizedText = string.Join(" ", (summaryText ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                .Trim();

            if (normalizedText.StartsWith($"{npcName}:", StringComparison.OrdinalIgnoreCase))
                normalizedText = normalizedText.Substring(npcName.Length + 1).TrimStart();

            if ((normalizedText.StartsWith("\"") && normalizedText.EndsWith("\"") && normalizedText.Length > 1)
                || (normalizedText.StartsWith("'") && normalizedText.EndsWith("'") && normalizedText.Length > 1))
            {
                normalizedText = normalizedText.Substring(1, normalizedText.Length - 2).Trim();
            }

            return normalizedText;
        }

        private static bool TryGetJsonPropertyValue(JObject obj, string propertyName, out JToken? value)
        {
            foreach (JProperty property in obj.Properties())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private static string ExtractJsonPayload(string responseText)
        {
            string trimmed = (responseText ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstLineBreak = trimmed.IndexOf('\n');
                if (firstLineBreak >= 0)
                    trimmed = trimmed.Substring(firstLineBreak + 1).Trim();

                int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (closingFence >= 0)
                    trimmed = trimmed.Substring(0, closingFence).Trim();
            }

            if (trimmed.StartsWith("{", StringComparison.Ordinal) || trimmed.StartsWith("[", StringComparison.Ordinal))
                return trimmed;

            int objectStart = trimmed.IndexOf('{');
            int objectEnd = trimmed.LastIndexOf('}');
            if (objectStart >= 0 && objectEnd > objectStart)
                return trimmed.Substring(objectStart, objectEnd - objectStart + 1).Trim();

            int arrayStart = trimmed.IndexOf('[');
            int arrayEnd = trimmed.LastIndexOf(']');
            if (arrayStart >= 0 && arrayEnd > arrayStart)
                return trimmed.Substring(arrayStart, arrayEnd - arrayStart + 1).Trim();

            return trimmed;
        }

        public static async Task<(int, int)> GetOpenAIUsage()
        {
            List<string> premiumModels = new List<string> { "gpt-5.4", "gpt-5.2", "gpt-5.1", "gpt-5.1-codex", "gpt-5", "gpt-5-codex", "gpt-5-chat-latest", "gpt-4.1", "gpt-4o", "o1", "o3" };
            List<string> regularModels = new List<string> { "gpt-5.4-mini", "gpt-5.4-nano", "gpt-5.1-codex-mini", "gpt-5-mini", "gpt-5-nano", "gpt-4.1-mini", "gpt-4.1-nano", "gpt-4o-mini",
                                                            "o1-mini", "o3-mini", "o4-mini", "codex-mini-latest" };
            string admin_key = ResolveOpenAiAdminKey();
            if (string.IsNullOrWhiteSpace(admin_key))
                return (-1, -1);

            string usageUrl = "https://api.openai.com/v1/organization/usage/completions";
            using HttpClient client = new HttpClient();

            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin_key);

            DateTime utcNow = DateTime.UtcNow;
            DateTime utcStartOfToday = utcNow.Date;
            long startTime = new DateTimeOffset(utcStartOfToday).ToUnixTimeSeconds();
            long endTime = new DateTimeOffset(utcNow).ToUnixTimeSeconds();

            try
            {
                var perModelTotals = new Dictionary<string, (long Input, long Output)>();
                string? nextPage = null;
                bool hasMore;

                do
                {
                    var query = $"start_time={startTime}&end_time={endTime}&group_by=model";
                    if (!string.IsNullOrWhiteSpace(nextPage))
                        query += $"&page={Uri.EscapeDataString(nextPage)}";

                    string requestUrl = $"{usageUrl}?{query}";
                    HttpResponseMessage response = await client.GetAsync(requestUrl);

                    if (!response.IsSuccessStatusCode)
                    {
                        string error = await response.Content.ReadAsStringAsync();
                        SMonitor.Log($"Error: {response.StatusCode} - {error}", LogLevel.Error);
                        return (-1, -1);
                    }

                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    JObject json = JObject.Parse(jsonResponse);

                    var data = json["data"] as JArray;
                    if (data != null)
                    {
                        foreach (var bucket in data)
                        {
                            var results = bucket?["results"] as JArray;
                            if (results == null)
                                continue;

                            foreach (var result in results)
                            {
                                string? model = result?["model"]?.ToString();
                                if (string.IsNullOrWhiteSpace(model))
                                    model = "unknown";

                                long inputTokens = result?["input_tokens"]?.Value<long>() ?? 0;
                                long outputTokens = result?["output_tokens"]?.Value<long>() ?? 0;

                                if (perModelTotals.TryGetValue(model, out var totals))
                                {
                                    perModelTotals[model] = (totals.Input + inputTokens, totals.Output + outputTokens);
                                }
                                else
                                {
                                    perModelTotals[model] = (inputTokens, outputTokens);
                                }
                            }
                        }
                    }

                    hasMore = json["has_more"]?.Value<bool>() ?? false;
                    nextPage = json["next_page"]?.ToString();
                }
                while (hasMore && !string.IsNullOrWhiteSpace(nextPage));

                if (perModelTotals.Count == 0)
                {
                    return (-1, -1);
                }

                // Aggregate totals for premium and regular model groups
                long premiumInputTotal = 0, premiumOutputTotal = 0;
                long regularInputTotal = 0, regularOutputTotal = 0;

                foreach (var kv in perModelTotals)
                {
                    var modelName = kv.Key ?? string.Empty;
                    var input = kv.Value.Input;
                    var output = kv.Value.Output;

                    // Prefer matching regular (specific) names first, then premium.
                    bool isRegular = regularModels.Any(rm =>
                        modelName.StartsWith(rm, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(rm, modelName, StringComparison.OrdinalIgnoreCase)
                    );
                    bool isPremium = !isRegular && premiumModels.Any(pm =>
                        modelName.StartsWith(pm, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(pm, modelName, StringComparison.OrdinalIgnoreCase)
                    );

                    if (isRegular)
                    {
                        regularInputTotal += input;
                        regularOutputTotal += output;
                    }
                    else if (isPremium)
                    {
                        premiumInputTotal += input;
                        premiumOutputTotal += output;
                    }
                }

                // SMonitor.Log($"Premium models: {premiumInputTotal} input tokens, {premiumOutputTotal} output tokens.\nRegular models: {regularInputTotal} input tokens, {regularOutputTotal} output tokens.", LogLevel.Error);
                return ((int)(premiumInputTotal + premiumOutputTotal), (int)(regularInputTotal + regularOutputTotal));
            }
            catch (Exception ex)
            {
                SMonitor.Log($"Request failed: {ex.Message}", LogLevel.Error);
                return (-1, -1);
            }
        }
        public static string BuildResponseConversationUserInput(NPC npc, string fallbackText)
        {
            if (npc == null || string.IsNullOrWhiteSpace(npc.Name))
                return (fallbackText ?? string.Empty).Trim();

            var messagesHistory = MessageManager.GetMessagesForNpc(npc.Name);
            string conversation = BuildConversationPreviewForAi(messagesHistory, npc.Name, maxLines: 12);

            if (!string.IsNullOrWhiteSpace(conversation))
                return conversation;

            return (fallbackText ?? string.Empty).Trim();
        }

    }
}
