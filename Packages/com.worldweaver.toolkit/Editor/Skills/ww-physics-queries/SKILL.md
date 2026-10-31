---
name: ww-physics-queries
description: Raycast, overlap и NonAlloc без ошибок масок и неполного результата.
category: physics
keywords: raycast overlap spherecast NonAlloc LayerMask Physics запрос луч коллайдер
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-physics-queries/SKILL.md
license: MIT
mode: advisory
---

# Raycast, overlap и NonAlloc без ошибок масок и неполного результата

## Рабочий подход
1. Выбирай cast для пути и overlap для наличия в области. LayerMask — битовая маска; индекс слоя не равен маске.
2. Не полагайся на порядок hits, если API его не гарантирует. Для NonAlloc обрабатывай только возвращённое число элементов и переполнение буфера.
3. Проверка: луч изнутри collider, trigger filtering и целевой слой имеют отдельные примеры; попадание не приписывается первому произвольному объекту.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `9c3fbd5ef723f4515d4e085ab7d04cde5f9d85cf2cb983436fd94b6f168c1545`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
