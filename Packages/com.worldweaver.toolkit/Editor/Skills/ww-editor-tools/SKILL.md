---
name: ww-editor-tools
description: Custom Inspector и EditorWindow с SerializedObject и Undo.
category: editor
keywords: EditorWindow CustomEditor SerializedObject PropertyDrawer инструмент редактор
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-editor-tools/SKILL.md
license: MIT
mode: advisory
---

# Custom Inspector и EditorWindow с SerializedObject и Undo

## Рабочий подход
1. Editor-код отделён от runtime. Для сериализованных полей используй SerializedObject/SerializedProperty, чтобы сохранить multi-edit, prefab overrides и Undo.
2. Не выполняй тяжёлую генерацию на каждом OnGUI или OnValidate. Временные listeners и ресурсы освобождаются при закрытии окна.
3. Проверка: Undo восстанавливает значение, multi-selection не повреждает другие объекты, повторное открытие окна не дублирует события.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `ea836546b71e19625631b357f514cdaa53a542350aff87c497ce43af58364e5c`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
