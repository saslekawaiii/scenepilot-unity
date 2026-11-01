---
name: ww-quests-level-events
description: Квесты, триггеры, encounters, checkpoints и возврат после катсцены.
category: gameplay
keywords: quest квест задан триггер trigger checkpoint respawn encounter
tools: create_node,draft_script,attach_script,configure_survival,create_resource
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-level-design/SKILL.md
license: MIT
mode: advisory
---

# Квесты, триггеры, encounters, checkpoints и возврат после катсцены

## Рабочий подход
1. Каждый триггер имеет владельца, фильтр игрока, одноразовость или явное повторение. Условия квеста и награда отделены от текста диалога.
2. Checkpoint хранит достаточное состояние для восстановления; NPC-цель ссылается на стабильный объект и проверяемое условие, а не только название.
3. Проверка: повторный вход в trigger не дублирует награду; смерть/respawn и загрузка сохраняют согласованное состояние задач.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,draft_script,attach_script,configure_survival,create_resource. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `8f7c237fbf614de61be7ac730200078347d40fb56f88ee69129461bcad7fde97`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
