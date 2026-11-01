---
name: ww-mesh-authoring
description: Построение собственной геометрии через вершины, индексы и UV.
category: geometry
keywords: mesh вершины индексы геометрия мост арка лестница мостик
tools: create_node,create_mesh,set_transform,verify_scene
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/gameobject/SKILL.md
license: MIT
mode: advisory
---

# Построение собственной геометрии через вершины, индексы и UV

## Рабочий подход
1. Вызови discover_tools с names create_mesh create_node set_transform. Разбей сложный объект на читаемые части; предел одного меша 2048 вершин и 12000 индексов.
2. В create_mesh передай реальные vertices и triangles; порядок обхода определяет лицевую сторону. Добавь UV по одной паре на вершину и collider для проходимой геометрии.
3. В следующем шаге verify_scene kind mesh должен прочитать созданный MeshFilter и совпадающий MeshCollider. Для лестницы дополнительно проверь реальные габариты и движение в Play.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: create_node,create_mesh,set_transform,verify_scene. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `4583427893d4031550f65a888b028c348b0fbe7e8efee5f85157cbaa6098daa8`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
