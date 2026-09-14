# SP-BCFier

Плагин Autodesk Revit и автономный Windows Viewer для работы с замечаниями в формате [BCF](https://github.com/buildingSMART/BCF-XML). Форк [BCFier](https://github.com/teocomi/BCFier) с поддержкой современных версий Revit, BCF 3.0 и доработками под ежедневную работу с замечаниями.

В интерфейсе продукт отображается как **«Замечания BCF»**.

| | |
|---|---|
| **Revit** | 2022, 2023, 2025, 2026 |
| **Windows Viewer** | автономное приложение без Revit |
| **BCF** | чтение 1.0–3.0, запись 2.1 или 3.0 |
| **Языки** | русский, английский |
| **Версия** | 1.0.18 |

---

## Возможности

### Общие (Revit и Windows Viewer)

- Создание, открытие, сохранение и объединение отчётов `.bcf` / `.bcfzip`
- Drag and drop файлов, несколько отчётов во вкладках
- Замечания: статус, тип, приоритет, метки, исполнитель, срок, описание
- Виды (viewpoints): снимок, комментарии, список компонентов
- Настройки автора, списков статусов/типов и версии записи BCF

### Только Revit

- Виды с камеры 3D, привязка элементов, Section Box ↔ BCF `ClippingPlanes`
- Выбор и выделение элементов модели, сопоставление по Id / IfcGuid
- Фильтр «только замечания активного документа»
- Режимы координат: Revit Z-up, Source Y-up, Source X-up

### Windows Viewer

- Просмотр и редактирование BCF без установленного Revit
- Добавление видов из файла изображения (Browse / drag and drop)

### Типичный сценарий (Revit)

1. Откройте панель **«Замечания BCF»** на ленте Revit.
2. Создайте или откройте BCF-файл.
3. Добавьте замечание или вид с текущего 3D-вида (снимок, элементы, section box).
4. Заполните статусы и комментарии.
5. Сохраните `.bcf` / `.bcfzip` и передайте коллегам: при открытии viewpoint восстанавливаются камера, section box и выделение.

Настройки хранятся в `%LocalAppData%\BCFier\settings.config`.

---

## Установка

### Установщик (рекомендуется)

1. Закройте Revit (если ставите add-in).
2. Запустите `dist\BCFier-SP-1.0.18-Setup.exe`.
3. Выберите компоненты:
   - **Add-in for Autodesk Revit 2022 / 2025**
   - **BCFier for Windows** (просмотр BCF без Revit)
4. Для Revit: запустите Revit, на ленте появится панель **«Замечания BCF»**.
5. Для Viewer: ярлык в меню «Пуск» (и опционально на рабочем столе) → `BCFier.Win.exe`.

Установка per-user, права администратора не нужны.

| Компонент | Куда ставится |
|-----------|----------------|
| Revit add-in | `%AppData%\Autodesk\Revit\Addins\{год}\` |
| Windows Viewer | `%LocalAppData%\BCFier-SP\` |

Исходник установщика: [`InnoSetup/SP-BCFier.iss`](InnoSetup/SP-BCFier.iss).

### ZIP / ручная установка

**Revit add-in** — архивы и папки в `dist\`:

- `BCFier-SP-1.0.18-R2022.zip`, `BCFier-SP-1.0.18-R2025.zip`
- папки `dist\R2022\`, `dist\R2025\`

Скопируйте содержимое папки года в `%AppData%\Autodesk\Revit\Addins\{год}\` (Revit должен быть закрыт).

**Windows Viewer** — папка `dist\Win\`:

1. Скопируйте содержимое `dist\Win\` в любую папку.
2. Запустите `BCFier.Win.exe`.

Удаление: удалите файлы add-in / Viewer или воспользуйтесь «Установка и удаление программ» (после Setup).

---

## Что изменилось относительно оригинального BCFier

Оригинальный [BCFier](https://github.com/teocomi/BCFier) архивирован в 2024 году. Этот форк продолжает Revit-часть и автономный Windows Viewer.

### Удалено

- модули Navisworks и Xbim Explorer
- старый monorepo-инсталлер upstream

### Добавлено и существенно доработано

- поддержка Revit **2022–2026** (net48 / net8.0-windows), совместимость `ElementId` int/long
- запись **BCF 3.0** (наряду с 2.1)
- round-trip **Section Box** через `ClippingPlanes`
- выбор и выделение элементов, обновление ссылок компонентов
- сопоставление элементов по Id, IfcGuid и Id в имени (удобно для файлов из Solibri / Navisworks)
- локализация **ru / en** (русский по умолчанию)
- отдельный проект add-in для Revit (`Bcfier.Revit.Standalone`): кнопка на ленте и манифест `.addin`
- обновлённый **Windows Viewer** (`Bcfier.Win`) на текущем UI и BCF 3.0
- SDK-style сборка, InnoSetup-установщик, unit-тесты round-trip BCF

---

## Сборка из исходников

**Solution:** `SP-BCFier.sln`

| Проект | Назначение |
|--------|------------|
| `Bcfier` | Ядро: BCF I/O, WPF UI, локализация |
| `Bcfier.Revit` | Интеграция с Revit |
| `Bcfier.Revit.Standalone` | Add-in (лента и `.addin`) |
| `Bcfier.Win` | Автономный Windows Viewer |
| `tests/Bcfier.Tests` | Тесты |

Конфигурации: **R2022 | R2023 | R2025 | R2026 | Win**.

Для Revit-проектов нужен установленный Autodesk Revit (пути к API в `Directory.Build.props`).

**Revit add-in**

1. Откройте `SP-BCFier.sln` в Visual Studio.
2. Выберите конфигурацию года Revit.
3. Соберите solution. DLL остаются в `bin\R{год}\{tfm}\` (Revit при этом может быть запущен).
4. В Addins по умолчанию ничего не копируется: можно держать установленную версию и подгружать сборку из `bin` через Addin Manager.
5. Чтобы один раз положить add-in в Revit (Revit лучше закрыть):

```text
dotnet build Bcfier.Revit.Standalone -c R2023 -p:DeployToRevit=true
```

или скрипт `dist\R{год}\Install-Standalone.ps1`.

Плагин SP ссылается на `Bcfier` / `Bcfier.Revit` через ProjectReference и забирает DLL из тех же `bin\R{год}\{tfm}\` — отдельный деплой в Addins для этого не нужен.

**Windows Viewer**

```text
dotnet build SP-BCFier.sln -c Win
```

Результат: `dist\Win\BCFier.Win.exe`.

Для отладки Revit укажите `revit.exe` нужной версии как стартовое действие.

---

## Лицензия

Основано на BCFier (Matteo Cominetti).

GNU General Public License v3 Extended: допускается использование как плагина несвободного Autodesk Revit.  
См. [GPL FAQ: Plugins](http://www.gnu.org/licenses/gpl-faq.en.html#GPLPluginsInNF).

Copyright (c) 2013–2016 Matteo Cominetti  
Доработки: Copyright (c) Канухин Александр

Программа распространяется без каких-либо гарантий. Полный текст лицензии: [GNU GPL v3](https://www.gnu.org/licenses/gpl-3.0.html).
