---
name: ww-build-platforms
description: Сборка Player, платформа и defines с фиксацией фактического отчёта.
category: build
keywords: build Player platform Windows WebGL Android IL2CPP билд сборк
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-platforms/SKILL.md
license: MIT
mode: advisory
---

# Сборка Player, платформа и defines с фиксацией фактического отчёта

## Рабочий подход
1. Прочитай активную платформу, доступные modules, Build Settings или Build Profiles, список сцен и scripting backend.
2. Не обещай сборку для отсутствующего target module. Runtime-код не должен зависеть от UnityEditor; Editor success не равен Player success.
3. Проверка: BuildReport содержит успешный итог и ожидаемый output; запуск Player или smoke check выделяется отдельной проверкой.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `1dcaf8335d5893f0d7d83bbce34d73069e5113eb8af576eac653329a4f7ca641`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
