# Джерела даних AI-агентів на Windows

Результат spike #16. Звідки модуль `Agents` бере сесії, стан, токени, ліміти й вартість для
Claude Code, Codex, OpenCode та GitHub Copilot.

Перевірено 2026-11-27 на Windows 11 з реальними інсталяціями (Claude Code desktop/CLI, Codex
VS Code/CLI, OpenCode, Copilot CLI) та за офіційною документацією. Досліджувалась лише
**структура** файлів — вміст розмов не читався і в цей документ не потрапив.

## Головні висновки

1. **Документованих контрактів мало.** Стабільні лише хуки та статус-рядок Claude Code,
   app-server Codex і HTTP API OpenCode. Усе інше (журнали, SQLite) — внутрішні формати,
   які змінюються між версіями. Кожен парсер мусить переживати невідомі поля й таблиці.
2. **Найкращий канал для стану «працює / чекає / завершив» — хуки**, а не журнали.
   Журнали дають історію й токени, але із запізненням і без явного «чекає на тебе».
3. **Ліміти плану не лежать у файлах.** Claude віддає їх лише в статус-рядок, Codex — лише
   через свій app-server. Тож ліміти — окремий канал з окремою згодою користувача (#23).
4. **Copilot для фізичних осіб не має надійного API використання.** Провайдер Copilot (#22)
   лишається найнижчим пріоритетом.

## Матриця «агент × показник»

Надійність: **К** — документований контракт; **В** — внутрішній формат, може зламатися з оновленням;
**Е** — евристика; **—** — недоступно.

| Показник | Claude Code | Codex | OpenCode | Copilot |
|---|---|---|---|---|
| Активні сесії | реєстр `~/.claude/sessions/<pid>.json` (В) + хуки `SessionStart`/`SessionEnd` (К) | `state_5.sqlite` → `threads` (В); хуки (К) | `opencode.db` → `session` (В); HTTP API `opencode serve` (К) | — |
| Стан працює / простоює | реєстр: `status` = `busy` \| `idle` (В); хуки `UserPromptSubmit` → `Stop` (К) | `thread_history_1.sqlite` → `thread_turns.status` = `inProgress` (В) | події `session.idle` (v1) / `session.execution.succeeded` (v2) (К) | — |
| Чекає на користувача | хук `Notification`: `permission_prompt`, `agent_needs_input`, `idle_prompt`; хук `PermissionRequest` (К) | хук `PermissionRequest` (К) | подія `permission.updated` (К) | — |
| Поточна дія | журнал: останній блок `tool_use` → `name` (В); хук `PreToolUse` (К) | `thread_items.item_type` (`commandExecution`, `fileChange`, …) (В) | `part` з `type` = `tool` (В) | — |
| Проєкт / тека | реєстр `cwd`; журнал `cwd`, `gitBranch` (В) | `threads.cwd`, `git_branch` (В) | `session.directory` (В) | — |
| Модель | журнал `message.model` (В); статус-рядок `model.id` (К) | `threads.model` (В) | `session.model` (В) | — |
| Токени сесії | журнал `message.usage` (В) | `threads.tokens_used` — лише сума (В) | `session.tokens_input/output/reasoning/cache_read/cache_write` (В) | — |
| Вартість | рахуємо самі з токенів (#24); статус-рядок `cost.total_cost_usd` (К) | рахуємо самі | `session.cost` готове (В) | — |
| Ліміти плану | статус-рядок `rate_limits.five_hour/seven_day/spend_limit` (К) | app-server `account/rateLimits/read` (К) | — (залежить від провайдера моделі) | REST billing API — під питанням (див. нижче) |

## Claude Code

### Реєстр живих сесій — `%USERPROFILE%\.claude\sessions\<pid>.json`

Один файл на запущений процес. Поля (типи перевірено, значення — ні):
`pid`, `sessionId`, `cwd`, `status`, `statusUpdatedAt`, `startedAt`, `updatedAt`, `name`, `kind`,
`entrypoint`, `version`, `pidDomain`.

- `status` бачили лише `busy` та `idle`. Окремого «чекає дозволу» немає — його дають хуки.
- Часові поля — Unix-мілісекунди.
- Файл може лишитися після аварійного завершення: перевіряти, що `pid` живий **і** що
  `procStart` збігається з часом старту процесу (захист від перевикористання PID).
- `pidDomain` (наприклад, `win32:<host>`) відрізняє PID Windows від PID у WSL.
- Поруч лежать `<pid>.<hash>.key` — це секрети сесії. **Не читати.**

Це найдешевший спосіб отримати список активних сесій і стан busy/idle — без хуків і без
парсингу журналів. Формат внутрішній, тому потрібен запасний шлях через журнали.

### Журнали сесій — `%USERPROFILE%\.claude\projects\<slug-теки>\<sessionId>.jsonl`

JSON Lines, дописуються в кінець. Тип запису — поле `type`. Корисні для нас:

| `type` | Що беремо |
|---|---|
| `assistant` | `message.model`, `message.usage.{input_tokens, output_tokens, cache_creation_input_tokens, cache_read_input_tokens}`, `message.stop_reason`, блоки `content[]` з `type` = `tool_use` → `name` |
| `user` | `timestamp`, `cwd`, `gitBranch` — лише метадані, **не** `message.content` |
| `custom-title`, `agent-name` | назва сесії для показу |
| `system` | `subtype` = `compact_boundary` (стиснення контексту) |
| `pr-link` | номер і URL PR, відкритого в сесії |

Підводні камені:

- **Одне повідомлення моделі пишеться кількома рядками** (по рядку на блок вмісту, поле
  `apiBlockIndex`) з однаковим `usage`. Токени треба дедуплікувати за парою
  `message.id` + `requestId`, інакше сума буде завищена в кілька разів.
- Трапляється модель `<synthetic>` — це службові записи, у підрахунок не включати.
- Файли бувають великими: читати інкрементально від збереженого зсуву, не перечитувати.
- Записи невідомих типів пропускати без помилки — нові типи з'являються регулярно.

### Хуки — документований контракт

Документація: <https://code.claude.com/docs/en/hooks>. Налаштовуються в
`%USERPROFILE%\.claude\settings.json`. Є тип `"type": "http"` — Claude Code сам робить
POST на вказану адресу, тож **окремий скрипт-посередник не потрібен**.

Кожен хук отримує `session_id`, `transcript_path`, `cwd`, `permission_mode`, `hook_event_name`.
Нам потрібні події:

| Подія | Що означає для віджета |
|---|---|
| `SessionStart` / `SessionEnd` | сесія з'явилася / зникла |
| `UserPromptSubmit` | почав працювати |
| `PreToolUse` | поточна дія (назва інструмента) |
| `Notification` з `notification_type` = `permission_prompt`, `agent_needs_input`, `elicitation_dialog` | **чекає на тебе** |
| `Notification` з `idle_prompt` | давно простоює |
| `Stop` | завершив хід |
| `StopFailure` | хід завершився помилкою API |
| `SubagentStart` / `SubagentStop` | субагенти в межах сесії |

### Статус-рядок — єдине джерело лімітів плану

Документація: <https://code.claude.com/docs/en/statusline>. Команда статус-рядка отримує на
stdin JSON з полями `session_id`, `model.id`, `cost.total_cost_usd`, `context_window.*` та
`rate_limits.five_hour`, `rate_limits.seven_day`, `rate_limits.spend_limit`
(`used_percentage` 0–100, `resets_at` — Unix-секунди).

- `rate_limits` є лише в підписників Pro / Max / Ultra і **лише після першої відповіді моделі** в сесії.
- Налаштування `statusLine` одне на користувача. Якщо користувач уже має свій статус-рядок,
  наш має бути обгорткою: переслати JSON застосунку й викликати його команду далі.
- Вікна лімітів спільні для всіх сесій акаунта: беремо найсвіжіше значення з будь-якої сесії.

## Codex

На цій машині Codex зберігає дані в SQLite, а тека `~/.codex/sessions` порожня — старі
журнали `rollout-*.jsonl` мігровано. Номери у файлах (`state_5`, `thread_history_1`) —
версії схеми, тож **шлях до файлу теж може змінитися**.

### `%USERPROFILE%\.codex\state_5.sqlite` → таблиця `threads`

Корисні колонки: `id`, `cwd`, `title`/`name`, `model`, `model_provider`, `tokens_used`,
`created_at_ms`, `updated_at_ms`, `archived`, `git_branch`, `source`, `approval_mode`.

- `source`: `cli`, `exec`, `vscode` — головні треди; JSON з `subagent` — субагенти
  (`guardian`, `thread_spawn` з `parent_thread_id`). Субагентів групувати під батьківським тредом.
- `tokens_used` — лише загальна сума, без розбивки на вхід/вихід/кеш. Для точної вартості
  потрібна розбивка з подій `token_count` (див. app-server).
- **Не читати** колонки `first_user_message` і `preview` — це вміст розмови.

### `%USERPROFILE%\.codex\thread_history_1.sqlite`

- `thread_turns.status`: `inProgress`, `completed`, `failed`, `interrupted` — стан треду.
- `thread_items.item_type` (`commandExecution`, `fileChange`, `mcpToolCall`, `webSearch`, …) —
  поточна дія. `item_json` містить вміст команд — брати лише `item_type`.
- Файл великий (на тестовій машині ~4 ГБ). Відкривати **тільки на читання**, у режимі WAL,
  з короткими запитами за індексом і ніколи не тримати транзакцію довго.

### App-server — документований контракт

Документація: <https://learn.chatgpt.com/docs/app-server>. `codex app-server` говорить JSON-RPC
через stdio. Метод `account/rateLimits/read` повертає `primary`/`secondary` з `usedPercent`,
`windowDurationMins`, `resetsAt`; сервер також шле сповіщення `account/rateLimits/updated`.

Це запуск дочірнього процесу Codex з авторизацією користувача, тож Codex сам ходить у мережу.
Вмикати лише за явною згодою, як і будь-який мережевий канал (правило 7 в `AGENTS.md`).

### Хуки

Codex теж має хуки (`SessionStart`, `PermissionRequest`, `SessionEnd`, …) — документація
<https://www.codex-docs.com/en/docs/hooks>. Перед реалізацією #20 звірити повний перелік подій
і формат входу з документацією актуальної версії.

## OpenCode

### `%USERPROFILE%\.local\share\opencode\opencode.db`

Шлях однаковий на Windows і Linux. Таблиця `session` уже містить усе для віджета:
`directory`, `title`, `model`, `agent`, `cost`, `tokens_input`, `tokens_output`,
`tokens_reasoning`, `tokens_cache_read`, `tokens_cache_write`, `time_created`, `time_updated`
(мілісекунди), `parent_id` (субагенти), `time_archived`.

- Поточна дія — таблиця `part`, поле `data.type` = `tool`; вміст `data` не зберігати.
- `session_input.prompt`, `message.data`, `todo.content` — вміст розмови, **не читати**.
- `account`, `control_account`, `credential` містять токени доступу — **не відкривати ці таблиці взагалі**.

### HTTP API та події — документований контракт

`opencode serve` піднімає HTTP API з потоком подій (<https://opencode.ai/docs/api>). Події
сесії: у v1 — `session.idle`, `session.updated`, `permission.updated`; у v2 «хід завершено»
перейменовано на `session.execution.succeeded`. Провайдер має підтримувати обидві назви.

## GitHub Copilot

- Локально (`%USERPROFILE%\.copilot`) є лише `config.json`, lock-файли IDE та логи процесів.
  Даних про сесії, токени чи квоту там немає.
- REST API: є ендпоінт звіту про premium requests для користувача
  (<https://docs.github.com/en/rest/billing/usage>), але в обговореннях GitHub повідомляють,
  що для індивідуальних планів він працює не завжди. **Потрібна перевірка з реальним
  fine-grained токеном** перед початком #22.
- Агентний режим Copilot у VS Code локально не логує нічого придатного. Активність показувати не будемо.

## WSL

Встановлені дистрибутиви видно через `wsl.exe -l -q`. Ті самі файли лежать за шляхами
`\\wsl$\<дистрибутив>\home\<користувач>\.claude\…`, `…\.codex\…`, `…\.local\share\opencode\…`.

- `FileSystemWatcher` на `\\wsl$` ненадійний — опитувати з інтервалом.
- PID сесій у WSL не є PID Windows: перевіряти живучість лише в межах свого `pidDomain`.
- Не звертатися до `\\wsl$`, якщо дистрибутив зупинений — це його запустить.

## Приватність і безпека

- Читаємо **лише метадані**: стан, час, теку, модель, назви інструментів, токени. Тексти
  промптів, відповідей, команд і диффів не читаємо, не логуємо й не зберігаємо.
- Не відкриваємо файли й таблиці з секретами: `.credentials.json`, `auth.json`,
  `sessions\*.key`, таблиці `account`/`credential` OpenCode.
- Усі файли відкриваються тільки на читання і зі спільним доступом
  (`FileShare.ReadWrite | FileShare.Delete`), щоб не блокувати агента.
- Локальний HTTP-ендпоінт для хуків: лише `127.0.0.1`, випадковий порт, секретний заголовок
  у конфігурації хука. Інакше будь-яка сторінка в браузері могла б слати підроблені події.
- Встановлення хуків і обгортки статус-рядка змінює конфігурацію агента користувача: лише
  за явною згодою, з показом диффу, резервною копією та кнопкою «прибрати» (#19).

## Що з цього випливає для наступних задач

| Задача | Рішення |
|---|---|
| #17 контракт | Провайдер повертає потік подій, а не знімки; стан сесії будує `AgentHub`. Кожне значення має позначку надійності (точне / оцінка) |
| #18 Claude Code | Спершу реєстр `sessions\*.json` (список і busy/idle), потім журнали (токени, модель, поточна дія) з дедуплікацією |
| #19 події Claude | HTTP-хуки на локальний ендпоінт замість named pipe — Claude Code підтримує це сам, без скриптів |
| #20 Codex | `threads` + `thread_turns` у SQLite тільки на читання; app-server — опційно для лімітів |
| #21 OpenCode | Таблиця `session` дає токени й вартість одразу; події — через `opencode serve`, якщо запущений |
| #22 Copilot | Відкласти до перевірки REST API з реальним токеном |
| #23 ліміти | Claude — обгортка статус-рядка; Codex — app-server. Обидва за окремою згодою |
| #27 WSL | Ті самі провайдери з іншим коренем шляху й опитуванням замість watcher-а |
