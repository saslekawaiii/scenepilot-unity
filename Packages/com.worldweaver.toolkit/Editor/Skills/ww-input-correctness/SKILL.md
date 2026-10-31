---
name: ww-input-correctness
description: Input System с различием нажатия, удержания и режимов ввода.
category: gameplay
keywords: InputSystem InputAction triggered IsPressed WasPressedThisFrame ввод клавиш мыш кнопк
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-input-correctness/SKILL.md
license: MIT
mode: advisory
---

# Input System с различием нажатия, удержания и режимов ввода

## Рабочий подход
1. Проверь установленный Input System и существующий режим проекта. Одно нажатие, удержание и завершение interaction требуют разных проверок.
2. Игровой и UI action map переключаются явно; подписки и Enable/Disable принадлежат определённому объекту. Не используй InputValue вне callback.
3. Проверка: кнопка срабатывает один раз, меню не пропускает действие в мир, повторное включение игрока не удваивает callbacks.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `a9d6d8109cbf7b0611d53c162dae40c4200a41d003bbb25f0676ea17e357cc7b`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
