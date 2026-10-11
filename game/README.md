# game/ — игра на Unity

Проект Unity: этап И2 в SPEC — Вторая битва при Пикшарпе за одним компьютером.

- Редактор: **Unity 6000.6.4f1**. Шаблон — Universal 2D: URP с 2D-рендером, Input System.
- Правила боя приходят из `core/` (C#-библиотека без Unity). Они подключены локальным пакетом
  `com.journal.battlecore` в `Packages/manifest.json`: `"file:../../core/Package"`.
  Unity компилирует исходники движка сам; тесты движка — по-прежнему `cd core && dotnet run --project Tests`.
- Рисунок бойцов, земли и построек — тот же, что в полигоне (`core/Tests/polygon-men.js`, `core/Tests/polygon.html`,
  решения В1–В12 в SPEC). Полигон рисует части в атласы PNG, Unity собирает из них бойцов.

## Как открыть

Unity Hub → Add → Add project from disk → эта папка `game/`. При первом открытии Unity соберёт `Library/`
(в git не идёт) — пару минут.

## Что в git

`Assets/`, `Packages/` (manifest и lock), `ProjectSettings/`. Служебные `Library/`, `Logs/`, `UserSettings/`,
`Temp/`, сборки и файлы решений Visual Studio — в `.gitignore`.
