---
name: ww-script-review
description: Ревью C# на ответственность, API, единицы измерения и жизненный цикл.
category: code
keywords: CSharp C# review ревью код Rotate градус radians degrees deltaTime
tools: read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/scriptdesign/SKILL.md
license: MIT
mode: advisory
---

# Ревью C# на ответственность, API, единицы измерения и жизненный цикл

## Рабочий подход
1. Проверяй сигнатуры и единицы: Transform.Rotate принимает градусы; непрерывную скорость умножай на Time.deltaTime, физику согласуй с FixedUpdate.
2. Разделяй авторские значения, кэшированные ссылки и runtime-состояние. Убирай Find, LINQ и отражение из горячего пути только при реальной необходимости.
3. Проверка: скомпилированный скрипт ещё не доказательство механики. Измерь ожидаемый результат за заданное время или честно оставь поведенческую проверку невыполненной.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `b1ce164f3365ac92c84c753d9e495b82a7fc0bfdfccf7fad9de5f13ea4707f93`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
