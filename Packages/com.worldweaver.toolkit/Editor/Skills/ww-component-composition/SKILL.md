---
name: ww-component-composition
description: Сборка нового поведения из существующих зарегистрированных компонентов.
category: code
keywords: composition компонование привязки сериализованные поля ссылки
tools: create_node,read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/component/SKILL.md
license: MIT
mode: advisory
---

# Сборка нового поведения из существующих зарегистрированных компонентов

## Рабочий подход
1. discover_tools names add_component inspect_component set_component create_node. Выбирай существующий зарегистрированный тип, не выдумывай названия классов или полей.
2. После add_component прочитай реальные serialized property names через inspect_component. set_component меняет только настоящее редактируемое поле; для ссылки задавай target и component явно.
3. Проверь read-back каждой ссылки и отсутствие Missing Script. Произвольные методы не вызываются; новую доменную логику реализует скомпилированный компонент.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `ab218c450bf84bf75bf0abacab3caf6d122170fadba7c5706c133894565a810d`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
