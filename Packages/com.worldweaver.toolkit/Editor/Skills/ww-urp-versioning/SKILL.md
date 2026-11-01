---
name: ww-urp-versioning
description: URP Renderer Features и параметры с проверкой версии установленного пакета.
category: render
keywords: URP Universal RendererFeature renderer feature renderScale SSAO
tools: discover_tools,list_skills,read_skill,inspect_object
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/urp/SKILL.md
license: MIT
mode: advisory
---

# URP Renderer Features и параметры с проверкой версии установленного пакета

## Рабочий подход
1. Уточни версию com.unity.render-pipelines.universal, тип renderer и используемый asset. API меняется между версиями Unity.
2. Для feature выясни поддерживаемые injection points и зависимости. Не выдавай исходный код неизвестного package API за гарантированно компилируемый.
3. Проверка: C# и renderer asset принимают feature, эффект виден в нужной камере и не ломает остальные камеры.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: discover_tools,list_skills,read_skill,inspect_object. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `e6db7b65a4f319a1877d625e639398630c306048f3f0aebbd7daeee670c5d4a7`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
