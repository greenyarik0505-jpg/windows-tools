# Як ми працюємо

## Потік роботи

```
issue  →  гілка від main  →  коміти  →  PR (draft)  →  CI зелений  →  ревʼю  →  squash-merge  →  main
                                                                                              ↓
                                                              release-please оновлює «Release PR»
                                                                                              ↓
                                                     мердж Release PR → тег vX.Y.Z → збірка → GitHub Release
```

1. Кожна задача має issue. Взяв — признач себе.
2. `git switch main && git pull && git switch -c feat/audio-per-app-volume`
3. Відкрий draft-PR якомога раніше, щоб інші бачили, над чим ти працюєш.
4. Перед запитом ревʼю: онови гілку від `main` (`git rebase origin/main` або кнопка Update branch).
5. Потрібен 1 апрув; для змін у `Core`, `Interop`, `App`, CI — апрув власника з `CODEOWNERS`.
6. Мерджить автор PR кнопкою **Squash and merge**. Гілка видаляється автоматично.

## Гілки

| Гілка | Призначення | Живе |
|---|---|---|
| `main` | завжди робочий стан, джерело релізів | постійно |
| `feat/<scope>-<опис>` | нова можливість | до 2–3 днів |
| `fix/<scope>-<опис>` | виправлення | до 1 дня |
| `refactor/…`, `docs/…`, `ci/…`, `build/…`, `chore/…`, `test/…`, `perf/…` | за типом | коротко |
| `release-please--…` | створює бот, руками не чіпати | до релізу |

Довгих гілок і `develop` немає. Незавершена велика фіча мерджиться частинами й ховається:
модуль не зареєстрований у списку або позначений `IsExperimental`.

## Коміти — Conventional Commits

```
<type>(<scope>): <summary в наказовому способі, з малої, без крапки, до 72 символів>

<тіло: що і чому, необовʼязково>

<футери: Closes #12, BREAKING CHANGE: ...>
```

| type | коли | версія |
|---|---|---|
| `feat` | нова можливість для користувача | minor |
| `fix` | виправлення багу | patch |
| `perf` | швидше/менше памʼяті без зміни поведінки | patch |
| `refactor` `docs` `test` `build` `ci` `chore` | без зміни поведінки | — |
| `revert` | відкат | patch |
| `!` після scope або `BREAKING CHANGE:` | зламана сумісність налаштувань/даних | major (до 1.0 — minor) |

Scope: `app` `core` `interop` `audio` `monitor` `switcher` `snap` `clipboard` `capture` `island`
`cleaner` `tools` `deps` `release` `repo`.

Приклади:

```
feat(audio): add per-app volume sliders to tray flyout
fix(clipboard): keep history when an image copy fails
perf(switcher): reuse DWM thumbnails between invocations
feat(core)!: move settings to per-module JSON files
```

Оскільки мердж — squash, у `main` потрапляє **заголовок PR**. Його перевіряє CI (`PR title`).
Коміти всередині гілки бажано теж у цьому форматі, але це не блокує.

## Pull request

- Одна тема, орієнтир — до ~400 змінених рядків. Більше — діли.
- Заповни шаблон: що, навіщо, як перевірено, скріншот/гіф для UI.
- Ревʼюер відповідає протягом одного робочого дня. Коментарі: `blocking:` / `nit:` / `question:`.
- Конфлікти вирішує автор. Чужі гілки не force-push.

## Релізи

Версії — SemVer, керує [release-please](https://github.com/googleapis/release-please).

- Після кожного мерджу в `main` бот оновлює **Release PR** з новою версією та `CHANGELOG.md`.
- Реліз = мердж цього PR (вирішує реліз-менеджер тижня). Бот ставить тег `vX.Y.Z` і створює GitHub Release;
  той самий workflow збирає `win-x64` і `win-arm64` та прикріплює архіви.
- До `1.0.0` живемо на `0.x`: `feat` піднімає minor, `fix` — patch.
- Ритм: реліз раз на 2 тижні в кінці спринту; хотфікс — позачергово через звичайний `fix/…` PR.
- Вручну **не** редагуємо: `CHANGELOG.md`, `version.txt`, `.release-please-manifest.json`, теги.
- Примусово задати версію: коміт із футером `Release-As: 0.5.0`.

## Перше налаштування машини

- Windows 11, Visual Studio 2022+ з навантаженням **WinUI application development** (або .NET 10 SDK + VS Code).
- `git config core.autocrlf true` (за замовчуванням у Git for Windows).
- Не тримай робочу копію в теці, що синхронізується OneDrive: він блокує файли в `bin/obj` і ламає збірку.
