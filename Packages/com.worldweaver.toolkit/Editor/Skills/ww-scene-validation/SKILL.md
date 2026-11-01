---
name: ww-scene-validation
description: Проверка missing scripts, ссылок, чисел и состояния перед сохранением.
category: scene
keywords: validation missing scripts references проверк валидац NaN infinity
tools: create_node,set_parent,set_transform,find_scene_objects,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/validation/SKILL.md
license: MIT
mode: advisory
---

# Проверка missing scripts, ссылок, чисел и состояния перед сохранением

## Рабочий подход
1. Проверяй missing components, обязательные ссылки, finite transform, активные камеры и зависимости сцены. Отчёт должен указывать конкретную цель.
2. Не исправляй массово все найденные проблемы без связи с запросом. Отсутствие текстового GUID-совпадения не доказывает отсутствие runtime-загрузки.
3. Проверка: после минимальной правки снова прочитай проблемный объект; непроверенные типы ресурсов не входят в заявленное покрытие.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,set_parent,set_transform,find_scene_objects,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `42afdcabd37134561cb625242104d3c15fd4dcf21270ea11ee72dcd74374b835`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
