# Архітектура

## Огляд

```
┌──────────────────────── WindowsTools.App (WinUI 3) ────────────────────────┐
│  трей-іконка · флайаут · вікно налаштувань · composition root (DI)          │
└───────────────┬────────────────────────────────────────────────────────────┘
                │ реєструє та вмикає/вимикає
┌───────────────▼───────────────┐
│  Modules.*  (по проєкту кожен) │   Audio · Monitor · Switcher · Snap · Clipboard
│  не знають один про одного     │   Capture · Island · Cleaner · Tools
└───────────────┬───────────────┘
                │ залежать лише від
┌───────────────▼───────────────┐      ┌─────────────────────────────┐
│  WindowsTools.Core             │─────▶│  WindowsTools.Interop       │
│  IModule, ISettingsStore,      │      │  CsWin32, COM-обгортки      │
│  IHotkeyService, IHookService, │      │  єдине місце з unsafe       │
│  ITrayHost, логування          │      └─────────────────────────────┘
└────────────────────────────────┘
```

## Контракт модуля (чернетка, фіналізується у фазі 0)

```csharp
public interface IModule
{
    string Id { get; }                       // "audio", стабільний, використовується в налаштуваннях
    ModuleMetadata Metadata { get; }         // назва, опис, іконка, IsExperimental, RequiresAdmin
    void ConfigureServices(IServiceCollection services);
    Task StartAsync(CancellationToken ct);   // ставить хуки, таймери, вікна
    Task StopAsync(CancellationToken ct);    // звільняє ВСЕ; після цього модуль коштує нуль
}
```

Модуль додатково може надати: сторінку налаштувань, секцію у флайауті трея, гарячі клавіші,
«активність» для Island. Усе це — через інтерфейси `Core`, а не прямі посилання на `App`.

## Наскрізні сервіси Core

| Сервіс | Навіщо |
|---|---|
| `ISettingsStore` | JSON у `%AppData%\WindowsTools\`, окремий файл на модуль, версіонування схеми |
| `IHotkeyService` | реєстрація глобальних гарячих клавіш, виявлення конфліктів між модулями |
| `IHookService` | один низькорівневий хук клавіатури/миші та WinEvent на весь застосунок |
| `ITrayHost` | іконка, контекстне меню, флайаут |
| `IActivityHub` | модулі публікують «активності» (таймер, запис екрана), Island їх показує |
| Логування | `Microsoft.Extensions.Logging`, файл із ротацією, без персональних даних |

## Windows API за модулями

| Модуль | Основні API |
|---|---|
| Audio | Core Audio: `IAudioSessionManager2`, `ISimpleAudioVolume`, `IMMDeviceEnumerator` |
| Monitor | PDH/perf counters, DXGI/`D3DKMT` для GPU, LibreHardwareMonitorLib для температур і вентиляторів (admin) |
| Switcher | `EnumWindows`, DWM thumbnails (`DwmRegisterThumbnail`), `SetForegroundWindow` |
| Snap | `SetWinEventHook`, `SetWindowPos`, `MonitorFromWindow`, DPI-aware координати |
| Clipboard | `AddClipboardFormatListener`, формати text/html/image/files |
| Capture | `Windows.Graphics.Capture`, Media Foundation для запису, `Windows.Media.Ocr`, Win2D для анотацій |
| Island | завжди-зверху layered-вікно, `GlobalSystemMediaTransportControlsSessionManager`, `UserNotificationListener` |
| Cleaner | Known Folders, реєстр Uninstall, `winget` CLI для оновлень |
| Tools | `SetThreadExecutionState` (keep-awake), піпетка через `GetPixel`/capture |

## Рішення, які вже прийнято

- **Unpackaged**, а не MSIX: потрібні глобальні хуки, запуск helper-процесу з admin, вільне розташування файлів.
- **Один процес** + опційний elevated helper. Модулі — не плагіни з динамічним завантаженням, а проєкти в solution.
- **Без Rust/C++** на старті. Перегляд — лише через ADR із замірами.

Нові суттєві рішення фіксуємо як `docs/adr/NNNN-назва.md` (контекст → рішення → наслідки).
