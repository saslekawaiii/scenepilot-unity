---
name: ww-data-versioning
description: Конфигурация и runtime-данные с версионированием и миграцией.
category: data
keywords: data config JSON version migration конфигурац данные баланс
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-data-driven/SKILL.md
license: MIT
mode: advisory
---

# Конфигурация и runtime-данные с версионированием и миграцией

## Рабочий подход
1. Выбери ScriptableObject для авторских assets и DTO для переносимых данных; изменяемое состояние не смешивай с балансом.
2. Формат имеет версию, проверку диапазонов, fallback отсутствующего поля и явное правило миграции. Не доверяй произвольному type name из JSON.
3. Проверка: старая запись мигрирует предсказуемо, неверные числа отклоняются, изменение config не портит текущую сессию незаметно.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `265571f28c466c5b9726aa5f96de97ec0b68c7be798d71ec2ddae42fa319344b`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
