# Personal Platform

Веб-приложение для личной продуктивности и финансов: привычки, задачи, планировщик дня и недели,
учёт доходов и расходов, заметки и дневник. Один пользователь видит только свои данные.

- **Сервер**: ASP.NET Core Web API на .NET 10 (LTS), EF Core 10 + PostgreSQL 17, ASP.NET Core Identity,
  FluentValidation, OpenAPI.
- **Клиент**: Blazor WebAssembly (C#), раздаётся тем же приложением (один origin, CORS не используется).
- **Развёртывание**: Docker (multi-stage, non-root, read-only), Docker Compose для разработки и production,
  nginx как TLS-прокси, отдельная задача миграций с правами владельца схемы.

> Статус: функциональность реализована и проверена автоматическими тестами (см. [docs/TESTING.md](docs/TESTING.md)).
> Перед выпуском в production нужно пройти [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md): часть проверок
> (просмотр алертов CodeQL, настоящий SMTP и TLS-сертификат на реальном домене, внешний pentest) ещё не
> выполнена. CI и E2E/DAST на GitHub Actions проходят.

## Возможности

| Модуль | Что умеет |
|---|---|
| Аккаунт | регистрация с подтверждением email, вход/выход, сброс и смена пароля, список сеансов и их отзыв, профиль (часовой пояс, язык ru/en, валюта, формат даты, первый день недели), удаление аккаунта со всеми данными |
| Привычки | ежедневные / по дням недели / раз в N дней, отметки по локальной дате пользователя, отмена, история, серии, процент выполнения, календарь, недельная и месячная статистика, цели и напоминания, архив |
| Задачи | статусы, приоритеты, срок и время, напоминания, категории и теги, подзадачи, поиск, фильтры и сортировка, представления «Сегодня / Предстоящие / Просроченные / Без срока / Завершённые», повторяющиеся задачи (правка экземпляра не меняет серию), перенос, массовые операции (до 100), постраничный вывод |
| Планировщик | день и неделя, временные блоки, привязка задач, перенос и изменение длительности (drag-and-drop), правило конфликтов (полуоткрытые интервалы; по умолчанию пересечения сохраняются и подсвечиваются как конфликт, в профиле их можно запретить — тогда сохранение отклоняется с 409), задачи без времени, корректная работа с часовыми поясами и DST |
| Финансы | счета и начальные остатки, доходы/расходы/переводы (в т.ч. между валютами с явной суммой зачисления), двухуровневые категории, бюджеты и план/факт, повторяющиеся операции, отчёты по месяцам и годам, движение денег, CSV-экспорт с защитой от формул, идемпотентное создание операций |
| Заметки и дневник | заметки с тегами, закреплением, архивом, полнотекстовым поиском, историей версий и автосохранением с обнаружением конфликтов; дневник по датам с настроением |
| Главная | сводка дня: задачи, привычки, план, финансы, напоминания |

## Быстрый старт (локально)

Требуется Docker (на macOS — Docker Desktop; образы собираются нативно и для Apple Silicon, и для Intel).
.NET SDK 10 нужен только для разработки и тестов.

```bash
# 1. Секреты для локального стенда (случайные, только для разработки; файл .env не коммитится)
./deploy/scripts/generate-dev-env.sh

# 2. Весь стенд: PostgreSQL, Mailpit, миграции, приложение
docker compose up --build
# приложение: http://localhost:8080, письма (подтверждение email и т.п.): http://localhost:8025
```

Запуск без контейнера приложения (для отладки): поднимите PostgreSQL с ролями из
`deploy/postgres/roles.sql`, примените миграции (`dotnet ef database update` с переменной
`PP_MIGRATIONS_CONNECTION` для роли `pp_owner`) и запустите `src/PersonalPlatform.Api` с
`ASPNETCORE_ENVIRONMENT=Development` и строкой подключения роли `pp_app` в user-secrets
(`dotnet user-secrets set ConnectionStrings:Postgres "..."`). В Development письма сохраняются в `.mail/`,
а JWT-ключ генерируется на время процесса.

## Тесты

```bash
dotnet build PersonalPlatform.slnx -c Release
dotnet test -c Release --no-build --project tests/PersonalPlatform.UnitTests
dotnet test -c Release --no-build --project tests/PersonalPlatform.ArchitectureTests
dotnet test -c Release --no-build --project tests/PersonalPlatform.IntegrationTests   # нужен Docker (Testcontainers)
dotnet test -c Release --no-build --project tests/PersonalPlatform.SecurityTests      # нужен Docker
dotnet test -c Release --no-build --project tests/PersonalPlatform.E2ETests           # Docker + Chromium (playwright.ps1 install chromium)
```

Нагрузочный тест (k6) и сканеры описаны в [docs/TESTING.md](docs/TESTING.md).

## Структура

```
src/
  PersonalPlatform.Domain          сущности и бизнес-правила (без зависимостей от инфраструктуры)
  PersonalPlatform.Contracts       DTO запросов/ответов, общие для API и клиента
  PersonalPlatform.Application     сценарии (сервисы), валидация, интерфейсы портов
  PersonalPlatform.Infrastructure  EF Core/PostgreSQL, Identity, JWT/сеансы, email, миграции
  PersonalPlatform.Api             HTTP API, безопасность, хостинг клиента
  PersonalPlatform.Client          Blazor WebAssembly UI
tests/
  PersonalPlatform.UnitTests, .ArchitectureTests, .IntegrationTests, .SecurityTests, .E2ETests
  PersonalPlatform.TestInfrastructure   общие фикстуры (Testcontainers PostgreSQL, хост API)
  load/                            нагрузочный сценарий k6
deploy/                            роли PostgreSQL, production compose, nginx, скрипты секретов и резервного копирования
docs/                              документация
```

## Документация

- [Архитектура](docs/ARCHITECTURE.md)
- [Безопасность](docs/SECURITY.md) и [модель угроз](docs/THREAT_MODEL.md)
- [API](docs/API.md)
- [База данных](docs/DATABASE.md)
- [Тестирование и фактические результаты](docs/TESTING.md)
- [Развёртывание](docs/DEPLOYMENT.md)
- [Чек-лист выпуска](docs/RELEASE_CHECKLIST.md)
- [План реализации и журнал](docs/IMPLEMENTATION_PLAN.md)
