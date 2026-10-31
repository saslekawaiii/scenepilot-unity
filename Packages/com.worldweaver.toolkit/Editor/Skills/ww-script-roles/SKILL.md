---
name: ww-script-roles
description: Выбор MonoBehaviour, ScriptableObject и обычного класса для новой механики.
category: code
keywords: MonoBehaviour script roles роли класс plain CSharp скрипт
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/script-roles/SKILL.md
license: MIT
mode: advisory
---

# Выбор MonoBehaviour, ScriptableObject и обычного класса для новой механики

## Рабочий подход
1. MonoBehaviour связывает сцену и Unity lifecycle. Чистый класс хранит алгоритм или правило, которому не нужны Transform и кадры.
2. ScriptableObject хранит авторскую конфигурацию и общие данные; текущие HP, прогресс и временные цели относятся к экземпляру runtime.
3. Проверка: правило можно вызвать с простыми входными данными; два персонажа не разделяют изменяемые HP через один asset.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `c6c237632cb0f9ff4afc0a4e31ca146fd92ef301175cb990bb788053fd604541`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
