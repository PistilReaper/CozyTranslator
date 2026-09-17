# CozyTranslator

**English** | [简体中文](README.zh-CN.md)

A lightweight Windows LLM translation and dictionary app. Bring your own API key and use models from different providers.

## Public beta

[Download v0.3.0-beta.1](https://github.com/PistilReaper/CozyTranslator/releases/tag/v0.3.0-beta.1) · [Report an issue](https://github.com/PistilReaper/CozyTranslator/issues) · [MIT license](LICENSE)

![Markdown and mathematical formulas](docs/screenshots/markdown-dark.png)

## Getting started

1. Extract `CozyTranslator-v0.3.0-beta.1-win-x64.zip` to a permanent folder.
2. Run `CozyTranslator.exe`. Keep the runtime files alongside the executable.
3. Open settings with the gear button or “配置模型” (Configure model). Select the API protocol and enter the base URL, exact model ID, and API key.
4. Optionally use “测试连接” (Test connection) to send a short test request, then save your settings.
5. Under “翻译触发” (Translation trigger), choose clipboard translation, right-click mode, or manual only. You can also type directly and press `Ctrl+Enter`.
6. Choose a theme and font size (16–24). Appearance settings can be saved before configuring an API.

The self-contained Windows x64 package includes the .NET 10 runtime; no separate SDK or desktop runtime is required. It has been tested locally on Windows 11 24H2. The current app interface is in Simplified Chinese.

To update, choose “退出” (Exit) in the old version's tray menu before extracting and running the new version. Your API key, model, and prompts are loaded from the same local settings folder.

## Translation triggers

- **Mode 1 · Translate on copy:** Copy text in another app to fill the source area and translate automatically.
- **Mode 2 · Right-click mode:** Hold the left mouse button while selecting a word, sentence, or paragraph. **Without releasing the left button, click the right button with your middle finger.** A word opens a dictionary entry; longer text is translated. Selection reading leaves the clipboard unchanged, preserves ordinary context menus, and keeps focus in the source app.
- **Manual only:** Type and press `Ctrl+Enter`, or use the floating button or global shortcut to query the clipboard.

Switch modes in settings or the tray menu. Right-click mode requires the source app to expose its text selection through Windows UI Automation. Scanned images, controls without text-selection support, and elevated windows are not guaranteed to work. If no selection is available, the app shows a message without sending an empty request.

## Translation and dictionary

- **Sentence and paragraph translation:** Source and translation are stacked vertically. Chinese–English direction is detected automatically or can be selected explicitly. Results stream into the window.
- **English dictionary:** A single English word shows parts of speech, common Chinese meanings, British and American IPA, and a local pronunciation button. Hyphens, internal apostrophes, and surrounding punctuation are supported.
- **Query modes:** Automatic, dictionary, or translation. Phrases use translation by default; dictionary mode can be selected manually.
- **Writing style:** Academic, professional, general, natural, or conversational. When a result is present, releasing the slider on a new style requests a new translation; dragging does not send repeated requests.
- **Pronunciation:** Uses an installed English SAPI voice, separately from the IPA text. Microsoft Zira Desktop was available on the test device. Install an English speech pack in Windows language settings if needed.

## Markdown and formulas

Translations render headings, bold and italic text, lists, block quotes, code, and simple tables automatically. Formula delimiters include `$…$`, `$$…$$`, `\(...\)`, and `\[…\]`. Common fractions, roots, scripts, sums, integrals, `pmatrix` matrices, and multiline `align` formulas have been tested.

- Font settings apply to both text and formulas. Formula colors follow the theme, and wide formulas scroll horizontally.
- Streaming updates are batched every 120 ms and reuse rendered formulas. Stopping a request preserves the content received so far.
- The result copy button and Select All → Copy preserve the complete Markdown. Copying a partial selection preserves included formulas as LaTeX source.
- The source area remains editable plain text. Translation prompts request preservation of Markdown and formulas without wrapping the entire result in a code fence.
- Rendering supports a lightweight LaTeX subset. Unsupported syntax such as `aligned` and custom macros, or incomplete formulas, remains visible as source text. Formula delimiters inside code are not rendered.

## API configuration

| Protocol | Example base URL | Endpoint |
|---|---|---|
| OpenAI-compatible / DeepSeek | `https://api.deepseek.com` | `/chat/completions` |
| Other OpenAI-compatible services | The provider's base URL, including `/v1` when required | Appends `/chat/completions` to the base path |
| Native Claude | `https://api.anthropic.com/v1` | `/messages` |
| Native Gemini | `https://generativelanguage.googleapis.com/v1beta` | `/models/{model}:streamGenerateContent` or `:generateContent` |

Enter the **exact model ID currently available in your provider account**. Use an HTTPS base URL without a full chat endpoint, query parameters, or API key. The app stores one active configuration; changing providers requires the corresponding model and key.

Each protocol has its own authentication and response parsing. Custom services must implement the selected protocol: translation requires streaming text, and dictionary lookup requires JSON matching the dictionary prompt. Model errors, rate limits, malformed responses, and truncated output are shown in the app and can be retried manually.

The app uses Windows/.NET network and proxy settings. Provider access depends on your network. Testing a connection calls the API you configured.

## Desktop controls

| Action | Behavior |
|---|---|
| Drag the floating button | Move it and remember its position |
| Click the floating button / use the global shortcut | Read and query the clipboard |
| Launch the app | Show the main window and taskbar icon; keep the window visible when focus changes |
| Drag empty space / a window edge | Move / resize the window; text editing and selection remain available, and switching modes preserves the size |
| Copy text in another app in Mode 1 | Fill and translate automatically without taking focus; a hidden window stays hidden |
| Use the tray's translation-trigger menu | Switch between clipboard, right-click, and manual modes |
| Minimize | Minimize to the taskbar |
| Close / `Alt+F4` | Hide to the tray and preserve the current content |
| `Esc` / tray's floating-button command | Collapse to the floating button and preserve the current content |
| Pin button | Toggle always-on-top |
| Copy result | Copy the current translation or dictionary entry |
| Stop during generation | Cancel the request and keep the partial result marked as incomplete |
| Click the tray icon | Restore the main window |
| Right-click the tray icon | Open the window, translate the clipboard, show the floating button, hide to the tray, open settings, or exit |
| Launch again | Restore the existing instance |

Choose System, Light, or Dark. System mode follows Windows **app color mode** changes while running; a manual theme overrides it. Appearance changes preview immediately and persist after saving. Window and floating-button positions are constrained to the current monitor's work area.

## Settings and data

Settings are stored in `%LOCALAPPDATA%\CozyTranslator\settings.json`. API keys are encrypted with Windows DPAPI for the current user; copying this file to another account or computer does not transfer a usable key. Source and release packages do not include personal settings.

Mode 1 sends text copied in other apps to your configured model. Mode 2 reads selected text only after the mouse gesture. Manual mode does not collect text automatically. Switch modes in settings or the tray menu. The app's own copy operations and settings editing do not trigger automatic translation. Source text and results stay in the current session, and requests go directly to your configured service. You can edit or reset the system prompt; dictionary lookup uses a separate structured prompt.

## Build and verify

Requires the .NET 10 SDK. Markdown uses [Markdig](https://github.com/xoofx/markdig), and formulas use [WPF-Math](https://github.com/ForNeVeR/xaml-math). Native WPF rich text and vector formulas render locally without a browser engine or online rendering service. WPF, tray integration, DPAPI, and SAPI use Windows/.NET capabilities. Third-party licenses are included in `THIRD-PARTY.md`.

```powershell
dotnet run --project tests/CozyTranslator.Tests -c Release
dotnet run --project tests/CozyTranslator.WindowsTests -c Release -- artifacts/qa
dotnet publish src/CozyTranslator.App -c Release -r win-x64 --self-contained true -o artifacts/CozyTranslator-v0.3.0-beta.1-win-x64
```

Alternatively, run `build.ps1` to test, publish, and package the app. Windows checks require an interactive desktop session and include local speech verification. Visual fixtures belong to the test app and are not included in the product.

### Project structure

- `src/CozyTranslator.Core`: Query classification, prompts, API protocols, streaming decoding, dictionary validation, and query state.
- `src/CozyTranslator.App`: Native UI, tray and floating button, shortcuts, local settings, and pronunciation.
- `tests`: Behavior tests, protocol fixtures, Windows integration, and WPF rendering checks.

Protocol tests use local response fixtures; providers have not each been verified with live cloud accounts. The current beta is an unsigned portable ZIP package.

## Official references

- [DeepSeek API](https://api-docs.deepseek.com/)
- [Claude Messages](https://platform.claude.com/docs/en/api/messages/create)
- [Gemini GenerateContent](https://ai.google.dev/api/generate-content)
- [.NET 10 support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)
- [Windows clipboard notifications](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-addclipboardformatlistener)
- [Windows registry change notifications](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regnotifychangekeyvalue)
- [WPF themes](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net90)
- [WPF WindowChrome](https://learn.microsoft.com/en-us/dotnet/api/system.windows.shell.windowchrome)
