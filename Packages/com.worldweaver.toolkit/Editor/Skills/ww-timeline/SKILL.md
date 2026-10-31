---
name: ww-timeline
description: Timeline bindings, сигналы и восстановление управления после катсцены.
category: animation
keywords: Timeline PlayableDirector катсцен cinematic track binding signal
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/timeline/SKILL.md
license: MIT
mode: advisory
---

# Timeline bindings, сигналы и восстановление управления после катсцены

## Рабочий подход
1. Проверь timeline asset, scene bindings и владельца PlayableDirector. Track без binding не равен работающей катсцене.
2. На пропуске и завершении восстанови управление, камеру и отключённые компоненты. Сигнал не должен повторно выдавать награду после перемотки.
3. Проверка: обычное завершение, Skip и повторный запуск дают одинаковое конечное состояние сцены.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `a947218c7f0f7b7f8a128b77c5c55f95c4508684c5fc360926efc661a7447e61`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
