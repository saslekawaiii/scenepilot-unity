---
name: ww-graphics-settings
description: Графические настройки с пониманием pipeline, quality level и побочного влияния.
category: render
keywords: graphics quality render pipeline график качество рендер
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/graphics/SKILL.md
license: MIT
mode: advisory
---

# Графические настройки с пониманием pipeline, quality level и побочного влияния

## Рабочий подход
1. Сначала прочитай активный render pipeline и quality level. Настройка может быть общей для проекта или отдельной платформы.
2. Меняй минимальный набор параметров под целевой эффект или измеренную проблему. Не заменяй RenderPipelineAsset глобально ради косметической правки сцены.
3. Проверка: текущий и целевой quality level согласованы; визуальная сцена и выбранная сборка работают без missing shader.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `6e31f3faa72e039ed3f594d1513bba14f712e23c776c18a2c0879afbb806b463`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
