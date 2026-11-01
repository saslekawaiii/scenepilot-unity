---
name: ww-scene-contracts
description: Проверяемый контракт сцены, зависимостей и запуска.
category: scene
keywords: scene contract контракт сцен bootstrap camera listener requirements
tools: create_node,set_parent,set_transform,find_scene_objects,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/scene-contracts/SKILL.md
license: MIT
mode: advisory
---

# Проверяемый контракт сцены, зависимостей и запуска

## Рабочий подход
1. Перечисли обязательные корни, камеру, AudioListener, ссылки и ожидаемый порядок старта. Отличай обязательное от необязательного.
2. При additive-загрузке определи, кто владеет общими службами и выгружает ресурсы. Нельзя считать объект найденным только по неоднозначному имени.
3. Проверка: сцена запускается самостоятельно и после повторной загрузки; отсутствующие зависимости дают конкретное сообщение, а не цепочку NullReference.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,set_parent,set_transform,find_scene_objects,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `a31a3fb76b1ed4c96123e0f7f7fb42a0b1e8e31a20ed505ed23a352aa2e673bb`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
