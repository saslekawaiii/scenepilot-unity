---
name: ww-audio-spatial
description: 3D AudioSource, громкость, импорт и одно активное прослушивание.
category: audio
keywords: audio sound AudioSource AudioListener звук spatial rolloff микшер
tools: configure_audio,search_project,inspect_component
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-audio/SKILL.md
license: MIT
mode: advisory
---

# 3D AudioSource, громкость, импорт и одно активное прослушивание

## Рабочий подход
1. Выбери 2D или 3D sound и согласуй spatialBlend, min/max distance, rolloff и mixer group с масштабом мира.
2. В сцене должна быть осознанная активная AudioListener; не добавляй вторую к каждой камере. Частые звуки могут нуждаться в ограничении голосов.
3. Проверка: звук слышен в нужной дистанции, не клипует на одновременных событиях, pause и mute действуют на нужную группу.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: configure_audio,search_project,inspect_component. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `f17e25c439615bd7afaddd412f1831e076e5563e286f99c278c92f91bab61fd9`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
