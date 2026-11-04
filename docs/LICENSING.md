# Лицензии и происхождение материалов

## Авторский код ScenePilot

В текущем репозитории общая лицензия авторского кода не назначена: отдельного корневого LICENSE нет, а `package.json` не объявляет SPDX-лицензию. Эта страница описывает файлы поставки. Решение о лицензии принимает автор проекта отдельно.

## Справочная MD-библиотека

Для адаптированных Markdown-навыков сохранены MIT notices исходных проектов:

- [Besty0728/Unity-Skills](https://github.com/Besty0728/Unity-Skills/tree/fb7a8d4208671bca1d1013b2776eb2e4917f4964), commit `fb7a8d4208671bca1d1013b2776eb2e4917f4964`; [копия лицензии](../Packages/com.worldweaver.toolkit/Editor/Skills/LICENSES/Besty0728-MIT.txt).
- [Nice-Wolf-Studio/unity-claude-skills](https://github.com/Nice-Wolf-Studio/unity-claude-skills/tree/a149d3e995420432f52b0899092daa4b069f441f), commit `a149d3e995420432f52b0899092daa4b069f441f`; [копия лицензии](../Packages/com.worldweaver.toolkit/Editor/Skills/LICENSES/Nice-Wolf-Studio-MIT.txt).

Точные source paths и SHA, русские адаптации и условия загрузки описаны в [реестре навыков](skills/README.md) и [THIRD-PARTY-NOTICES](../Packages/com.worldweaver.toolkit/Editor/Skills/THIRD-PARTY-NOTICES.md).

## Ассеты пользователя и зависимости

Встроенные 13 процедурных поверхностей и их 39 карт описаны в [реестре происхождения](../Packages/com.worldweaver.toolkit/Runtime/Materials/builtin-provenance.json); [условия CC0](../Packages/com.worldweaver.toolkit/Runtime/Materials/LICENSE.txt) относятся к этим изображениям. Для скачанных поверхностей Poly Haven импортёр сохраняет источник, лицензию и контрольные суммы в каталоге самого материала. [Устройство библиотеки](material-library.md).

Интерфейс диалогов использует **Cormorant Garamond** и **Noto Sans** из Google Fonts под SIL Open Font License 1.1. Сохранены исходные байты шрифтов, точные commits, SHA-256 и тексты лицензий: [реестр шрифтов](../Packages/com.worldweaver.toolkit/Resources/WorldWeaver/UI/Fonts/SOURCES.json), [Cormorant OFL](../Packages/com.worldweaver.toolkit/Resources/WorldWeaver/UI/Fonts/cormorantgaramond-OFL.txt), [Noto OFL](../Packages/com.worldweaver.toolkit/Resources/WorldWeaver/UI/Fonts/notosans-OFL.txt).

Для размещаемых prefabs и материалов сохраняются условия их исходной поставки; каталог ScenePilot не подтверждает их лицензию автоматически. Unity и подключаемые пакеты распространяются по своим условиям. Веса моделей не входят в репозиторий. Ссылки на научные и официальные источники не означают передачи прав на их содержимое.
