---
name: ww-animator-states
description: Animator Controller, параметры и переходы с реальными именами и условиями.
category: animation
keywords: Animator animation анимац transition blend trigger bool controller
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/animator/SKILL.md
license: MIT
mode: advisory
---

# Animator Controller, параметры и переходы с реальными именами и условиями

## Рабочий подход
1. Прочитай реальные параметры и состояния до изменения. Различай trigger, bool, int и float; не посылай trigger с именем состояния.
2. Для переходов согласуй exit time, interruption и blend duration. Не создавай лишний controller поверх настроенного персонажа.
3. Проверка: idle→walk→idle и прерывание атаки идут по нужным переходам, параметры обновляются без предупреждений и зависания.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `cf55d9ff600ee979f53563836112e7ff4c369ae133bf61ee47748fb68c38b657`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
