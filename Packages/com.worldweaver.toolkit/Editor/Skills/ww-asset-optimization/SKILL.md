---
name: ww-asset-optimization
description: Оптимизация текстур и mesh по измеренному бюджету и платформе.
category: performance
keywords: optimization texture mesh memory LOD оптимизац текстур меш
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/optimization/SKILL.md
license: MIT
mode: advisory
---

# Оптимизация текстур и mesh по измеренному бюджету и платформе

## Рабочий подход
1. Сначала установи источник расхода памяти или draw calls. Отдельно оцени mesh, текстуры, материалы и дубликаты.
2. Изменяй размер, compression, LOD или static flags только для подходящих ресурсов. Static не подходит движущемуся NPC; качество нельзя оценить одним размером файла.
3. Проверка: до/после измерены на одной сцене и настройках; визуально важные элементы и коллизии сохранились.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `6918beccee0bbd9851d91b273762b38ee2e8c8cc80144b38df135aff419fa364`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
