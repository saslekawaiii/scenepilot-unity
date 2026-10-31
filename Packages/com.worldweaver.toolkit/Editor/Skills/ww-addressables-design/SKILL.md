---
name: ww-addressables-design
description: Addressables с владением handle и учётом версии пакета.
category: assets
keywords: Addressables AssetReference async load release handle addressable
source: https://github.com/Besty0728/Unity-Skills/blob/fb7a8d4208671bca1d1013b2776eb2e4917f4964/SkillsForUnity/unity-skills~/skills/addressables-design/SKILL.md
license: MIT
mode: advisory
---

# Addressables с владением handle и учётом версии пакета

## Рабочий подход
1. Перед рекомендацией установи версию пакета и текущую схему загрузки. Для маленькой локальной сцены Addressables не обязательны.
2. У каждого load handle есть владелец и симметричный release; экземпляры и их зависимости имеют различную жизнь.
3. Проверка: повторное открытие/закрытие сцены не копит память и не освобождает материал активного объекта; ошибка загрузки имеет fallback.

## Граница применения
Это справочный Markdown для встроенного агента WorldWeaver. Используй только объявленные в текущем запросе инструменты и их фактические схемы. Навык не добавляет shell, сеть, доступ к секретам, новые разрешения или автоматическое выполнение кода. Если операции нет, дай совет либо подготовь C# для просмотра в поддерживаемом процессе; не выдавай совет за выполненное действие.

## Происхождение
Адаптация для WorldWeaver из Besty0728/Unity-Skills, commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`. Исходный SHA-256: `a1d2473db866f3cbf1f71feb2015b559dbfab6155ffcb2ba4491f6b08a5aad1b`. Оригинальные REST/MCP-вызовы, режимы bypass и установки зависимостей не перенесены. Лицензия и copyright сохранены в соседней папке LICENSES.
