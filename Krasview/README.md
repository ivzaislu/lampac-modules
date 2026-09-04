# Krasview

Онлайн-источник **Krasview** для Lampac. Модуль ищет фильмы и сериалы по нескольким связанным хостам, предпочитает HLS и использует **`streamproxy = true`**.

## Интерфейс

**`IModuleLoaded`**, **`IModuleOnline`**.

## Поведение

- **`Invoke`** всегда возвращает **`new(conf)`**.
- Для фильмов и сериалов выполняет поиск по названию и году, затем выбирает подходящую карточку источника.
- Для проверки доступности поддерживает `checksearch`.
- Кэш обнаружения источника принудительно держится коротким: **`cache_ttl = 1`**.

## Глобальный поиск

Отдельного **`with_search.Add(...)`** в `ModInit` нет.

## Подпись качества

**`OnlineApiQuality`**: при **`e.balanser == "krasview"`** → **` ~ 1080p`**.

## Конфигурация

Секция в `init.conf`: **`Krasview`** (`ModuleConf`).

Значения по умолчанию:

- основной/search host: **`https://hlamer.ru`**;
- фильмы: **`https://smartkino.ru`**;
- сериалы: **`https://sersoap.ru`**;
- `stream_referer`: **`https://smartkino.ru/`**;
- **`prefer_hls = true`**;
- **`match_year_tolerance = 1`**;
- **`cache_ttl = 1`**;
- **`displayindex = 545`**;
- **`streamproxy = true`**;
- **`stream_access = "apk,cors,web"`**.

## HTTP

| Маршрут | Назначение |
|---------|------------|
| **`lite/krasview`** | Основная выдача для фильмов и сериалов; также используется для `checksearch`. |

## Файлы

**`ModInit.cs`**, **`Controller.cs`**, **`Model.cs`**, **`ModuleConf.cs`**.

Модуль загружается динамически согласно **`manifest.json`**.
