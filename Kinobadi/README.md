# Kinobadi

Онлайн-источник **Kinobadi** (`https://my.kinobadi.im`) для Lampac. Модуль работает через FEMD, использует **`streamproxy = true`** и подключается к глобальному поиску Lampac.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Поведение

- **`Invoke`** всегда возвращает **`new(conf)`**.
- При загрузке добавляет **`kinobadi`** в **`online.with_search`**.
- Для запросов к источнику подключает обработчик **`ProxyApiCreateHttpRequest`** через `Encoder`.

## Глобальный поиск

**`with_search.Add("kinobadi")`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "kinobadi"`** → **` ~ 1080p`**.

## Конфигурация

Секция в `init.conf`: **`Kinobadi`** (`OnlinesSettings`).

По умолчанию:

- домен: **`https://my.kinobadi.im`**;
- **`streamproxy = true`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/kinobadi`** | Основная выдача, поиск и формирование карточки фильма/сериала. |
| **`lite/kinobadi/resolve`** | Разрешение карточки Kinobadi/FEMD в служебную модель. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Model.cs`**, **`Services/Encoder.cs`**, **`Services/FemdInvoke.cs`**.

Модуль загружается динамически согласно **`manifest.json`**.
