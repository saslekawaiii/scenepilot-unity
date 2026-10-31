---
name: ww-ugui-layout
description: UGUI Canvas, anchors и масштабируемая вёрстка игровых экранов.
category: ui
keywords: UGUI Canvas RectTransform anchors button кнопк меню интерфейс
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/ui/SKILL.md
license: MIT
mode: advisory
---

# UGUI Canvas, anchors и масштабируемая вёрстка игровых экранов

## Рабочий подход
1. Начинай с существующего Canvas и CanvasScaler; не создавай второй EventSystem без причины. Выбери anchors и pivot под ожидаемый resize.
2. Для списков используй LayoutGroup с контролируемым ContentSizeFitter; избегай циклического пересчёта размеров родителя и ребёнка.
3. Проверка: кнопки доступны мышью и клавиатурой, текст не обрезан на двух aspect ratios, canvas имеет нужную сортировку.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `5427b3f083e9c9c18a8220d6720fdde4b022b5f0cd0dd4772c5e4f71f33f2218`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
