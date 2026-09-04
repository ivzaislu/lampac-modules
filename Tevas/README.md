# Tevas

Онлайн-источник **TEVAS** для Lampac. Поддерживает фильмы и сериалы, использует зеркала источника и работает со **`streamproxy = true`**.

> **Важно:** для работы **Tevas нужен прокси с IP РФ**. Запросы к источнику должны выходить в интернет через российский IP.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Поведение

- Для аниме **`Invoke`** возвращает `null`.
- Для остальных материалов возвращается **`new(conf)`**.
- Модуль подключает **`tevas`** к глобальному поиску Lampac.
- Для фильмов и сериалов используется отдельная логика поиска и выбора потока.
- Поддерживается `checksearch`.

## Глобальный поиск

**`with_search.Add("tevas")`**.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "tevas"`** → **` ~ 720p`**.

## Конфигурация

Секция в `init.conf`: **`Tevas`** (`ModuleConf`).

Значения по умолчанию:

- основной host: **`https://pult.tevas.dev`**;
- зеркала: **`https://pult.tevas.dev`**, **`https://tevas.team`**, **`https://tevas.tech`**;
- CDN фильмов: **`bigsgppgs.tevas.dev`**;
- CDN сериалов: **`bigjjxjjs.tevas.dev`**;
- `referer`: **`https://pult.tevas.dev/`**;
- **`displayindex = 515`**;
- **`streamproxy = true`**;
- **`stream_access = "apk,cors,web"`**;
- **`httptimeout = 8`**;
- **`serial_cache_hours = 6`**.

Прокси с российским IP настраивается средствами Lampac для этого модуля/исходящих запросов.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/tevas`** | Основная выдача для фильмов и сериалов; также используется для `checksearch`. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`ModuleConf.cs`**.

Модуль загружается динамически согласно **`manifest.json`**.
