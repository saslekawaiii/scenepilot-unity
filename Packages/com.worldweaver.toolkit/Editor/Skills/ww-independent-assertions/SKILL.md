---
name: ww-independent-assertions
description: Подтверждение сцены явными проверками после выполнения команд.
category: verification
keywords: assert assertions факт доказательство readback независимая проверка
tools: verify_scene,inspect_object,inspect_component,run_tests,poll_job,read_console
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/validation/SKILL.md
license: MIT
mode: advisory
---

# Подтверждение сцены явными проверками после выполнения команд

## Рабочий подход
1. discover_tools names verify_scene inspect_component read_console. Проверка выполняется отдельным шагом после создания, с реальными stable targets.
2. verify_scene checks поддерживает component active mesh terrain_height path. Для component задай фактический тип; для path — существующую конечную цель. Неверное условие возвращает ошибку.
3. Проверяй смысл запроса отдельно от наличия компонентов. Для игрового поведения нужны настоящие Play измерения или тесты, для внешнего вида — реальный рендер; не придумывай численные результаты.

## Доступные действия WorldWeaver
При необходимости вызови discover_tools с names: verify_scene,inspect_object,inspect_component,run_tests,poll_job,read_console. Это ссылки на существующие команды, а не разрешение на их автоматическое выполнение. После создания читай реальный target, затем применяй зависимые действия отдельным шагом.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `42afdcabd37134561cb625242104d3c15fd4dcf21270ea11ee72dcd74374b835`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
