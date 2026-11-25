# Одноразове налаштування репозиторію

Виконує **власник** репозиторію (потрібні права admin). Усе, що нижче, не можна зробити комітом.

## 1. Скрипт

```powershell
./scripts/setup-repo.ps1
```

Він вмикає: лише squash-мердж (заголовок PR = коміт), автовидалення гілок, дозвіл Actions створювати PR,
мітки для issue та ruleset із `.github/rulesets/main.json`:

- у `main` — лише через PR, 1 апрув, апрув code owner, усі треди закриті;
- обовʼязкові перевірки: `Build and test`, `Conventional PR title`;
- заборонено force-push і видалення `main`, лінійна історія.

> Rulesets і захист гілок у **приватних** репозиторіях доступні лише на платному плані (Pro/Team).
> На безкоштовному плані — або зробити репозиторій публічним, або правила тримаються на домовленості.

## 2. Токен для release-please

PR, створені стандартним `GITHUB_TOKEN`, не запускають CI — тож Release PR не пройде обовʼязкові перевірки.

1. Створити fine-grained PAT (або GitHub App) лише для цього репозиторію: **Contents: Read and write**,
   **Pull requests: Read and write**.
2. Settings → Secrets and variables → Actions → New repository secret → `RELEASE_PLEASE_TOKEN`.

Без секрету workflow працює зі стандартним токеном, але Release PR доведеться мерджити адміну в обхід правил.

## 3. Команда

- Додати 4 розробників як collaborators (роль Write).
- Вписати логіни в `.github/CODEOWNERS` і таблицю ролей у `docs/ROADMAP.md`.
- Створити milestones `v0.1.0` … `v0.4.0` і завести issue з фази 0.

## 4. Рішення, які ще треба прийняти

- **Ліцензія** (MIT / Apache-2.0 / GPL-3.0) — додати `LICENSE` до першого публічного релізу.
- **Підписання коду** — без сертифіката SmartScreen попереджатиме під час запуску.
- Хвилини Actions: у приватному репозиторії Windows-раннери тарифікуються з множником 2.
