# База данных

PostgreSQL 17, одна база `personal_platform`, все таблицы в схеме `app`, имена — snake_case.
Схема описывается моделью EF Core (`src/PersonalPlatform.Infrastructure/Persistence`) и единственной
миграцией `InitialSchema`. CI проверяет, что модель и миграции совпадают
(`dotnet ef migrations has-pending-model-changes`).

## Роли и права

Создаются скриптом `deploy/postgres/roles.sql` (выполняется при инициализации контейнера).

| Роль | Права | Кто использует |
|---|---|---|
| `postgres` | суперпользователь | только инициализация контейнера и резервное копирование (локальный сокет внутри контейнера) |
| `pp_owner` | владелец схемы `app`: DDL | задача миграций (`migrator`), однократно при развёртывании |
| `pp_app` | `USAGE` на схему, `SELECT/INSERT/UPDATE/DELETE` на таблицы (через `ALTER DEFAULT PRIVILEGES`), без DDL | приложение |

Для `pp_app` заданы `statement_timeout = 30s`, `lock_timeout = 10s`, `idle_in_transaction_session_timeout = 60s`.
`PUBLIC` лишён прав на базу и схему `public`. Попытка `CREATE TABLE` от `pp_app` завершается
`permission denied for schema app` (проверено на production-стенде; тестовая фикстура выполняет миграции
от `pp_owner`, а тесты — от `pp_app`).

## Таблицы

| Область | Таблицы |
|---|---|
| Identity | `users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `user_tokens`, `role_claims` |
| Сеансы | `user_sessions` (семейство токенов), `refresh_tokens` (только SHA-256 хеш, `consumed_at`) |
| Data Protection | `data_protection_keys` (XML ключей, зашифрованный сертификатом в production) |
| Профиль | `user_profiles` |
| Организация | `categories` (для задач и привычек), `tags` |
| Задачи | `tasks`, `subtasks`, `task_tags`, `task_series` |
| Привычки | `habits`, `habit_completions`, `quit_habits`, `quit_habit_relapses` (вредные привычки и срывы) |
| Планировщик | `time_blocks` |
| Финансы | `accounts`, `finance_categories`, `transactions`, `ledger_entries`, `budgets`, `recurring_transactions` |
| Заметки | `notes`, `note_tags`, `note_revisions`, `journal_entries` |

### Общие правила

- **Ключи** — UUIDv7, создаются в домене.
- **Владелец.** Каждая пользовательская таблица содержит `owner_id` с внешним ключом на `users(id)`
  `ON DELETE CASCADE` — удаление аккаунта удаляет все данные одной операцией.
- **Составные внешние ключи.** У владеемых таблиц есть альтернативный ключ `(id, owner_id)`, а ссылки между
  ними объявлены как `(ref_id, owner_id) → (id, owner_id)` (`ON DELETE NO ACTION`). Поэтому строка одного
  пользователя физически не может ссылаться на строку другого (`Database_rejects_cross_user_references_*`).
- **Версии.** Оптимистическая конкуренция по системному столбцу `xmin` (без отдельного столбца).
- **Время.** Мгновения — `timestamptz`, календарные даты — `date`, время суток — `time`.
- **Деньги.** `numeric(19,4)`; валюта — `character(3)`.
- **Поиск.** В `notes` и `journal_entries` — сгенерированный столбец `search_vector tsvector` (конфигурация
  `simple`) с GIN-индексом. Задачи и операции ищутся параметризованным поиском подстроки без учёта регистра
  по названию/описанию в пределах данных владельца.

### Ограничения целостности (выборка)

| Ограничение | Смысл |
|---|---|
| `ck_transactions_amount_positive`, `ck_transactions_destination_amount_positive` | суммы операций положительны |
| `ck_transactions_transfer_shape` | перевод имеет счёт и сумму зачисления, не имеет категории и не переводит на тот же счёт; доход/расход не имеет полей перевода |
| `ck_ledger_entries_amount_nonzero` | проводка не может быть нулевой |
| `ck_budgets_limit_positive`, `ck_budgets_threshold` | лимит > 0, порог предупреждения 1–100 % |
| `ck_finance_categories_not_self_parent` | категория не может быть родителем самой себя |
| `ck_tasks_completed_at_matches_status` | дата завершения есть тогда и только тогда, когда задача выполнена |
| `ck_tasks_due_time_requires_date` | время срока без даты невозможно |
| `ck_time_blocks_interval` | конец позже начала, длительность ≤ 24 часов |
| `ck_habits_interval_days`, `ck_habits_target_streak` | интервал 1–365 дней, цель серии 1–3650 |
| `ck_journal_entries_mood` | настроение 1–5 |

Уникальные индексы: `transactions(owner_id, idempotency_key)` — идемпотентность;
`transactions(recurring_transaction_id, recurrence_date)` и `tasks(series_id, occurrence_date)` — нет дублей
повторений; `habit_completions(habit_id, date)`; `quit_habit_relapses(quit_habit_id, date)`; `journal_entries(owner_id, entry_date)`;
`tags(owner_id, normalized_name)`; `categories(owner_id, scope, name)`;
`finance_categories(owner_id, kind, parent_id, name)`; `budgets(owner_id, category_id, currency_code)`;
`refresh_tokens(token_hash)`; `users(normalized_user_name)` (имя пользователя = email).

## Финансовая модель

- Операция (`transactions`) хранит то, что ввёл пользователь; её влияние на счета — проводки
  (`ledger_entries`): расход и доход — одна проводка со знаком, перевод — две (списание и зачисление,
  возможно в разных валютах).
- Баланс счёта = `opening_balance` + сумма его проводок. Отдельного столбца баланса нет, поэтому его
  нельзя рассинхронизировать или передать с клиента.
- Изменение операции заменяет её проводки в той же транзакции БД; удаление удаляет проводки каскадно.
- Повторяющиеся операции (`recurring_transactions`) создают обычные операции с `recurring_transaction_id` и
  `recurrence_date`; уникальный индекс исключает двойное проведение.

Проверочный запрос (используется в `restore.sh`): у каждого перевода в одной валюте сумма проводок равна нулю.

## Миграции

```bash
# создать миграцию (подключение к БД не нужно)
dotnet ef migrations add <Name> --project src/PersonalPlatform.Infrastructure --startup-project src/PersonalPlatform.Infrastructure

# применить локально от роли владельца
PP_MIGRATIONS_CONNECTION="Host=...;Database=personal_platform;Username=pp_owner;Password=..." \
  dotnet ef database update --project src/PersonalPlatform.Infrastructure --startup-project src/PersonalPlatform.Infrastructure
```

В Docker миграции применяет образ `migrator` (EF Core migration bundle) перед запуском приложения;
приложение само схему не меняет. Строку подключения он читает из файла `PP_MIGRATIONS_CONNECTION_FILE`
(production) или переменной `PP_MIGRATIONS_CONNECTION`.

Правила изменений схемы: миграции только добавочные и обратно совместимые с предыдущей версией
приложения (новые столбцы nullable или с default, удаление — в следующем выпуске), чтобы откат приложения
не требовал отката схемы.

## Резервное копирование и восстановление

- `deploy/scripts/backup.sh <каталог>` — `pg_dump` в формате custom внутри контейнера БД, контрольная сумма
  SHA-256, при `BACKUP_AGE_RECIPIENT` — шифрование `age` до записи на диск.
- `deploy/scripts/restore.sh <файл> [новая_база]` — восстанавливает только в **новую** базу (отказывается
  использовать имя `personal_platform` и существующие базы), проверяет контрольную сумму, выводит число строк
  и проверяет баланс переводов.

Проверено 2026-10-11 на production-стенде: резервная копия (≈100 КБ) восстановлена в новую базу,
количество строк и проверка проводок совпали; восстановление в рабочую базу, в существующую базу и из
изменённого файла отклонено.

Рекомендации: ежедневные копии, хранение вне хоста (зашифрованными), регулярные учения по восстановлению,
срок хранения в соответствии с политикой удаления данных (удалённый аккаунт остаётся в старых копиях до
их удаления). Для RPO меньше суток — WAL-архивирование / point-in-time recovery (не настроено).
