---
name: ww-scriptable-config
description: Настройка ScriptableObject как авторской конфигурации и безопасное клонирование.
category: data
keywords: ScriptableObject конфиг configuration asset CreateAssetMenu
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/scriptableobject/SKILL.md
license: MIT
mode: advisory
---

# Настройка ScriptableObject как авторской конфигурации и безопасное клонирование

## Рабочий подход
1. Перед изменением выясни, общий это asset или копия экземпляра. Не записывай состояние текущей игры в исходный общий config.
2. Поля конфигурации имеют безопасные значения и пределы. Не создавай сложный граф полиморфных assets, когда достаточно одного небольшого config.
3. Проверка: PlayMode не загрязняет авторский asset; независимые экземпляры могут использовать разные настройки без скрытого влияния.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `6ef5b9f2550178ddc273b03bbcbbcc5320c0828ce356d600e13bdb8ef1db7610`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
