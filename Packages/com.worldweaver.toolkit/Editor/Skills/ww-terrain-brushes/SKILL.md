---
name: ww-terrain-brushes
description: Локальное изменение ландшафта кистями без переписывания генератора.
category: world
keywords: кисть кратер воронка насыпь выравнивание crater hill flatten
tools: create_world,update_world,shape_terrain,create_cave,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/terrain/SKILL.md
license: MIT
mode: advisory
---

# Локальное изменение ландшафта кистями без переписывания генератора

## Рабочий подход
1. Получай реальные цели через find_scene_objects и inspect_object. discover_tools names shape_terrain открывает штатную кисть с метрами в world XZ.
2. shape_terrain operation crater создает чашу и обод; hill поднимает холм; flatten выравнивает круг по высоте его центра. Не путай amount в метрах с нормализованным TerrainSpec.
3. Проверяй высоты и склоны в изменённой области; наличие записи кратера само по себе не доказывает нужную форму. Геометрию пещеры добавляй после скульптинга.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_world,update_world,shape_terrain,create_cave,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `691b695af062b27769c1f1bdc60a051c391eff0893c846102ecbc4873a26bd7a`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
