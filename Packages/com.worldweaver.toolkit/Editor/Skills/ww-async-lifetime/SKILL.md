---
name: ww-async-lifetime
description: Выбор coroutine или Task с отменой при уничтожении объекта.
category: code
keywords: async await coroutine корутин отмен lifecycle cancellation Task
tools: read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/async/SKILL.md
license: MIT
mode: advisory
---

# Выбор coroutine или Task с отменой при уничтожении объекта

## Рабочий подход
1. Для короткой Unity-последовательности достаточно coroutine. Task нужен для существующего асинхронного API; сторонний UniTask не добавляй автоматически.
2. Назови владельца операции, условия отмены и очистки. После await заново проверь отмену и существование объекта перед изменением сцены; Unity API вызывается на главном потоке.
3. Проверка: закрытие окна, смена сцены и Stop не приводят к позднему изменению, утечке подписок или зависшему статусу.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: read_script,draft_script,patch_script,attach_script,inspect_component,add_component,set_component. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `7d08503bc2cdb7fdc9134088471c805d3b2b25716fbbeec4f53266fa26fb23d5`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
