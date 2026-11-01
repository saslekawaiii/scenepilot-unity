---
name: ww-physics-authoring
description: Сборка физических объектов и материалов без отдельного пресета игры.
category: physics
keywords: rigidbody отскок трение гравитация масса физическое падающий
tools: configure_physics,inspect_component,set_component,verify_scene
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/physics/SKILL.md
license: MIT
mode: advisory
---

# Сборка физических объектов и материалов без отдельного пресета игры

## Рабочий подход
1. discover_tools names create_node configure_physics inspect_component set_component. Создай объект или прочитай существующий Collider.
2. configure_physics настраивает mass gravity kinematic, добавляет один Collider и отдельный материал friction bounce. Не добавляй Rigidbody к CharacterController и динамический nonconvex MeshCollider.
3. Прочитай Rigidbody и Collider, затем запусти Play проверку падения и контакта; наличие компонентов не подтверждает поведение при столкновении.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: configure_physics,inspect_component,set_component,verify_scene. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `2ee3f66161280b34d8a91b03dd252251ff229b952aa0b36af3e3cfe50e57b236`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
