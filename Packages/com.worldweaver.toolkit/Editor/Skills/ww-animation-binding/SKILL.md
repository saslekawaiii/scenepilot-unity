---
name: ww-animation-binding
description: Подключение реальных моделей, контроллеров и авторских клипов.
category: character
keywords: риг skinned rig idle sprint прыжок blendtree controller
tools: create_animated_player,configure_animator,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/animator/SKILL.md
license: MIT
mode: advisory
---

# Подключение реальных моделей, контроллеров и авторских клипов

## Рабочий подход
1. discover_tools names create_animated_player configure_animator search_project. Для встроенного героя используй импортированный Kenney rig; для другого ассета сначала найди реальный FBX и контроллер.
2. configure_animator связывает существующий AnimatorController. Если контроллер или клипы отсутствуют, явно оставь этот этап незавершённым и предложи полный код/ассет в поддерживаемом процессе.
3. Проверяй SkinnedMeshRenderer.bones, реальные клипы и изменение костей при ходьбе/беге. Не выдавай Animator без состояний за готовую анимацию.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_animated_player,configure_animator,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `cf55d9ff600ee979f53563836112e7ff4c369ae133bf61ee47748fb68c38b657`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
