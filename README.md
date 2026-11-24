# Glass — a liquid-glass AI sidebar for Windows and Mac

Flick your mouse hard into the left or right edge of the screen and a sidebar made of real-time refracting glass springs out. Chat with Claude, Gemini, Groq, or any OpenAI-compatible model, and go fullscreen when you want more room.

---

## Features

- **Flick to open.** Fling the cursor into the left or right edge of the screen and the sidebar slides out on that side with a springy, liquid motion. Flick the same edge again to close it.
- **Real-time liquid glass.** Every frame, the app captures what's behind the panel and bends it through a curved glass edge. You get a light rainbow split at the rim, a specular highlight, an inner shadow, a gloss toward the top and a soft drop shadow. It's live, so videos and moving windows behind it refract as they play.
- **Tint and blur you can change.** Semi-gray by default. Pick from 9 swatches or type any hex color, and adjust tint strength and blur (0–24 px). Changes show up immediately.
- **A full chat app:**
  - replies stream in as they're written, with formatting (headings, lists, code blocks, bold, italics, inline code, links)
  - a typing indicator, Copy and Retry on replies, and a Stop button while it's answering
  - suggestion chips on the welcome screen
- **Fullscreen mode.** One button grows the glass to fill the monitor and adds a **chats sidebar** where you can start a new chat, search, switch between chats and delete them.
- **Saved chats.** Every conversation is saved automatically and titled from your first message.
- **Several providers:**
  | Provider | Key looks like | Models |
  |---|---|---|
  | Anthropic (Claude) | `sk-ant-…` | You type the model (default `claude-sonnet-5-5`) |
  | Google Gemini | `AIza…` | Loaded live from your account |
  | Groq | `gsk_…` | Loaded live from your account |
  | OpenAI-compatible | `sk-…` / other | You type the model and base URL (OpenAI, OpenRouter, a local server, …) |
- **Easy key import.** Paste a key, or import it from a `.txt`, `.env`, `.json` or `.key` file. The app finds the key and picks the matching provider.
- **Key stays private.** Your key is encrypted with Windows (DPAPI) and stored only on your PC.

---

## Requirements

- Windows 10 version 2004 or later, or Windows 11. Live refraction relies on a Windows feature that hides the app's own window from screen capture. On older Windows the panel falls back to a solid tinted background.
- .NET Framework 4.x, which is built into Windows. Nothing to install.

---

## Build and run

1. Put the `GlassSidebar` folder somewhere easy, like your Desktop.
2. Double-click **`build.bat`**.
3. It closes any copy that's already running, compiles **`GlassSidebar.exe`**, and launches it.

The build uses the C# compiler that ships with Windows (`csc.exe` from .NET Framework). There's no Visual Studio or SDK to install.

If the build fails, the window stays open and shows the errors.

---

## First-time setup

1. Open the sidebar: flick to a screen edge, press **Ctrl+Alt+Space**, or click the tray orb.
2. Click the **gear** icon (or the model chip in the text box).
3. Under **AI provider**, paste your key, or click **Import from file…**.
4. Pick a model. For Gemini and Groq, tap one from the list.
5. Click **Save changes**.

---

## Controls

| Action | How |
|---|---|
| Open / close sidebar | Flick the cursor hard into the left or right screen edge |
| Toggle sidebar | **Ctrl+Alt+Space** |
| Send | **Enter** |
| New line | **Shift+Enter** |
| Stop a reply | Click the send button while it's answering |
| New chat | **Ctrl+N**, or the new-chat icon |
| Fullscreen | Fullscreen icon in the header |
| Close / back | **Esc** (leaves settings first, then fullscreen, then closes) |
| Keep it open when clicking elsewhere | Pin icon |
| Quit | Right-click the tray orb → **Quit** |

---

## Settings

**Glass**
- **Tint:** Semi-gray (default), Graphite, Smoke, Midnight, Ocean, Violet, Rose, Forest, Amber, or a custom hex color.
- **Tint strength:** from clear to nearly solid. Over bright backgrounds the tint gets a little deeper on its own, so white text stays readable.
- **Blur:** Off to 24 px (default 9).
- **Reset to semi-gray:** restores the defaults.

**AI provider:** API key, provider, model, and base URL for OpenAI-compatible services.

**Behavior**
- **Swipe sensitivity:** *Hard flick*, *Normal* or *Light*. Use this if the sidebar opens by accident or won't open.
- **System prompt:** instructions sent with every chat.

---

## Where your data lives

| What | Location |
|---|---|
| Settings and encrypted API key | `%APPDATA%\GlassSidebar\settings.json` |
| Saved chats (one JSON file each) | `%APPDATA%\GlassSidebar\chats\` |

To reset everything, quit the app and delete the `%APPDATA%\GlassSidebar` folder.

---

## Start with Windows

Press **Win+R**, type `shell:startup` and press Enter, then put a shortcut to `GlassSidebar.exe` in the folder that opens.

---

## Troubleshooting

- **"Could not write to output file … being used by another process" while building:** the app is still running. The current `build.bat` closes it for you. If it doesn't, quit it from the tray first.
- **The flick doesn't open it:** set Swipe sensitivity to *Light*. Don't hold a mouse button while flicking, since that counts as dragging. Touchpad multi-finger swipes don't work because Windows keeps those gestures for itself, so move the pointer itself.
- **It opens by accident:** set Swipe sensitivity to *Hard flick*.
- **The panel is a solid color with no refraction:** your Windows version is older than 10 (2004).
- **Text is hard to read over a bright background:** raise Tint strength, or pick a darker tint such as Graphite.
- **Feels slow in fullscreen:** lower the blur. Fullscreen already draws the glass at half resolution to keep it smooth.
- **API errors:** the error shows under the reply. Check the key, the provider and the model name in Settings.

---

## Project layout

```
GlassSidebar/
├─ build.bat                      one-click build + launch
├─ README.md
└─ src/
   ├─ Program.cs                  entry point, tray icon, single instance
   ├─ SwipeDetector.cs            ~250 Hz cursor watcher for edge flicks
   ├─ Native.cs                   Windows API calls (screen capture, hotkey, DPI)
   ├─ Glass.cs                    capture buffer, blur, refraction shader (multi-core)
   ├─ SidebarWindow.cs            window, spring animations, per-frame render loop
   ├─ SidebarWindow.Chat.cs       settings screen + provider/model picker
   ├─ SidebarWindow.Messages.cs   conversation UI, streaming, copy/retry
   ├─ SidebarWindow.ChatList.cs   saved-chats pane (fullscreen)
   ├─ SidebarWindow.Appearance.cs tint swatches, sliders
   ├─ Chats.cs                    saving and loading chats
   ├─ Markdown.cs                 Markdown → rich text for replies
   ├─ Ai.cs                       settings, API key handling, streaming clients
   └─ ui.xaml                     layout and styles
```

### How the glass works

1. The app's window is excluded from screen capture (`WDA_EXCLUDEFROMCAPTURE`), so each frame it can copy the pixels *behind* itself with GDI.
2. It applies the blur you chose to that capture (a fast box blur, run twice).
3. A per-pixel pass treats the panel as a rounded rectangle with a curved bezel. Near the edge, it samples outward along the edge's normal, so content from beyond the rim is pulled in. It samples red, green and blue at slightly different offsets for the rainbow split, then adds the tint, a rim highlight, the inner shadow and the top gloss.
4. The result goes into a bitmap under the chat UI. The work is spread across CPU cores, and large surfaces are drawn at half resolution.
