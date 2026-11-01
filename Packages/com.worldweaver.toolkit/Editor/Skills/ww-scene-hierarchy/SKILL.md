---
name: ww-scene-hierarchy
description: Создание логических узлов и безопасная сборка иерархии.
category: scene
keywords: иерархия parent parenting дочерний узел группировка
tools: create_node,set_parent,set_transform,find_scene_objects,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/gameobject/SKILL.md
license: MIT
mode: advisory
---

# Создание логических узлов и безопасная сборка иерархии

## Рабочий подход
1. discover_tools names create_node set_parent inspect_object. Пустые узлы дают место для компонентов и привязок, а не видимость выполненной механики.
2. set_parent требует собственные объекты, запрещает циклы и сохраняет world transform. keep_world false сбрасывает локальную позицию и поворот.
3. Проверь parent и Transform после каждой перегруппировки, особенно если родитель масштабирован. Не переносись в другую сцену без разрешённого процесса открытия.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,set_parent,set_transform,find_scene_objects,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `4583427893d4031550f65a888b028c348b0fbe7e8efee5f85157cbaa6098daa8`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
