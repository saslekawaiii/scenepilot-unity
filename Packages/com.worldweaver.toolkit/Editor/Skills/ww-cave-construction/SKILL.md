---
name: ww-cave-construction
description: Доступные пещеры с отверстиями Terrain и собственными коллайдерами.
category: world
keywords: пещера туннель тоннель cave tunnel вход подземелье
tools: create_world,update_world,shape_terrain,create_cave,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/terrain/SKILL.md
license: MIT
mode: advisory
---

# Доступные пещеры с отверстиями Terrain и собственными коллайдерами

## Рабочий подход
1. discover_tools names create_cave shape_terrain inspect_object. Сначала закончи изменение Terrain, затем выбери свободную область внутри границ.
2. create_cave получает position в world XZ, width и length; инструмент возвращает entrance и interior с фактической высотой. Не заменяй пещеру чёрной сферой.
3. Проверь MeshCollider, отверстия Terrain, пространство для CharacterController и проход от входа внутрь. NPC использует только рассчитанный PathComplete, а не телепортацию.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_world,update_world,shape_terrain,create_cave,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `691b695af062b27769c1f1bdc60a051c391eff0893c846102ecbc4873a26bd7a`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
