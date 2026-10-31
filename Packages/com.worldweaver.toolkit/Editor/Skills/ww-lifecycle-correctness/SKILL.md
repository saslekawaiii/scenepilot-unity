---
name: ww-lifecycle-correctness
description: Unity lifecycle, destroyed-object null и симметрия подписок.
category: code
keywords: Awake Start OnEnable OnDisable OnDestroy lifecycle null жизненн цикл
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-lifecycle/SKILL.md
license: MIT
mode: advisory
---

# Unity lifecycle, destroyed-object null и симметрия подписок

## Рабочий подход
1. UnityEngine.Object использует особую проверку уничтоженного объекта: ?. и ?? не заменяют нужную Unity null-проверку.
2. Подписки обычно парны OnEnable/OnDisable; Destroy отложен, поэтому не используй объект как будто он уже исчез в том же кадре.
3. Проверка: disable/enable, unload и уничтожение не оставляют подписок, задач или поздних callback; порядок Awake не используется как случайный контракт.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `e034001e9aadb8bd5e329a6dac130e449f8552e74d4d7947b919a83d7f41aa55`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
