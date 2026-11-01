---
name: ww-capability-discovery
description: Поиск доступных действий, когда не подходит ни один известный пресет.
category: agent
keywords: возможности инструменты capabilities discovery неизвестный умеешь любой
tools: plan_task,discover_tools,list_skills,read_skill,verify_scene
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/workflow/SKILL.md
license: MIT
mode: advisory
---

# Поиск доступных действий, когда не подходит ни один известный пресет

## Рабочий подход
1. Не делай вывод о полном отсутствии возможности по сокращённой схеме. discover_tools query открывает категории terrain objects characters npc materials physics ui survival code navigation presentation project.
2. Можно указать names для точных команд. Выполни чтение отдельно, затем используй фактические definitions следующего шага. Параллельно list_skills/read_skill дают проверенные рецепты.
3. Если прямой команды нет, собери решение из узлов, мешей, компонентов и reviewed C#. Отсутствующий сторонний сервис обозначь явно; не объявляй задачу выполненной без действий.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: plan_task,discover_tools,list_skills,read_skill,verify_scene. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `1a0f4b953ae0a99c56a575ad26cd234247b7a1e678bfa53aa58e557d639e81ca`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
