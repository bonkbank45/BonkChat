# BonkChat

> **Modified fork notice:** BonkChat is a modified fork of
> [Chat 2](https://github.com/Infiziert90/ChatTwo) by Infi & Anna,
> modified since July 2026. It adds AI-assisted English learning features
> for Thai players. Licensed under the [EUPL v1.2](LICENCE), the same
> licence as the original work.

BonkChat is a complete rewrite of the in-game chat window as a plugin,
based on Chat 2, with an AI portal for practicing English while you play.

## AI features (BonkChat)

- **AI portal** supporting ChatGPT (OpenAI), Gemini (Google) and SWU AI
- **Grammar correction** (Ctrl+G or the spell-check button): corrects your
  English before sending, highlights changed words and explains each fix
  in Thai so you learn as you chat
- **Thai → English translation** (Ctrl+T or the translate button): type in
  Thai, get natural English for game chat with vocabulary notes
- **Message translation**: right click any received message →
  "AI: Translate to Thai" with slang and vocabulary explanations
- Suggestion panel with apply/dismiss — you always review before sending
- Response cache, configurable prompts and keybinds, API keys stored
  encrypted (Windows DPAPI)

## Chat 2 features

- Unlimited tabs
- Tabs that always send to a certain channel
- More flexible filtering
- RGB channel colouring
- Completely variable font size
- Sidebar tabs
- Unread counts
- Emotes
- Screenshot mode (obfuscate names)
- Custom background images (global or per tab, with cropping) *(BonkChat addition)*

---

### Installation

Add this URL as a custom plugin repository in Dalamud settings
(`/xlsettings` → Experimental → Custom Plugin Repositories):

```
https://raw.githubusercontent.com/bonkbank45/BonkChat/main/repo.json
```

Then install **Chat 2 (BonkChat)** from the plugin installer. Settings and
message history from the official Chat 2 are migrated automatically on
first load.

---

### Chat Window
![chatWindow.png](ChatTwo/images/chatWindow.png)

### With SimpleTweaks "Chat Name Colors"
![withSimpleTweaks.png](ChatTwo/images/withSimpleTweaks.png)

---

### IPC Integration
Other plugins can easily integrate their functionality into the context menu of chat2
For more infos read [IPC Guide](ipc.md)

---

### Thanks to
- [Chat 2](https://github.com/Infiziert90/ChatTwo) by Infi, and the original dev Anna~
- Srinakharinwirot University (SWU AI) for the AI API service
