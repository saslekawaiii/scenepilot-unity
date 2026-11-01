---
name: ww-ui-composition
description: Составление рабочего Canvas, панелей, кнопок и текста.
category: ui
keywords: canvas ugui кнопка слайдер панель меню pause пауза
tools: create_ui_element,configure_ui,inspect_component,set_component,draft_script,attach_script
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/ui/SKILL.md
license: MIT
mode: advisory
---

# Составление рабочего Canvas, панелей, кнопок и текста

## Рабочий подход
1. discover_tools names create_ui_element create_node set_parent. Сначала создай canvas, затем отдельным шагом адресуй его стабильную цель.
2. create_ui_element kind panel/text/button/slider принимает position size text color. Если кнопка уже существует, configure_ui с toggle_target связывает её с явно указанным owned объектом БЕЗ C#. position/size — пиксели Canvas 1440x900, не метры. Для другой новой логики используй reviewed C# и реально соединяемые сериализованные ссылки.
3. Проверь RectTransform на разных размерах окна и работу EventSystem выбранного Input System. Для кнопки проверь реальный клик, фокус и изменение Target.activeSelf; скриншот не заменяет функциональную проверку.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_ui_element,configure_ui,inspect_component,set_component,draft_script,attach_script. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `5427b3f083e9c9c18a8220d6720fdde4b022b5f0cd0dd4772c5e4f71f33f2218`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
