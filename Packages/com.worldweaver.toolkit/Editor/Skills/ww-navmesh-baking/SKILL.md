---
name: ww-navmesh-baking
description: Подготовка NavMesh, размеры агента и полная достижимость маршрута.
category: npc
keywords: NavMesh bake surface agent radius obstacle навмеш навигац маршрут
tools: create_npc,update_npc,configure_npc_ai,set_navigation,validate_world
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/navmesh/SKILL.md
license: MIT
mode: advisory
---

# Подготовка NavMesh, размеры агента и полная достижимость маршрута

## Рабочий подход
1. До bake определи walkable geometry, agent type, radius, height, slope и step. Присутствие NavMeshAgent не означает, что под ним есть NavMesh.
2. Проверяй путь до цели и status полного пути; частичный путь нельзя выдавать за достижение. Для динамической преграды различай carving и обход.
3. Проверка: агент размещён на mesh, обходит препятствие и достигает цели в допуске; недостижимая цель возвращает понятную причину.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_npc,update_npc,configure_npc_ai,set_navigation,validate_world. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `1ecf63d1d82a5ff8dcb2d690bf2dace74bafcfae49135d39567b55378d1c3e45`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
