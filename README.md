*** ADVANCED AI CUSTOM PROVIDER ***

Power users can route Smartphone AI calls to a custom service (local LLM or another provider) by editing config.json.

When CustomApiEndpoint is configured, Smartphone uses custom template mode for:
- NPC chat responses
- Daily conversation summaries
- StardewSocial post text generation
- StardewSocial comment generation

Network policy:
- Remote endpoints must use HTTPS.
- HTTP is only allowed for localhost or loopback hosts (localhost, 127.0.0.1, ::1).

Supported payload placeholders (plain TOKEN and {{TOKEN}} are both supported):
- INPUT_HERE (combined SYSTEM + USER text)
- SYSTEM_INPUT_HERE
- USER_INPUT_HERE
- SYSTEM_MESSAGE_HERE
- USER_MESSAGE_HERE
- MODEL_HERE

Example config.json values:

{
	"CustomApiEndpoint": "http://localhost:11434/v1/chat/completions",
	"CustomApiKey": "",
	"CustomApiKeyHeader": "Authorization",
	"CustomApiKeyPrefix": "Bearer",
	"CustomApiPayloadTemplate": "{\"model\":\"MODEL_HERE\",\"messages\":[{\"role\":\"system\",\"content\":\"SYSTEM_INPUT_HERE\"},{\"role\":\"user\",\"content\":\"USER_INPUT_HERE\"}]}",
	"CustomApiResponseTextPath": "choices[0].message.content",
	"CustomApiTimeoutSeconds": 45
}

Notes:
- CustomApiKey is optional.
- If your provider returns text in a different field, set CustomApiResponseTextPath (for example output_text, result.text, or candidates[0].content.parts[0].text).
- In custom template mode, function calling is disabled for chat. The mod use function call for Unlimited Event Expansion only, so you can fall back to use the button instead.


*** EXTERNAL APP API (FOR OTHER MODDERS) ***

Other mods can integrate with App Messenger by obtaining its API. Below is the documentation for the available API methods.

### Getting the API

You can access the API in your mod's `Entry` method via SMAPI's ModRegistry:

```csharp
public override void Entry(IModHelper helper)
{
    helper.Events.GameLoop.GameLaunched += OnGameLaunched;
}

private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
{
    var api = Helper.ModRegistry.GetApi<IAppMessengerApi>("congha22.SmartphoneAppMessenger");
    if (api != null)
    {
        // Use the API here
    }
}
```

---

### Quick Action Buttons

You can register custom quick-action icons that appear in the App Messenger chat quick-action menu (opened by clicking the `^` button in the chat interface).

#### `RegisterChatQuickActionButton`
Registers a custom quick-action icon.
```csharp
bool RegisterChatQuickActionButton(
    string ownerModId,
    string actionId,
    Texture2D iconTexture,
    Action<string> onClick,
    bool closePhoneOnLaunch = false,
    int sortOrder = 0,
    Rectangle? sourceRect = null,
    List<string>? npcNames = null
);
```
- **Parameters**:
  - `ownerModId`: The unique ID of your mod.
  - `actionId`: A unique identifier for this action within your mod.
  - `iconTexture`: The texture to display as the icon in the quick-action menu.
  - `onClick`: A callback invoked when the user clicks the icon. It receives the currently selected NPC's internal name as a `string`.
  - `closePhoneOnLaunch`: (Optional) Whether the phone menu should close automatically before invoking the callback. Defaults to `false`.
  - `sortOrder`: (Optional) Lower values are shown earlier in the quick-action menu stack. Defaults to `0`.
  - `sourceRect`: (Optional) The source rectangle in the texture if the icon is part of a spritesheet.
  - `npcNames`: (Optional) An allowlist of NPC internal names (e.g. `new List<string> { "Abigail", "Lewis" }`). If provided, the quick action is only visible when chatting with these specific NPCs.
- **Returns**: `true` if the registration was successful; otherwise `false`.

#### `UnregisterChatQuickActionButton`
Unregisters a previously registered quick action.
```csharp
bool UnregisterChatQuickActionButton(string ownerModId, string actionId);
```
- **Parameters**:
  - `ownerModId`: The unique ID of your mod.
  - `actionId`: The action identifier used during registration.
- **Returns**: `true` if a quick-action icon was successfully removed; otherwise `false`.

---

### Sending Messenger Messages

You can programmatically simulate messages sent between the player and NPCs. Nothing will happen if the specified NPC is not in the messenger app list.

#### `SendSmartphoneMessageFromNPC`
Sends a message from an NPC to the player (simulating receiving a message on the smartphone).
```csharp
void SendSmartphoneMessageFromNPC(string npcName, string message, string playerId = "");
```
- **Parameters**:
  - `npcName`: The case-sensitive name of the NPC sending the message (e.g., `"Abigail"`).
  - `message`: The content of the message.
  - `playerId`: (Optional) The target player's `UniqueMultiplayerID` as a string. If null, empty, or invalid, the message is broadcast to all online players.

#### `SendSmartphoneMessageFromPlayer`
Sends a message from the player to an NPC (simulating sending a message from the smartphone).
```csharp
void SendSmartphoneMessageFromPlayer(string npcName, string message, string playerId = "");
```
- **Parameters**:
  - `npcName`: The case-sensitive name of the NPC receiving the message.
  - `message`: The content of the message.
  - `playerId`: (Optional) The target player's `UniqueMultiplayerID` as a string. If null, empty, or invalid, the message is broadcast to all online players.

