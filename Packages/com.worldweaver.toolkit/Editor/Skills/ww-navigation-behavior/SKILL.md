---
name: ww-navigation-behavior
description: Runtime путь, перепланирование и остановка NPC на NavMesh.
category: npc
keywords: pathfinding destination pathPending remainingDistance patrol маршрут патрул ворот площадь
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-ai-navigation/SKILL.md
license: MIT
mode: advisory
---

# Runtime путь, перепланирование и остановка NPC на NavMesh

## Рабочий подход
1. До движения проверь наличие агента на NavMesh и вычисление полного пути. Пока pathPending, remainingDistance может не доказывать прибытие.
2. Разделяй принятие команды, начало движения и фактическое прибытие. Для следования ограничь частоту SetDestination и обрабатывай исчезновение цели.
3. Проверка: препятствие обходится, Stop прекращает движение, заблокированная цель возвращает failure; поведение не заменяется прямым Transform.MoveTowards сквозь стены.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `ba2b0e2d86e4f0c7535d3259e8644ec5c1931c1fddd337072d54b3bf76e7985d`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
