# Архітектура

## Огляд

```
┌──────────────────────── WindowsTools.App (WinUI 3) ────────────────────────┐
│  трей-іконка · флайаут · вікно налаштувань · composition root (DI)          │
└───────────────┬────────────────────────────────────────────────────────────┘
                │ реєструє та вмикає/вимикає
┌───────────────▼───────────────┐
│  Modules.*  (по проєкту кожен) │   Audio · Monitor · Switcher · Snap · Clipboard
│  не знають один про одного     │   Capture · Island · Agents · Input · Display · Cleaner · Tools
└───────────────┬───────────────┘
                │ залежать лише від
┌───────────────▼───────────────┐      ┌─────────────────────────────┐
│  WindowsTools.Core             │─────▶│  WindowsTools.Interop       │
│  IModule, ISettingsStore,      │      │  CsWin32, COM-обгортки      │
│  IHotkeyService, IHookService, │      │  єдине місце з unsafe       │
│  ITrayHost, логування          │      └─────────────────────────────┘
└────────────────────────────────┘
```

## Контракт модуля (заморожено у фазі 0)

```csharp
public sealed record ModuleMetadata(
    string Name,
    string Description,
    string? IconGlyph = null,
    bool IsExperimental = false,
    bool RequiresAdmin = false,
    bool EnabledByDefault = false);

public interface IModule
{
    string Id { get; }                       // "audio", стабільний, використовується в налаштуваннях
    ModuleMetadata Metadata { get; }         // назва, опис, іконка, IsExperimental, RequiresAdmin, EnabledByDefault
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
| Agents | журнали сесій CLI-агентів у профілі користувача (також у WSL), named pipe для подій хуків, Credential Manager для токенів |
| Input | низькорівневі хуки миші й клавіатури, `SendInput`, Raw Input для тачпада |
| Display | DDC/CI (`SetVCPFeature`), WMI-яскравість, `Windows.Devices.Radios` |
| Cleaner | Known Folders, реєстр Uninstall, `winget` CLI для оновлень |
| Tools | `SetThreadExecutionState` (keep-awake), піпетка через `GetPixel`/capture |

## Рішення, які вже прийнято

- **Unpackaged**, а не MSIX: потрібні глобальні хуки, запуск helper-процесу з admin, вільне розташування файлів.
- **Один процес** + опційний elevated helper. Модулі — не плагіни з динамічним завантаженням, а проєкти в solution.
- **Без Rust/C++** на старті. Перегляд — лише через ADR із замірами.

Нові суттєві рішення фіксуємо як `docs/adr/NNNN-назва.md` (контекст → рішення → наслідки).

## Як додати Win32 API

1. Додайте точну назву API до `src/WindowsTools.Interop/NativeMethods.txt`. CsWin32 згенерує
   типізований виклик і, для ресурсів із відомим способом звільнення, варіант із `SafeHandle`.
2. Створіть невелику публічну обгортку в `WindowsTools.Interop`, яка приймає та повертає типи
   .NET або типи з `Windows.Win32`, а не оголює деталі генератора споживачам.
3. Передавайте SafeHandle до інших Win32-викликів там, де це підтримується; не копіюйте нативні
   хендли в `IntPtr` і не звільняйте їх вручну. Для API, де генератор не має варіанта SafeHandle,
   додайте власний похідний `SafeHandle` в `Interop` з відповідним згенерованим методом звільнення.
4. Перевіряйте чисту збірку `WindowsTools.Interop` для x64 та ARM64 і тестуйте керовану
   обгортку в `tests/WindowsTools.Interop.Tests`. Не додавайте ручні `[DllImport]`.
