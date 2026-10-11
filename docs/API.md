# API

HTTP API версии 1 под префиксом `/api/v1`. Машиночитаемое описание (OpenAPI 3.1) отдаётся по
`/openapi/v1.json` в среде Development или при `Security:ExposeOpenApi=true`; в production по умолчанию
выключено. Таблицы ниже сгенерированы из этого документа (100 операций).

## Соглашения

**Аутентификация.** `Authorization: Bearer <access JWT>` для всех эндпоинтов, кроме отмеченных «анонимно».
Access-токен выдают `POST /auth/login` и `POST /auth/refresh` в теле ответа (`accessToken`, `expiresAt`);
refresh-токен приходит только в cookie `__Host-pp_refresh` и в теле не возвращается. Запросы
`/auth/refresh` и `/auth/logout` должны содержать заголовок `X-CSRF: 1` и идти с того же origin.

**Формат.** JSON (UTF-8). Имена полей — camelCase, перечисления — строками (`"Expense"`, `"High"`).
Даты — `YYYY-MM-DD` (календарная дата пользователя), мгновения — ISO 8601 с часовым поясом (сервер
отдаёт UTC). Суммы — JSON-числа с точностью валюты (например, 2 знака для RUB/EUR, 0 для JPY, 3 для KWD),
коды валют — ISO 4217. Неизвестные поля в теле запроса → 400. Максимальный размер тела — 1 МБ.

**Ошибки** — `application/problem+json` (RFC 9457) с полем `code`:

| Статус | `code` | Когда |
|---|---|---|
| 400 | `validation_failed` | ошибки валидации; `errors` — сообщения по полям, `errorCodes` — машинные коды |
| 400 | `bad_request`, `invalid_token` | некорректный JSON или запрос; недействительная ссылка из письма |
| 401 | `invalid_credentials`, `invalid_refresh_token` | неверные учётные данные, нет/истёк/отозван сеанс |
| 403 | `email_not_confirmed`, `csrf_rejected` | адрес не подтверждён; запрос с cookie не с того origin |
| 404 | `not_found` | объекта нет **или он принадлежит другому пользователю** (различить нельзя) |
| 409 | `concurrency_conflict`, `duplicate`, `idempotency_key_reused`, `time_block_overlap`, `limit_reached`, `reference_conflict` | объект изменён другим запросом; нарушена уникальность; ключ идемпотентности повторён с другим телом; пересечение блоков при запрете пересечений (в `details` — пересекающиеся блоки); достигнут лимит количества объектов; ссылка на удалённый объект |
| 413 | `payload_too_large` | тело больше 1 МБ |
| 422 | доменный код (`date_not_scheduled`, `account_archived`, …) | нарушено бизнес-правило |
| 429 | `rate_limited` | превышен лимит; см. заголовок `Retry-After` |
| 503 | `timeout` (может отсутствовать) | запрос не уложился в 30 секунд или запрос к БД отменён по таймауту |

Ответы содержат `X-Correlation-Id` — его стоит указывать при обращении в поддержку.

**Конкурентные изменения.** DTO изменяемых объектов содержат `version`. Запрос на изменение передаёт
полученную версию; если объект успели изменить, ответ 409 `concurrency_conflict` — клиент перечитывает объект
и показывает пользователю конфликт, а не перезаписывает чужие изменения.

**Идемпотентность.** `POST /finance/transactions` принимает заголовок `Idempotency-Key` (8–64 символа
`[A-Za-z0-9_-]`, уникален для намерения пользователя). Повтор с тем же ключом и тем же телом возвращает уже
созданную операцию с заголовком `Idempotency-Replayed: true`; тот же ключ с другим телом — 409 `idempotency_key_reused`.
`POST /finance/recurring/post-due` идемпотентен по датам повторений.

**Пагинация.** Списки возвращают `{ items, page, pageSize, totalCount }`; `page` 1–10000, `pageSize` 1–100.
Поиск — до 100 символов: в заметках и дневнике — по началу слов (полнотекстовый), в задачах и операциях — по
подстроке названия/описания. Диапазоны дат ограничены (обычно 366 дней).

**Кэширование.** Все ответы API — `Cache-Control: no-store`.

## Пример

```http
POST /api/v1/finance/transactions
Authorization: Bearer eyJ...
Idempotency-Key: 6f1c2a9e-3b7d-4c55-9a51-0e8b2f6d1a44
Content-Type: application/json

{ "kind": "Transfer", "occurredOn": "2026-10-05", "description": null,
  "accountId": "01a12bf8-a5f6-72bb-9d47-c08a5996959c", "amount": 100.00,
  "categoryId": null, "destinationAccountId": "01a12bf8-a6ce-7fdb-817c-e23bf1f0a2fd", "destinationAmount": null }
```

`201 Created` с созданной операцией. Для перевода между счетами в разных валютах `destinationAmount`
обязателен: курс никогда не вычисляется неявно.

## Эндпоинты

Доступ: «анонимно» — без токена; «пользователь» — любой аутентифицированный пользователь, только к своим
данным; «admin» — роль администратора.

### Account

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/account/me` | пользователь |  |
| POST | `/api/v1/account/change-password` | пользователь |  |
| GET | `/api/v1/account/sessions` | пользователь |  |
| DELETE | `/api/v1/account/sessions/{sessionId}` | пользователь |  |
| POST | `/api/v1/account/sessions/revoke-all` | пользователь | Signs out every device, including this one. |
| POST | `/api/v1/account/delete` | пользователь | Permanently deletes the account and all data. Requires the password and the word DELETE. |

### Admin

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/admin/stats` | admin |  |

### Auth

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| POST | `/api/v1/auth/register` | анонимно |  |
| POST | `/api/v1/auth/confirm-email` | анонимно |  |
| POST | `/api/v1/auth/resend-confirmation` | анонимно |  |
| POST | `/api/v1/auth/login` | анонимно |  |
| POST | `/api/v1/auth/refresh` | анонимно | Exchanges the refresh cookie for a new access token and a rotated refresh cookie. |
| POST | `/api/v1/auth/logout` | анонимно |  |
| POST | `/api/v1/auth/forgot-password` | анонимно |  |
| POST | `/api/v1/auth/reset-password` | анонимно |  |

### Dashboard

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/dashboard` | пользователь |  |

### Export

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/export/transactions.csv` | пользователь |  |
| GET | `/api/v1/export/notes.json` | пользователь |  |

### Accounts

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/finance/accounts` | пользователь |  |
| POST | `/api/v1/finance/accounts` | пользователь |  |
| GET | `/api/v1/finance/accounts/{id}` | пользователь |  |
| PUT | `/api/v1/finance/accounts/{id}` | пользователь |  |
| DELETE | `/api/v1/finance/accounts/{id}` | пользователь |  |
| POST | `/api/v1/finance/accounts/{id}/archive` | пользователь |  |

### FinanceCategories

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/finance/categories` | пользователь |  |
| POST | `/api/v1/finance/categories` | пользователь |  |
| PUT | `/api/v1/finance/categories/{id}` | пользователь |  |
| DELETE | `/api/v1/finance/categories/{id}` | пользователь |  |

### Transactions

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/finance/transactions` | пользователь |  |
| POST | `/api/v1/finance/transactions` | пользователь | Creates an operation. With an `Idempotency-Key` header a retry returns the original operation (200 and |
| GET | `/api/v1/finance/transactions/{id}` | пользователь |  |
| PUT | `/api/v1/finance/transactions/{id}` | пользователь |  |
| DELETE | `/api/v1/finance/transactions/{id}` | пользователь |  |

### Budgets

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/finance/budgets` | пользователь |  |
| POST | `/api/v1/finance/budgets` | пользователь |  |
| PUT | `/api/v1/finance/budgets/{id}` | пользователь |  |
| DELETE | `/api/v1/finance/budgets/{id}` | пользователь |  |
| GET | `/api/v1/finance/budgets/report` | пользователь |  |

### RecurringTransactions

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/finance/recurring` | пользователь |  |
| POST | `/api/v1/finance/recurring` | пользователь |  |
| PUT | `/api/v1/finance/recurring/{id}` | пользователь |  |
| DELETE | `/api/v1/finance/recurring/{id}` | пользователь |  |
| POST | `/api/v1/finance/recurring/{id}/pause` | пользователь |  |
| POST | `/api/v1/finance/recurring/post-due` | пользователь | Posts all occurrences due up to today. Idempotent and safe to call repeatedly. |

### FinanceReports

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/finance/reports/year` | пользователь |  |
| GET | `/api/v1/finance/reports/period` | пользователь |  |
| GET | `/api/v1/finance/reports/categories` | пользователь |  |

### Habits

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/habits` | пользователь |  |
| POST | `/api/v1/habits` | пользователь |  |
| GET | `/api/v1/habits/{id}` | пользователь |  |
| PUT | `/api/v1/habits/{id}` | пользователь |  |
| DELETE | `/api/v1/habits/{id}` | пользователь |  |
| POST | `/api/v1/habits/{id}/archive` | пользователь |  |
| PUT | `/api/v1/habits/{id}/completions/{date}` | пользователь | Checks the habit off for a local date (idempotent). |
| DELETE | `/api/v1/habits/{id}/completions/{date}` | пользователь | Removes the check-off for a local date (idempotent). |
| GET | `/api/v1/habits/{id}/history` | пользователь |  |
| GET | `/api/v1/habits/{id}/stats` | пользователь |  |

### Notes

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/notes` | пользователь |  |
| POST | `/api/v1/notes` | пользователь |  |
| GET | `/api/v1/notes/{id}` | пользователь |  |
| PUT | `/api/v1/notes/{id}` | пользователь |  |
| DELETE | `/api/v1/notes/{id}` | пользователь |  |
| POST | `/api/v1/notes/{id}/pin` | пользователь |  |
| POST | `/api/v1/notes/{id}/archive` | пользователь |  |
| GET | `/api/v1/notes/{id}/revisions` | пользователь |  |

### Journal

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/journal` | пользователь |  |
| GET | `/api/v1/journal/{date}` | пользователь |  |
| PUT | `/api/v1/journal/{date}` | пользователь |  |
| DELETE | `/api/v1/journal/{date}` | пользователь |  |

### Categories

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/categories` | пользователь |  |
| POST | `/api/v1/categories` | пользователь |  |
| PUT | `/api/v1/categories/{id}` | пользователь |  |
| DELETE | `/api/v1/categories/{id}` | пользователь |  |

### Tags

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/tags` | пользователь |  |
| POST | `/api/v1/tags` | пользователь | Creates a tag, or returns the existing tag with the same (case-insensitive) name. |
| DELETE | `/api/v1/tags/{id}` | пользователь |  |

### Planner

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/planner` | пользователь | Blocks, due habits and due tasks for a range of local dates (at most 42 days). |
| GET | `/api/v1/planner/blocks/{id}` | пользователь |  |
| PUT | `/api/v1/planner/blocks/{id}` | пользователь |  |
| DELETE | `/api/v1/planner/blocks/{id}` | пользователь |  |
| POST | `/api/v1/planner/blocks` | пользователь |  |
| POST | `/api/v1/planner/blocks/{id}/move` | пользователь |  |

### Profile

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/profile` | пользователь |  |
| PUT | `/api/v1/profile` | пользователь |  |
| GET | `/api/v1/profile/reference-data` | анонимно | Supported currencies, time zones and locales (public, non-personal reference data). |

### Tasks

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/tasks` | пользователь |  |
| POST | `/api/v1/tasks` | пользователь |  |
| GET | `/api/v1/tasks/{id}` | пользователь |  |
| PUT | `/api/v1/tasks/{id}` | пользователь |  |
| DELETE | `/api/v1/tasks/{id}` | пользователь |  |
| POST | `/api/v1/tasks/{id}/status` | пользователь |  |
| POST | `/api/v1/tasks/{id}/reschedule` | пользователь |  |
| POST | `/api/v1/tasks/{id}/subtasks/{subtaskId}/done` | пользователь |  |
| POST | `/api/v1/tasks/{id}/dismiss-reminder` | пользователь |  |
| POST | `/api/v1/tasks/bulk` | пользователь | Complete, reopen, reschedule or delete up to 100 tasks in one atomic operation. |

### TaskSeries

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/task-series/{id}` | пользователь |  |
| PUT | `/api/v1/task-series/{id}` | пользователь |  |
| DELETE | `/api/v1/task-series/{id}` | пользователь |  |
| POST | `/api/v1/task-series/{id}/stop` | пользователь |  |

### Reminders

| Метод | Путь | Доступ | Описание |
|---|---|---|---|
| GET | `/api/v1/reminders` | пользователь |  |

