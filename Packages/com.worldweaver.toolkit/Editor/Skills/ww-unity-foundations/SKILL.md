---
name: ww-unity-foundations
description: Объяснение базовых понятий Unity и transform перед точной небольшой правкой.
category: scene
keywords: basic beginner основы local world transform координат scene prefab
tools: create_node,set_parent,set_transform,find_scene_objects,inspect_object
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-foundations/SKILL.md
license: MIT
mode: advisory
---

# Объяснение базовых понятий Unity и transform перед точной небольшой правкой

## Рабочий подход
1. Различай GameObject, Component, prefab asset и экземпляр; объясняй только понятия, необходимые для текущего действия.
2. Положение зависит от родителя и пространства координат; scale влияет на детей и collider. Пользовательские метры нужно сопоставить с масштабом сцены.
3. Проверка: простая операция имеет наблюдаемый результат; объяснение не вызывает инструментальные изменения, которых пользователь не просил.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,set_parent,set_transform,find_scene_objects,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `14d5fc71d21a26c7cd1a60383cd4e1f5d3d9c868d781bfbf8c4d1e66cee79133`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
