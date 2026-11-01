---
name: ww-survival-loops
description: Цикл выживания с конечными запасами, восстановлением и поражением.
category: gameplay
keywords: выживание кислород кислорода survival oxygen укрытие экспедиция
tools: create_node,draft_script,attach_script,configure_survival,create_resource
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-game-loop/SKILL.md
license: MIT
mode: advisory
---

# Цикл выживания с конечными запасами, восстановлением и поражением

## Рабочий подход
1. discover_tools names configure_survival create_resource create_animated_player. Сначала создай Terrain и игрока или найди существующего; не дублируй героя.
2. configure_survival связывает игрока с world, собственную gravity и oxygen_seconds. Созданная база восстанавливает кислород и HP; голая пещера уменьшает воздействие, но кислород продолжает расходоваться.
3. Размести три sample и oxygen через create_resource. Проверь расход вне базы, восстановление внутри, сбор только с расстояния до 3 м, поражение при нулевых HP и возврат с образцами.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,draft_script,attach_script,configure_survival,create_resource. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `9622d509c57c98dd2a68e2e80a64c26aa5b274c44d8b25d523b4e1ed7a54144b`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
