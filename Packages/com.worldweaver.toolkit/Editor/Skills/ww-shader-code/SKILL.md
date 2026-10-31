---
name: ww-shader-code
description: Генерация ShaderLab и HLSL с проверкой pipeline и целевой платформы.
category: render
keywords: shader HLSL ShaderLab шейдер compile keyword render
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/shader/SKILL.md
license: MIT
mode: advisory
---

# Генерация ShaderLab и HLSL с проверкой pipeline и целевой платформы

## Рабочий подход
1. Перед draft зафиксируй pipeline, pass, shader target, свойства и поддерживаемые платформы. Не смешивай include и макросы Built-in с URP/HDRP.
2. Делай минимальный материал-пример; не меняй глобальный pipeline ради исправления одного shader. Ограничивай варианты ключевых слов реальной нуждой.
3. Проверка: shader компилируется в используемом pipeline и сборке, а материал не розовый. Если проверка недоступна, пометь её явно.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `1374b4be5482eae193da644fcc29f727b3824aefa6dacee4d9146629b3680f2b`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
