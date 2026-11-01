---
name: ww-custom-mechanics
description: Новые игровые механики через компилируемые компоненты вместо отказа.
category: code
keywords: новая механика произвольный функционал умение способность кастомный
tools: read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-foundations/SKILL.md
license: MIT
mode: advisory
---

# Новые игровые механики через компилируемые компоненты вместо отказа

## Рабочий подход
1. Ищи штатные компоненты и существующий код через discover_tools list_skills read_script inspect_component. Не превращай отсутствие готового пресета в отказ.
2. Для отсутствующей механики draft_script содержит один полный небольшой файл, complete false. Код проходит просмотр и реальную компиляцию; затем attach_script связывает его с существующим узлом.
3. Проверь события, зависимости, включение и выключение компонента и наблюдаемое поведение. До результата компиляции запрещено считать импорт и подключение завершёнными.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `14d5fc71d21a26c7cd1a60383cd4e1f5d3d9c868d781bfbf8c4d1e66cee79133`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
