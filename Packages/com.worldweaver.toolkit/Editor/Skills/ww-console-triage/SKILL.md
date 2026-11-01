---
name: ww-console-triage
description: Разбор реальных ошибок Console с сохранением первого сбоя.
category: debug
keywords: Console exception stacktrace error лог ошибк консоль CS1503
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/console/SKILL.md
license: MIT
mode: advisory
---

# Разбор реальных ошибок Console с сохранением первого сбоя

## Рабочий подход
1. Начни с первой релевантной ошибки и полного stack trace. Поздние NullReference могут быть следствием единственной ошибки инициализации.
2. Сохрани сообщение и контекст до очистки Console. Отделяй compiler, runtime, package и сетевые ошибки, чтобы не менять не связанный код.
3. Проверка: повтори конкретный триггер после исправления и убедись, что первичная ошибка исчезла без подавления логирования.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `ef7e42c18aa8a8674fb687ec69b4d011f1c9d9b3c5a27fb8d539e7184e3f7858`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
