# Развёртывание

Целевая схема — один Linux-хост с Docker Engine и Compose v2: nginx (TLS) → приложение → PostgreSQL, плюс
одноразовая задача миграций. Все файлы — в `deploy/production/`.

## 1. Сборка образов

```bash
docker build --target runtime  -t registry.example.com/personal-platform:<версия> .
docker build --target migrator -t registry.example.com/personal-platform-migrator:<версия> .
docker push ...   # и запишите digest каждого образа
```

Базовые образы закреплены по digest в `Dockerfile` (обновляются осознанно, через Dependabot). За прокси с
TLS-инспекцией сборке можно передать дополнительный корневой сертификат как BuildKit-секрет — в слои образа
он не попадает: `--secret id=extra_ca,src=/path/ca.crt`.

## 2. Подготовка хоста

1. **Конфигурация без секретов**: скопируйте `deploy/production/prod.env.example` в
   `/etc/personal-platform/prod.env` (права `600`) и заполните: домен, digest образов, каталоги, SMTP-хост,
   идентификатор активного ключа JWT.
2. **Секреты**: `sudo deploy/scripts/generate-prod-secrets.sh /etc/personal-platform/secrets` создаёт пароли
   ролей БД, строки подключения, ключ подписи JWT, сертификат Data Protection и пустой файл `smtp_password`
   (впишите в него пароль SMTP). Существующие файлы не перезаписываются. Если используется менеджер секретов,
   запишите те же файлы из него. Каталог — `700`, файлы — `444` (контейнеры работают под своими uid).
   **Сохраните каталог секретов в надёжной резервной копии**: без `dataprotection.pfx` нельзя расшифровать
   ключи Data Protection (выданные ссылки подтверждения и сброса перестанут работать), без паролей ролей —
   подключиться к восстановленной базе.
3. **TLS**: положите `fullchain.pem` и `privkey.pem` в `TLS_DIR` (ключ должен читать uid 101 — пользователь
   nginx). Выпуск и продление сертификата (например, certbot с DNS-проверкой) выполняются вне этого стека;
   после продления — `docker compose ... exec proxy nginx -s reload`.
4. **DNS** домена → хост; открыты только порты 80 и 443.

## 3. Запуск и обновление

```bash
C="-f deploy/production/docker-compose.prod.yml --env-file /etc/personal-platform/prod.env"
docker compose $C config -q          # проверка конфигурации
docker compose $C up -d --wait       # db → migrator (миграции) → app → proxy
docker compose $C ps
```

Обновление: запишите новые digest в `prod.env`, сделайте резервную копию (`deploy/scripts/backup.sh`) и
выполните `up -d --wait` — сначала выполнятся миграции, затем будет пересоздан контейнер приложения.
Миграции пишутся обратно совместимыми, поэтому откат — это возврат предыдущих digest и `up -d`.

Первый администратор: укажите `ADMIN_EMAIL` (адрес уже зарегистрированного и подтверждённого аккаунта) и
перезапустите приложение; роль выдаётся при старте, событие пишется в лог.

## 4. Что настроено в production-стеке

| Компонент | Настройка |
|---|---|
| Сети | `edge` (публичная, только proxy), `frontend` и `backend` — внутренние без выхода наружу, `egress` — исходящий доступ приложения к SMTP |
| proxy | nginx 1.29, uid 101, порты 8080/8443 внутри → 80/443 снаружи, TLS 1.2/1.3, редирект на HTTPS, лимиты на IP, перезапись `X-Forwarded-For`, `/health/` закрыт, healthcheck на loopback-порту |
| app | `ASPNETCORE_ENVIRONMENT=Production`, uid 1654, доверяет `X-Forwarded-*` только от адреса прокси `172.30.10.10`, HSTS включён, секреты из `/run/secrets` (`PP_SECRETS_DIR`), сертификат Data Protection, лимит 1 GB RAM / 512 процессов |
| migrator | EF Core bundle, роль `pp_owner`, строка подключения из файла |
| db | PostgreSQL 17, uid 70, пароли из файлов, данные в томе `pgdata`, порт не опубликован |
| Все контейнеры | read-only корневая ФС (+ tmpfs), `cap_drop: ALL`, `no-new-privileges`, ротация логов json-file |

## 5. Ротация секретов

- **Ключ подписи JWT**: создайте файл с новым ключом (`head -c 32 /dev/urandom | base64`), добавьте его в
  compose как секрет с `target: Jwt__SigningKeys__1__Key` и переменную `Jwt__SigningKeys__1__KeyId`,
  перезапустите — новый ключ начинает приниматься. Переключите `JWT_ACTIVE_KEY_ID` на
  новый идентификатор, перезапустите — новые токены подписываются им. Через 5 минут (срок жизни access-токена)
  удалите старый ключ. Сеансы при этом не прерываются: refresh-токены от ключа JWT не зависят.
- **Пароль `pp_app`**: `ALTER ROLE pp_app PASSWORD '...'` от `postgres`, обновите `pp_app_password` и
  `app_connection_string`, перезапустите приложение. Аналогично для `pp_owner` (`migrations_connection`).
- **Сертификат Data Protection**: старый сертификат нужен для чтения существующих ключей; при замене
  сохраните старый до истечения выданных ключей (90 дней по умолчанию).
- **Компрометация сеансов**: «выйти везде» для пользователя или массовый отзыв —
  `UPDATE app.user_sessions SET revoked_at = now(), revocation_reason = 1 WHERE revoked_at IS NULL`
  (`1` = отозван пользователем/администратором; проверка сеанса на каждый запрос делает отзыв немедленным).

## 6. Эксплуатация

- **Health**: `/health/live` и `/health/ready` доступны только изнутри (healthcheck контейнера использует
  `dotnet PersonalPlatform.Api.dll --healthcheck`).
- **Логи**: JSON в stdout (`docker compose logs`); подключите сборщик логов. Полезные события — `security.*`,
  `HTTP ... responded 5xx`, ошибки доставки писем.
- **Мониторинг**: стоит оповещать о рестартах контейнеров, 5xx, всплесках 401/429 и `security.refresh_token_reuse`,
  месте на диске для `pgdata` и каталога копий, сроке действия TLS-сертификата.
- **Резервные копии**: `deploy/scripts/backup.sh /var/backups/personal-platform` по расписанию (cron/systemd
  timer), копирование вне хоста, регулярная проверка `deploy/scripts/restore.sh` — см. [DATABASE.md](DATABASE.md).
- **Масштабирование**: один экземпляр приложения. Для нескольких экземпляров нужны общий счётчик rate
  limiting и общее хранилище ключей Data Protection (уже в БД).
- **Обновления безопасности**: регулярно пересобирайте образы (новые патчи базовых образов), следите за
  Dependabot и результатами Trivy.
