# lampac-modules

Внешний репозиторий онлайн-модулей для **Lampac / Lampac NextGen**.

Модули лежат в корне репозитория отдельными каталогами и загружаются Lampac как динамические модули через `manifest.json`.

## Модули

| Модуль | Качество | Глобальный поиск | Особые требования |
|--------|----------|------------------|--------------------|
| [**FanCDN.2.0**](./FanCDN.2.0/) | `~ 1080p` | Нет отдельного `with_search` | **Cookie FanSeries + Playwright** |
| [**Kinobadi**](./Kinobadi/) | `~ 1080p` | Да | — |
| [**Krasview**](./Krasview/) | `~ 1080p` | Нет отдельного `with_search` | — |
| [**Tevas**](./Tevas/) | `~ 720p` | Да | **Нужен прокси с IP РФ** |

Подробности по каждому балансеру находятся в его собственном `README.md`.

## Установка в Lampac

Создайте или отредактируйте файл:

```text
module/repository.yaml
```

Добавьте репозиторий:

```yaml
- repository: https://github.com/ivzaislu/lampac-modules
  branch: main
  modules:
    - FanCDN.2.0
    - Kinobadi
    - Krasview
    - Tevas
```

Если `module/repository.yaml` уже существует и в нём подключены другие внешние репозитории, просто добавьте этот блок новой записью списка.

После сохранения перезапустите Lampac, чтобы загрузчик внешних репозиториев прочитал конфигурацию и установил/обновил модули.

### Установить только отдельные модули

В `modules:` можно оставить только нужные каталоги. Например, только FanCDN.2.0:

```yaml
- repository: https://github.com/ivzaislu/lampac-modules
  branch: main
  modules:
    - FanCDN.2.0
```

### Установить все модули репозитория

`modules` является необязательным параметром. Если его не указывать, Lampac может установить все доступные модули из репозитория:

```yaml
- repository: https://github.com/ivzaislu/lampac-modules
  branch: main
```

Репозиторий публичный, поэтому для обычной установки `token` не требуется.

## Настройка модулей

Параметры модулей переопределяются в `init.conf`. Имена секций совпадают с именами модулей:

- `FanCDN.2.0`
- `Kinobadi`
- `Krasview`
- `Tevas`

Дефолтные параметры и маршруты описаны в README каждого модуля.

### FanCDN.2.0

Для **FanCDN.2.0** нужны авторизованная cookie FanSeries и включённый Playwright. Модуль по умолчанию выключен, поэтому его нужно включить в `init.conf`:

```json
"FanCDN.2.0": {
  "enable": true,
  "cookie": "dle_user_id=<value>; dle_password=<value>"
}
```

Не публикуйте реальные значения cookie.

Маршрут модуля:

```text
/lite/fancdn.2.0
```

### Tevas и прокси

Для **Tevas обязателен выход через российский IP**. Настройте для него прокси РФ средствами Lampac; без российского IP источник может быть недоступен.

## Структура репозитория

```text
lampac-modules/
├── FanCDN.2.0/
│   ├── README.md
│   ├── manifest.json
│   └── ...
├── Kinobadi/
│   ├── README.md
│   ├── manifest.json
│   └── ...
├── Krasview/
│   ├── README.md
│   ├── manifest.json
│   └── ...
├── Tevas/
│   ├── README.md
│   ├── manifest.json
│   └── ...
└── README.md
```

## Обновление

Обновления модулей публикуются в ветку **`main`** этого репозитория. Если репозиторий уже указан в `module/repository.yaml`, перезапуск Lampac — самый простой способ применить актуальную версию модулей.

## License

Весь репозиторий распространяется по лицензии **MIT**. См. [LICENSE](./LICENSE).
