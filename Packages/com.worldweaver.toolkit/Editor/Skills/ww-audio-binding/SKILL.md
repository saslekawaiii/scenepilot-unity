---
name: ww-audio-binding
description: Подключение имеющегося звука с громкостью и пространственным смешением.
category: audio
keywords: звуковой spatial audioclip volume loop фоновая музыка шаги
tools: configure_audio,search_project,inspect_component
source: https://github.com/Nice-Wolf-Studio/unity-claude-skills/blob/a149d3e995420432f52b0899092daa4b069f441f/skills/unity-audio/SKILL.md
license: MIT
mode: advisory
---

# Подключение имеющегося звука с громкостью и пространственным смешением

## Рабочий подход
1. discover_tools names search_project configure_audio. Найди импортированный AudioClip, используй реальный путь ассета, не выдуманный URL.
2. configure_audio устанавливает volume loop spatial и playOnAwake. Для интерфейса spatial false; для источника в мире обычно true. Сохрани чужие AudioListener.
3. Проверь clip и пространственные настройки; прослушивание необходимо для утверждения о слышимости и балансе. Не заявляй, что API синтеза звука подключён без настоящего результата.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: configure_audio,search_project,inspect_component. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Nice-Wolf-Studio/unity-claude-skills, commit `a149d3e995420432f52b0899092daa4b069f441f`. Исходный SHA-256: `f17e25c439615bd7afaddd412f1831e076e5563e286f99c278c92f91bab61fd9`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
