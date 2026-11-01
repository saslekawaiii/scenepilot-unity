---
name: ww-profiler-evidence
description: Сбор сравнимого профиля CPU, GC, памяти и рендеринга.
category: performance
keywords: profiler profiling CPU GPU memory counters профайл измерен
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/profiler/SKILL.md
license: MIT
mode: advisory
---

# Сбор сравнимого профиля CPU, GC, памяти и рендеринга

## Рабочий подход
1. Укажи устройство, сцену, число объектов, quality, Editor или Player, окно прогрева и длительность измерения.
2. Разделяй CPU, GPU, GC и память; среднее FPS может скрывать длинные кадры. Данные Editor не равны производительности релизной сборки.
3. Проверка: сохрани исходные показатели и сравнимый повтор после изменения. Если инструмента профилирования нет, не придумывай численные результаты.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `eb77613eb7a549f572b274e4f6f2af85ffc70d2dbacaa56e9ee80d5cadf03144`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
