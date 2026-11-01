---
name: ww-import-settings
description: Настройки текстур, моделей и аудио с учётом платформы и стоимости reimport.
category: assets
keywords: importer texture audio model compression reimport импорт текстур
tools: catalog_assets,search_project,instantiate_prefab,material_search,material_import,material_apply
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/importer/SKILL.md
license: MIT
mode: advisory
---

# Настройки текстур, моделей и аудио с учётом платформы и стоимости reimport

## Рабочий подход
1. Сначала определи тип importer, целевую платформу, назначение и текущие значения. Для normal map, sprite и обычной albedo нужны разные настройки.
2. Группируй изменения перед одним reimport. Не меняй все ресурсы проекта ради одного объекта и не сжимай исходник без сохранённого основания.
3. Проверка: результат импортирован без ошибок, визуальное назначение сохранено, размер и качество измерены после изменения.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: catalog_assets,search_project,instantiate_prefab,material_search,material_import,material_apply. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `193f29271ef4c6a8b8040edd70558212d224420d8f48d6cf8441404814966004`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
