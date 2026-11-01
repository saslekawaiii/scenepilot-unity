---
name: ww-npc-context-routing
description: Соединение новых NPC с миром, моделью и свободным разговором.
category: npc
keywords: житель колонист диалоги свободный social спорить npc ai
tools: create_npc,update_npc,configure_npc_ai,set_navigation,validate_world
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-npc-behavior/SKILL.md
license: MIT
mode: advisory
---

# Соединение новых NPC с миром, моделью и свободным разговором

## Рабочий подход
1. discover_tools names create_npc configure_npc_ai update_npc set_navigation. Создай роль, знания и ограничения под текущий мир, без чужих шаблонов деревни.
2. configure_npc_ai assign_selected_model true связывает NPC с реально выбранной моделью; social включает ограниченный обмен. Движение требует существующей NavMesh и полного маршрута.
3. Проверь ссылки Knowledge Models Inventory WorldEntity, произвольную реплику игрока и работу при недоступном API. Текст модели не должен сам изменять награды и инвентарь.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_npc,update_npc,configure_npc_ai,set_navigation,validate_world. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `01a6af737203c4c5e0c8f7442ea3f676eb9eb967bc3d2fa7c76d21399cc42f53`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
