# Неделя 1. Database-first action runtime

Открытое задание первой недели ModuleDev. Здесь опубликованы условие, machine-readable contracts и открытая black-box проверка. Готового решения, внутренней шкалы и закрытых fixtures в репозитории нет.

С 22 по 26 августа 2026 года постройте C# gateway и generic C# runtime, который публикует зарегистрированные PostgreSQL-функции как HTTP actions.

Задание выполнено, если проверка после сборки может добавить функцию и manifest с неизвестными вашему C#-коду именами, вызвать новый action через общий маршрут и получить контрактный результат без изменения и пересборки API.

## Главный инвариант

> Клиент выбирает опубликованный action и передаёт payload, но не выбирает базу, schema, функцию, SQL, policy или предметный результат.

```text
POST /api/{module}/{action}
  -> gateway на :8080
  -> внутренний api по Compose DNS
  -> JWT и server-side context
  -> action manifest и request schema
  -> одна Npgsql transaction
  -> api.invoke(...)
  -> зарегистрированная PostgreSQL-функция
  -> outcome и response schema
  -> commit или rollback
```

## Срок и сдача

- начало работы: 22 августа 2026 года;
- дедлайн: 26 августа 2026 года, 23:59 по московскому времени;
- до дедлайна отправьте куратору URL приватного Git-репозитория, ветку и полный SHA коммита;
- заранее предоставьте куратору доступ к репозиторию;
- проверяется указанный коммит.

Архив в чате, ссылка без доступа или сообщение без полного SHA не считаются сдачей.

## Технологии и свобода реализации

Обязательны C#, актуальный LTS .NET, ASP.NET Core, PostgreSQL и Docker Compose.

На ваше усмотрение остаются:

- структура solution и число C# projects;
- ORM, query builder или чистый Npgsql;
- физические таблицы, индексы и внутренние имена;
- кеширование manifest;
- внутренние классы и интерфейсы;
- устройство idempotency storage;
- библиотеки JSON Schema и OpenAPI;
- собственные unit и integration tests.

Автопроверка не требует Clean Architecture и не ищет конкретные классы. Небольшая реализация с доказанными инвариантами предпочтительнее большого scaffold. Собственные tests рекомендуются, но не заменяют `./check.sh`.

## Обязательный интерфейс запуска

В корне решения должны находиться `compose.yaml` или `docker-compose.yml` и четыре сервиса:

| Сервис | Обязательное свойство |
|---|---|
| `gateway` | C# ASP.NET Core gateway; единственный публикует host-порт `8080` |
| `api` | Внутренний C# action runtime без опубликованных host-портов |
| `cli` | Course CLI; entrypoint принимает contract commands |
| `postgres` | PostgreSQL с базой `course`, ролью `postgres` и утилитой `psql` |

Данные PostgreSQL хранятся в named volume и переживают удаление контейнеров `gateway` и `api`. После чистого запуска не нужны ручные SQL-команды или публикация встроенных actions:

```bash
docker compose up -d --build
```

`api` читает:

| Переменная | Значение открытого профиля |
|---|---|
| `COURSE_JWT_ISSUER` | `moduledev-course` |
| `COURSE_JWT_AUDIENCE` | `moduledev-api` |
| `COURSE_JWT_SIGNING_KEY` | HS256 key длиной не менее 32 байт |

Реальный секрет в репозиторий не помещается. Проверка подменяет signing key через Compose override.

## Обязательные контракты

Точные сигнатуры, JSON envelopes, error matrix, CLI semantics, action contracts и колонки проверочных views находятся в [нормативном справочнике](docs/contract-reference.md). Machine schemas находятся в [`contracts/course-1`](contracts/course-1).

### Gateway

- внешний клиент обращается только к `http://localhost:8080`;
- gateway принимает по whitelist только actions, OpenAPI и health routes;
- gateway передаёт запрос и контрактные заголовки во внутренний `api` без изменения смысла;
- gateway обращается к `api` по Compose DNS, а не через `localhost`;
- gateway не содержит catalog, предметную логику и доступ к PostgreSQL;
- JWT, credentials и полный payload не попадают в логи;
- `GET /health/live` проверяет процесс, `GET /health/ready` — готовность внутреннего API.

### Action runtime

- один generic route: `POST /api/{module}/{action}`;
- explicit version передаётся только в `X-Action-Version`, без заголовка выбирается default;
- target разрешается только из immutable action catalog;
- request schema проверяется до предметного вызова;
- `api.invoke` повторно проверяет policy и вызывает только зарегистрированную функцию;
- outcome и `result` проверяются до commit;
- error, неизвестный outcome и invalid result откатывают весь предметный эффект;
- runtime-роль выполняет `api.invoke`, но не получает прямой DML к предметным таблицам.

### Trust boundary

- `principal`, `consumer` и `scopes` берутся только из проверенного JWT;
- claims проверяются по обязательности, JSON-типу и значению;
- `correlationId` и `deadline` формируются runtime;
- поля payload не могут подменить context, policy, target или результат;
- после аутентификации ответ содержит server-side `correlationId`;
- ошибки не раскрывают SQL, stack trace, connection string и внутренние targets.

### Версии и идемпотентность

- опубликованная версия manifest неизменяема;
- `enabled` и `is_default` меняются только атомарными CLI-командами;
- у включённого route ровно одна default-версия;
- одинаковые key и payload возвращают исходный результат без второго эффекта;
- тот же key с другим payload возвращает `409 idempotency.conflict`;
- конкурентная уникальность защищается PostgreSQL, а не process-local lock.

### Обязательные actions

- `payment.request` version 1 создаёт operation и одно событие `OPERATION_CREATED`;
- `operation.get` version 1 читает operation по `operationId`;
- клиент не передаёт workflow name/version и финальный status;
- точные payload/result schemas находятся в `contracts/course-1`.

### OpenAPI, health и evidence

- `/openapi/default.json` содержит только включённые default routes;
- `/openapi/actions/{module}/{action}/{version}.json` описывает одну точную версию;
- readiness успешен только при доступном PostgreSQL и завершённой инициализации;
- после восстановления PostgreSQL readiness возвращается без пересоздания API;
- schema `autocheck` публикует пять read-only views из нормативного справочника;
- `course_runtime` не может изменять operations или удалять events;
- object owner для `SECURITY DEFINER` имеет `NOLOGIN`.

## Что реализовать

- C# services `gateway`, `api`, `cli` и PostgreSQL в Compose;
- checksummed migrations и отдельные publication/runtime roles;
- immutable action catalog и `api.invoke(...)`;
- generic HTTP executor с request/response schema validation;
- JWT, server-side context и policy на двух границах;
- transactional idempotency и rollback contract;
- `payment.request` и `operation.get` как PostgreSQL-функции;
- единый HTTP error envelope;
- manifest-driven OpenAPI и health endpoints;
- пять read-only views schema `autocheck`;
- C4 Container diagram;
- ADR о trust boundary;
- ADR о техническом и предметном результате;
- README с запуском, диагностикой и проверкой.

## Открытая проверка

Запуск из корня решения:

```bash
./check.sh
```

| Область | Что проверяется |
|---|---|
| Compose | Чистая сборка, четыре сервиса, единственный внешний порт gateway |
| Publication | Migration, две версии нового action, default/explicit version, activate/disable |
| Security | JWT, claims, policy в HTTP и `api.invoke`, отсутствие секретов в логах |
| Contracts | Schemas, outcomes, error envelopes, rollback до commit |
| PostgreSQL | Конкурентная идемпотентность, operation, event, dispatch, права ролей |
| Recovery | Recreate gateway/API, остановка и восстановление PostgreSQL, readiness |
| Documentation | Manifest-driven OpenAPI и доступность точных version documents |

Проверка после дедлайна использует те же опубликованные контракты на новых данных, именах actions и конкурентных interleavings. Внутренние fixtures, сценарии и система оценки не публикуются.

## Оформление решения

В корневом `README.md` сдачи создайте секцию `## Решение` с подразделами:

- `### Архитектура` — ответственность сервисов и направление вызовов;
- `### Запуск` — prerequisites, команда запуска, адрес и ожидаемый результат;
- `### Конфигурация` — используемые переменные без реальных секретов;
- `### Миграции` — когда и каким сервисом они применяются;
- `### Проверка` — `./check.sh` и, при наличии, собственные tests;
- `### Диагностика` — gateway, api, PostgreSQL, health и OpenAPI;
- `### Ограничения` — известные ограничения и технические решения.

Исходный текст задания можно оставить ниже или перенести в `TASK.md`.

## Checklist перед сдачей

- все команды выполняются из корня чистого clone;
- `docker compose up -d --build` не требует ручной настройки после запуска;
- `./check.sh` завершается успешно;
- gateway использует Compose DNS, а не `localhost`;
- readiness действительно проверяет зависимости, а не только process liveness;
- API публикует JSON OpenAPI documents, Swagger UI их не заменяет;
- конкурентные повторы создают одну operation и одно начальное событие;
- ссылки на C4 и ADR открываются из секции `Решение`;
- в Git нет `.env`, реальных секретов, `bin`, `obj`, IDE files, логов и `week-1-public-report.json`;
- куратору доступны репозиторий, ветка и полный SHA проверяемого commit.

Admission checks выполняются до функционального прогона. Они проверяют Git commit, безопасный состав репозитория, Compose interface, секцию `Решение` и воспроизводимые команды.

## Условия незачёта

- контур не запускается по README;
- основной API реализован не на C#;
- авторитетное состояние находится не в PostgreSQL;
- предметные endpoints реализованы отдельными C# controllers без action catalog;
- клиент может выбрать БД, schema, функцию или SQL;
- повтор создаёт новый предметный эффект;
- operation и event расходятся;
- новый зарегистрированный action требует пересборки API;
- в репозитории или журналах находятся реальные секреты.

## Не входит в неделю

- workflow maps, process instances и worker;
- `payment.submit` и provider-simulator;
- Outbox/Inbox, receipts и manual actions;
- lease, retries и failpoints;
- отдельные C# handlers/controllers для предметных actions;
- Consul, CORS transformations, micro-cache, rate limiting и защитные очереди gateway.

Не проектируйте эти компоненты заранее ценой усложнения action runtime.

## Нормативные источники

1. Этот README определяет границы задания, запуск и сдачу.
2. [`docs/contract-reference.md`](docs/contract-reference.md) фиксирует точные runtime contracts.
3. JSON Schema в [`contracts/course-1`](contracts/course-1) определяет точную форму machine payloads и manifest.

Если документы противоречат друг другу, сообщите куратору: это дефект задания, а не повод угадывать поведение проверки.


# Нормативный справочник недели 1

Этот документ фиксирует точные интерфейсы задания. Краткий маршрут реализации и правила сдачи находятся в [README](../README.md).

## Course CLI

Публичная обёртка может выглядеть так:

```bash
#!/usr/bin/env bash
exec docker compose run --rm -T cli "$@"
```

Проверка не исполняет `course.sh` из недоверенного репозитория. Она вызывает сервис `cli` напрямую и монтирует доверенные fixtures read-only.

Обязательные команды:

```text
course.sh migration apply <directory>
course.sh action validate <manifest>
course.sh action publish <manifest>
course.sh action list
course.sh action activate <module.action> --version <version>
course.sh action disable <module.action> --version <version> [--replacement-version <version>]
```

Команда принимает путь, доступный внутри контейнера `cli`. Для автопроверки каталог монтируется как `/autocheck/input:ro`.

CLI пишет в stdout ровно один JSON-документ. Диагностика допускается только в stderr. При ошибке exit code ненулевой.

Успех:

```json
{
  "status": "ok",
  "result": {
    "resource": "action",
    "operation": "published",
    "key": "payment.request",
    "version": 1
  },
  "meta": {
    "contractVersion": "course-1"
  }
}
```

Ошибка:

```json
{
  "status": "error",
  "code": "manifest.conflict",
  "message": "published action version is immutable",
  "meta": {
    "contractVersion": "course-1"
  }
}
```

Правила:

- `action validate` не меняет данные;
- повторный `action publish` того же manifest безопасен;
- изменение опубликованной версии даёт conflict;
- `action activate` атомарно включает выбранную версию, делает её default и снимает default с прежней;
- `action disable` требует replacement, если route иначе останется без default;
- `migration apply` выполняет `.sql` в лексикографическом порядке, по одной транзакции на файл;
- повтор migration с тем же checksum безопасен;
- изменение уже применённого файла даёт conflict;
- API и будущий worker не используют migration credentials.

## Action manifest

Каноническая schema: [`contracts/course-1/action-manifest.schema.json`](../contracts/course-1/action-manifest.schema.json).

Минимальные контрактные поля:

- `module`, `action`, `version`, `http_method`;
- `target_schema`, `target_function`;
- `request_schema`, `response_schema`;
- `outcomes`, `required_policy`;
- `idempotency_mode`, `idempotency_scope`;
- `timeout_ms`.

Опубликованная версия неизменяема. `enabled` и `is_default` являются операционным состоянием и меняются только CLI-командами. Для route с включёнными версиями существует ровно одна default-версия. В обязательной части поддерживается только `POST`.

## Database-first выполнение

Единственная точка предметного выполнения:

```sql
api.invoke(
  p_module text,
  p_action text,
  p_version integer,
  p_context jsonb,
  p_payload jsonb
) returns jsonb
```

Target-функция имеет сигнатуру:

```sql
<target_schema>.<target_function>(
  p_context jsonb,
  p_payload jsonb
) returns jsonb
```

`api.invoke` обязан:

1. разрешить explicit или default version только из catalog;
2. отклонить неизвестный или выключенный action до target;
3. повторно проверить все scopes из `required_policy` по доверенному context;
4. проверить target и точную сигнатуру;
5. вызвать только зарегистрированную функцию с фиксированным `search_path`;
6. вернуть единый envelope.

HTTP executor валидирует request schema до предметного вызова. Затем он открывает Npgsql transaction, вызывает `api.invoke`, проверяет envelope, outcome и `result` по response schema и только после этого выполняет commit.

Если target изменил данные и вернул `status=error`, неизвестный outcome или несовместимый result, вся транзакция откатывается.

Runtime-роль имеет право выполнить `api.invoke`, но не получает прямой доступ к предметным таблицам и функциям. Владелец security-definer функций не имеет права входа.

## Доверенный context

C# формирует context после проверки JWT:

```json
{
  "principal": "candidate-client",
  "consumer": "web",
  "scopes": ["payment:write", "payment:read"],
  "correlationId": "1e534bdb-73a8-446a-a8f5-49c4959786c2",
  "requestId": "request-123",
  "deadline": "2026-08-28T12:00:02Z"
}
```

Правила:

- `principal`, `consumer` и `scopes` берутся только из проверенного JWT;
- обязательность, JSON-тип и значение каждого claim проверяются явно;
- `correlationId` генерируется runtime как UUID для каждого HTTP-запроса;
- `requestId` равен `Idempotency-Key`, если key передан;
- `deadline` вычисляется runtime из `timeout_ms` manifest;
- одноимённые поля payload не меняют context;
- `api.invoke` считает policy выполненной, только если context содержит все scopes manifest.

Учебные principals:

| `sub` | `consumer` | `scope` |
|---|---|---|
| `candidate-client` | `web` | `payment:write payment:read workflow:read` |
| `workflow-worker` | `internal` | `workflow:execute payment:internal` |
| `reviewer` | `backoffice` | `workflow:manual payment:read` |
| `denied-client` | `test` | пусто |

JWT использует HS256, issuer и audience из конфигурации, а также claims `sub`, `consumer`, `scope`, `iat`, `exp`. Корректная подпись не заменяет проверку формы claims: неверный JSON-тип приводит к `401 auth.invalid`, а не к framework 500.

## HTTP API

```http
POST /api/{module}/{action}
Authorization: Bearer <token>
X-Action-Version: 1
Idempotency-Key: request-123
Content-Type: application/json
```

После `/api` всегда ровно два route-сегмента. Версия передаётся только в `X-Action-Version`. Без заголовка выбирается default version.

Успех всегда отвечает `200 OK`:

```json
{
  "status": "ok",
  "outcome": "CREATED",
  "result": {},
  "meta": {
    "correlationId": "1e534bdb-73a8-446a-a8f5-49c4959786c2",
    "actionVersion": 1
  }
}
```

Ошибка:

```json
{
  "status": "error",
  "code": "payload.invalid",
  "message": "payload does not match schema",
  "retryable": false,
  "details": {},
  "meta": {
    "correlationId": "1e534bdb-73a8-446a-a8f5-49c4959786c2",
    "actionVersion": 1
  }
}
```

| Ситуация | HTTP | `code` |
|---|---:|---|
| Неверный, отсутствующий, просроченный JWT или claim неверного типа | 401 | `auth.invalid` |
| Невалидный JSON, route или version header | 400 | `request.invalid` |
| Нет обязательного idempotency key | 400 | `idempotency.required` |
| Недостаточная policy | 403 | `access.denied` |
| Неизвестный или выключенный action/version | 404 | `action.not_found` |
| Тот же key с другим payload | 409 | `idempotency.conflict` |
| Payload не соответствует request schema | 422 | `payload.invalid` |
| Временная недоступность PostgreSQL | 503 | `dependency.unavailable` |
| Result или outcome нарушает manifest | 500 | `action.contract_violation` |
| Истёк timeout | 504 | `action.timeout` |
| Необработанная ошибка | 500 | `internal.error` |

После успешной аутентификации `meta.correlationId` обязателен. Для ошибки разрешения action `meta.actionVersion` равен фактически выбранной версии или `null`.

Ответ 500 не раскрывает SQL, schema/function names, connection string и stack trace.

## Нормативная матрица исполнения

| Сценарий | HTTP/envelope | Предметный эффект | Проверяемое доказательство |
|---|---|---|---|
| Новый зарегистрированный action | `200`, объявленный `outcome`, schema-valid `result` | Commit target-изменения | Action доступен без изменения images gateway/API |
| Невалидный request payload | `422 payload.invalid` | Target не вызывается | Canary и предметные таблицы не изменены |
| Недостаточная policy | `403 access.denied` | Target не вызывается | Отказ подтверждён на HTTP-границе и в `api.invoke` |
| Повтор с тем же key и payload | Исходный успешный envelope | Второго эффекта нет | Та же operation и одно начальное событие |
| Тот же key с другим payload | `409 idempotency.conflict` | Нового эффекта нет | Авторитетное состояние не изменено |
| Target вернул `status=error` | Контролируемый error envelope | Полный rollback | Canary отсутствует, dispatch содержит техническую ошибку |
| Неизвестный outcome или invalid result | `500 action.contract_violation` | Полный rollback | Canary отсутствует, успешный idempotency result не сохранён |
| Неизвестная или выключенная версия | `404 action.not_found` | Target не вызывается | В dispatch нет успешного предметного вызова |
| Recreate gateway/API | После readiness возвращается исходный результат | PostgreSQL-состояние сохранено | `operation.get` возвращает ту же operation |

## `payment.request` version 1

Route: `POST /api/payment/request`.

| Поле manifest | Значение |
|---|---|
| `required_policy` | `["payment:write"]` |
| `idempotency_mode` | `required` |
| `idempotency_scope` | `principal_action` |
| `outcomes` | `["CREATED"]` |

Payload:

```json
{
  "operationKind": "PAYMENT_EXECUTION",
  "amount": "1000.00",
  "currency": "RUB"
}
```

`operationKind` принимает `PAYMENT_EXECUTION` или `PAYMENT_APPROVAL`. `amount` является строкой от `0.01` до `9999999999999999.99`, без exponent и не более чем с двумя знаками после точки. Поддерживается только `RUB`. Неизвестные поля запрещены.

Успешный `result`:

```json
{
  "operationId": "8c26513d-8441-43ea-b064-3bca8c240052",
  "requestId": "request-123",
  "operationKind": "PAYMENT_EXECUTION",
  "amount": "1000.00",
  "currency": "RUB",
  "status": "CREATED"
}
```

Первый запрос атомарно создаёт operation и одно событие `OPERATION_CREATED`. Идентичный повтор возвращает ту же operation. Тот же key с другим payload возвращает `409 idempotency.conflict`. Конкурентные одинаковые запросы создают один предметный эффект за счёт гарантии PostgreSQL, а не process-local lock.

Клиент не передаёт workflow name/version или финальный status. На первой неделе process ещё не создаётся.

## `operation.get` version 1

Route: `POST /api/operation/get`.

| Поле manifest | Значение |
|---|---|
| `required_policy` | `["payment:read"]` |
| `idempotency_mode` | `none` |
| `idempotency_scope` | `none` |
| `outcomes` | `["FOUND"]` |

Payload:

```json
{
  "operationId": "8c26513d-8441-43ea-b064-3bca8c240052"
}
```

`result` имеет ту же форму operation, что `payment.request`. Неизвестный ID возвращает контролируемую предметную ошибку, а не 500.

Machine schemas находятся в [`contracts/course-1`](../contracts/course-1).

## OpenAPI и health

- `GET /openapi/default.json` содержит только включённые default routes;
- `GET /openapi/actions/{module}/{action}/{version}.json` содержит одну точную версию action;
- документ строится из опубликованного manifest;
- версии одного action не объединяются через `oneOf`;
- после `activate` или `disable` default document меняется без пересборки API;
- `GET /health/live` возвращает 200, если HTTP-процесс жив;
- `GET /health/ready` возвращает 200 только при доступном PostgreSQL и завершённой инициализации;
- при недоступном PostgreSQL action возвращает `503 dependency.unavailable`;
- после восстановления PostgreSQL readiness возвращается без пересоздания API.

## Проверочные проекции

Физические таблицы остаются вашими. Для black-box проверки создайте read-only schema `autocheck` и views:

| View | Обязательные колонки |
|---|---|
| `contract_info` | `contract_version text`, `generated_at timestamptz` |
| `action_definitions` | `module text`, `action text`, `version integer`, `http_method text`, `target_schema text`, `target_function text`, `outcomes jsonb`, `enabled boolean`, `is_default boolean` |
| `action_dispatches` | `correlation_id uuid`, `request_id text`, `module text`, `action text`, `version integer`, `principal text`, `payload_hash text`, `status text`, `outcome text`, `occurred_at timestamptz` |
| `operations` | `operation_id uuid`, `request_id text`, `operation_kind text`, `amount numeric`, `currency text`, `status text`, `process_id uuid`, `created_at timestamptz`, `updated_at timestamptz` |
| `operation_events` | `event_id uuid`, `operation_id uuid`, `event_type text`, `payload_hash text`, `occurred_at timestamptz` |

Требования:

- `contract_info` содержит одну строку с `contract_version = 'course-1'`;
- enum-подобные значения используют uppercase ASCII;
- `action_dispatches.status` принимает `OK` или `ERROR`;
- `operations.status` принимает `CREATED`, `PROCESSING`, `COMPLETED`, `REJECTED`;
- `payload_hash` является lowercase SHA-256 hex, полный payload не публикуется;
- `process_id` на первой неделе равен `null`;
- views не раскрывают JWT, signing key, connection string и полный payload.

### Неизменяемость и модель угроз PostgreSQL

Неизменяемость проверяется относительно прикладных ролей, а не superuser PostgreSQL:

- `course_runtime` может читать `autocheck` views, но не выполнять через них `INSERT`, `UPDATE` или `DELETE`;
- identity и payload-поля operation не изменяются после создания;
- `operation_events` является insert-only history для прикладных runtime/publication roles;
- изменение предметного состояния выполняют только зарегистрированные функции через доверенную транзакционную границу;
- object owner, используемый `SECURITY DEFINER`, имеет `NOLOGIN`;
- административный superuser не входит в модель угроз задания.

Проверка выполняет отрицательные mutation probes после `SET ROLE course_runtime`. Стабильной границей служат `autocheck` views и права ролей, а не физические таблицы или ORM.

## Нормативная структура README сдачи

Секция `## Решение` содержит подразделы:

- `### Архитектура`;
- `### Запуск`;
- `### Конфигурация`;
- `### Миграции`;
- `### Проверка`;
- `### Диагностика`;
- `### Ограничения`.

Все команды выполняются из корня чистого clone. Ссылки относительные или доступны проверяющему. В Git отсутствуют `.env`, реальные секреты, `bin`, `obj`, IDE files, логи и `week-1-public-report.json`. Схема, C4 и два ADR открываются по ссылкам из секции `Решение`.
