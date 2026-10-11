# Тестирование

В документе — состав тестов, как их запускать и **фактические результаты** последнего прогона. Отдельный
раздел перечисляет то, что не выполнялось.

## Наборы тестов

| Проект | Что проверяет | Зависимости |
|---|---|---|
| `PersonalPlatform.UnitTests` | доменные правила (повторения, расписания и серии привычек, денежная точность, проводки, бюджеты, часовые пояса и DST, планировщик, заметки, теги, цвета); валидаторы; CSV-защита; построение tsquery; идемпотентность; паритет перечислений Contracts↔Domain; CSRF-проверка; ключи rate limiting; токены и кольцо ключей JWT; маппинг ошибок; CSP; лимит тела запроса; email-заголовки; клиентские `ReturnUrls`, `LinkFragment`, `Loc`. Property-based тесты на CsCheck | нет |
| `PersonalPlatform.ArchitectureTests` | зависимости между слоями; deny-by-default и список анонимных действий; rate limit и CSRF на нужных действиях; владелец не из запроса; нет публичных сеттеров у сущностей; правила исходников (нет `MarkupString`, inline-стилей и скриптов, web storage, raw SQL, заглушек, секретов в конфигурации, приватных ключей, секретов в шаблонах логов) | нет |
| `PersonalPlatform.IntegrationTests` | полные HTTP-сценарии всех модулей через `WebApplicationFactory` на настоящем PostgreSQL (Testcontainers), включая аутентификацию, сеансы, сброс пароля, удаление аккаунта, финансы (переводы, валюты, бюджеты, повторения, отчёты, экспорт), задачи и серии, привычки, планировщик, заметки и дневник | Docker |
| `PersonalPlatform.SecurityTests` | подделка JWT, отзыв сеансов, повторное использование refresh-токена, CSRF, CORS, заголовки, лимиты, подмена `X-Forwarded-For`, утечки в логах, обязательная аутентификация всех эндпоинтов, роли, BOLA/IDOR во всех модулях, mass assignment, межпользовательские ссылки на уровне БД, лимиты запросов и конкурентные изменения | Docker |
| `PersonalPlatform.E2ETests` | Playwright + Chromium против настоящего Kestrel-хоста с клиентом: регистрация с письмом, вход, восстановление сеанса, выход, защищённые маршруты, открытый редирект, ошибки входа, заголовки и CSP, задачи (XSS-строка, выполнение, поиск), привычки, финансы (расход, перевод, точные балансы), планировщик, автосохранение и поиск заметок, мобильная навигация; любая ошибка консоли или JS-диалог роняет тест | Docker, Chromium |
| `tests/load/k6/api-mixed.js` | смешанная нагрузка чтения и записи с отдельным аккаунтом на виртуального пользователя | развёрнутый стенд, k6 |

Тестовые данные синтетические: адреса `@example.test`, суммы и тексты придуманы; реальные секреты не
используются (ключ JWT в тестах фиксированный и предназначен только для тестов).

## Запуск

```bash
dotnet build PersonalPlatform.slnx -c Release
dotnet test -c Release --no-build --project tests/PersonalPlatform.UnitTests
dotnet test -c Release --no-build --project tests/PersonalPlatform.ArchitectureTests
dotnet test -c Release --no-build --project tests/PersonalPlatform.IntegrationTests
dotnet test -c Release --no-build --project tests/PersonalPlatform.SecurityTests

# E2E: один раз установить Chromium для Playwright (или указать свой: PP_E2E_CHROMIUM=/path/to/chrome)
pwsh tests/PersonalPlatform.E2ETests/bin/Release/net10.0/playwright.ps1 install chromium
dotnet test -c Release --no-build --project tests/PersonalPlatform.E2ETests
# трассы Playwright: tests/PersonalPlatform.E2ETests/bin/<cfg>/net10.0/playwright-traces/*.zip

# Отдельный класс или метод (Microsoft.Testing.Platform)
dotnet test --project tests/PersonalPlatform.SecurityTests -- --filter-class "*DataIsolationTests"

# Покрытие
dotnet test -c Release --no-build --project tests/PersonalPlatform.UnitTests --coverage --coverage-output-format cobertura --coverage-output unit.cobertura.xml
```

Нагрузочный тест (только на одноразовом стенде):

```bash
docker compose -p pp-load -f deploy/production/docker-compose.prod.yml -f tests/load/docker-compose.loadtest.yml \
  --env-file <prod.env стенда> up -d --wait
LOADTEST_DISPOSABLE=yes tests/load/provision-users.sh http://127.0.0.1:18081 25 \
  -p pp-load -f deploy/production/docker-compose.prod.yml -f tests/load/docker-compose.loadtest.yml --env-file <prod.env> > users.txt
k6 run -e BASE_URL=http://127.0.0.1:18081 -e USERS_FILE=users.txt tests/load/k6/api-mixed.js
```

## Результаты последнего прогона (2026-10-11)

Среда: облачный контейнер Ubuntu 24.04, 4 vCPU, 15 GB RAM; .NET SDK 10.0.112 (сборка образов — SDK 10.0.401),
PostgreSQL 17 в Docker, Chromium 141 (Playwright build 1194).

| Набор | Тестов | Успешно | Ошибок | Время |
|---|---|---|---|---|
| Unit | 202 | 202 | 0 | 6 с |
| Architecture | 27 | 27 | 0 | 2 с |
| Integration | 57 | 57 | 0 | 26 с |
| Security | 71 | 71 | 0 | 29 с |
| E2E (Chromium) | 12 | 12 | 0 | ≈1,5 мин |
| **Всего** | **369** | **369** | **0** | |

Сборка `-c Release`: 0 предупреждений, 0 ошибок (предупреждения считаются ошибками, анализаторы включены).
`dotnet format --verify-no-changes`: без изменений.

### Покрытие строк (Unit + Integration + Security, объединено ReportGenerator)

| Сборка | Строки |
|---|---|
| Domain | **93,2 %** |
| Application | **90,9 %** |
| Infrastructure | **96,7 %** |
| Contracts | 99,4 % |
| Api (рукописный код, без сгенерированного OpenAPI-кода) | 87,2 % |
| Api (вся сборка, включая сгенерированный код) | 49,2 % |
| Client | 1,9 % — код выполняется в браузере и не виден инструменту покрытия .NET; проверяется E2E |
| Всего | 72,7 % строк, 48,5 % ветвей |

Не покрыты тестами: `HealthProbe` (режим `--healthcheck`, проверен как HEALTHCHECK контейнера на стенде),
`SmtpEmailSender` (нужен SMTP-сервер; в тестах письма перехватываются), трансформер OpenAPI.

### Нагрузка (k6 v2.3.0, production-стенд с overlay `docker-compose.loadtest.yml`)

Сценарий: 25 виртуальных пользователей читают (главная, задачи, привычки, счета, операции, поиск заметок; пауза
1 с) + 10 итераций записи в секунду (создать и завершить задачу, создать расход с `Idempotency-Key`,
создать заметку), 90 секунд. Генератор нагрузки и стенд работали на одной машине.

| Метрика | Порог | Результат |
|---|---|---|
| Запросов | — | 13 976 (≈149 в секунду) |
| Ошибок HTTP | < 1 % | 0 % |
| Чтение p95 / p99 | < 300 / 800 мс | 18,3 / 29,0 мс |
| Запись p95 / p99 | < 500 / 1000 мс | 16,3 / 110,4 мс |
| Проверки | > 99 % | 100 % (13 951) |

После нагрузки в БД: 800 расходов и 800 проводок, ни одной операции с несовпадающими проводками,
0 дублей ключей идемпотентности, 800 завершённых задач. Это результат для заданного профиля нагрузки, а
не предельная пропускная способность; стресс-тест до отказа не выполнялся.

### Сканеры и проверки поставки

| Проверка | Результат |
|---|---|
| `dotnet list package --vulnerable --include-transitive` | уязвимых пакетов нет (12 проектов) |
| gitleaks v8.30.1 (вся история git) | утечек нет. Сканирование рабочего каталога находит токены только в игнорируемых файлах (`.mail/` локальной разработки, сторонние бандлы Playwright в `bin/`) |
| hadolint v2.15.1 | замечаний нет (после перехода на числовой `USER 1654`) |
| Trivy 0.75.0, образы `runtime` и `migrator` | 0 HIGH/CRITICAL с доступным исправлением, секретов нет |
| Trivy 0.75.0, конфигурация | 0 HIGH/CRITICAL |
| OWASP ZAP 2.17.0 baseline (через TLS-прокси) | 0 FAIL, 0 WARN, 59 PASS; правила 10049, 10015, 10109 исключены с обоснованием в `deploy/zap/rules.tsv` |
| Сборка Docker-образов | `runtime` и `migrator` собираются |

### Проверка production-стенда (`deploy/production`, локально, самоподписанный сертификат)

Выполнено: стек поднимается (`db`, `migrator` завершается с кодом 0, `app`, `proxy` — healthy); HTTP → 301 на
HTTPS; TLS 1.1 отклоняется; заголовки безопасности и HSTS присутствуют; `/health/*` снаружи недоступен;
клиент загружается в браузере под строгой CSP; регистрация, вход, создание счетов и операций через API,
балансы точные; контейнеры работают не от root (uid 1654/101/70), с read-only корнем, без capabilities;
секретов нет в переменных окружения контейнера; `pp_app` не может создавать таблицы; ключи Data Protection
в БД зашифрованы; порты БД и приложения не опубликованы; резервное копирование и восстановление в новую
базу с проверками (см. [DATABASE.md](DATABASE.md)).

Эта проверка нашла и исправила две ошибки, которые не проявлялись в тестах на `WebApplicationFactory`:
клиент в опубликованном образе не запускался (неразрешённые плейсхолдеры в `index.html`), и nginx падал на
IPv6-адресе в сети Docker без IPv6.

## Что не выполнялось

- GitHub Actions (CI и E2E/DAST workflow) и CodeQL — в этой среде нет доступа к GitHub API; файлы workflow
  проверены как YAML, а их шаги выполнены локально эквивалентными командами.
- SBOM (syft) не генерировался локально.
- Полный dev-стенд `docker compose up` с Mailpit не запускался (образ Mailpit не удалось загрузить из-за
  лимита Docker Hub); production-стенд проверен полностью, конфигурация dev-стенда валидируется `docker compose config`.
- Реальная доставка писем через SMTP и настоящий TLS-сертификат.
- E2E в Firefox и WebKit; автоматизированный аудит доступности.
- Активное DAST-сканирование API с аутентификацией, внешний pentest, мутационное тестирование.
- Стресс-тест до отказа и длительный (soak) тест.
