---
name: ww-cinemachine
description: Cinemachine follow и look-at с проверкой major version и приоритетов.
category: scene
keywords: Cinemachine follow camera orbit камера слежение virtual
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/cinemachine/SKILL.md
license: MIT
mode: advisory
---

# Cinemachine follow и look-at с проверкой major version и приоритетов

## Рабочий подход
1. Проверь установленную major version: Cinemachine 2 и 3 имеют разные классы и компоненты. Не создавай зависимость автоматически.
2. Разделяй Follow, LookAt, damping и активный приоритет; проверь CinemachineBrain на выходной камере.
3. Проверка: перемещение игрока меняет кадр плавно, препятствия и границы уровня учтены, возврат управления не оставляет две активные системы.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `c0a5a1efefef785ef8d6fa4f5d4d877dcc5d3a2c01df99b9d1a827bae999d61a`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
