---
name: ww-inspector-authoring
description: Понятные поля Inspector, ссылки, диапазоны и подсказки.
category: editor
keywords: Inspector serializefield tooltip range min инспектор настройки поля
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/inspector/SKILL.md
license: MIT
mode: advisory
---

# Понятные поля Inspector, ссылки, диапазоны и подсказки

## Рабочий подход
1. Используй SerializeField private для настраиваемых полей; Header и Tooltip добавляй, когда они объясняют назначение или единицы.
2. Обязательные соседние компоненты выражай через RequireComponent. OnValidate выполняет только лёгкую локальную проверку и не запускает сеть, генерацию или тяжёлый поиск.
3. Проверка: новый компонент работает с безопасными defaults; отрицательная скорость и пустая обязательная ссылка выявляются до PlayMode.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `85f3ad9adbbd7bae40258675b6dea9e8a70aa36674a0cd7653d2b40814dfba44`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
