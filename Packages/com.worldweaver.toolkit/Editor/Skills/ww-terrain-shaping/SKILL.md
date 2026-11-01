---
name: ww-terrain-shaping
description: Создание и правка Terrain с правильными координатами и сохранением декора.
category: world
keywords: terrain террейн ландшафт рельеф высот heightmap холм гора
tools: create_world,update_world,shape_terrain,create_cave,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/terrain/SKILL.md
license: MIT
mode: advisory
---

# Создание и правка Terrain с правильными координатами и сохранением декора

## Рабочий подход
1. Различай нормализованные высоты heightmap и мировые метры TerrainData.size.y; world XZ переводи относительно origin и размеров Terrain.
2. При правке общего TerrainData учитывай копирование и rollback. После изменения высот обнови только нужные привязанные декорации и NPC.
3. Проверка: фактическая высота в контрольных точках, границы мира, вода и объектные опоры соответствуют запросу; старый декор не потерян.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_world,update_world,shape_terrain,create_cave,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `691b695af062b27769c1f1bdc60a051c391eff0893c846102ecbc4873a26bd7a`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
