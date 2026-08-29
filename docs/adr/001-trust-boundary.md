# ADR-001: Trust Boundary

## Статус

Accepted

## Контекст

Система публикует зарегистрированные PostgreSQL-функции как HTTP actions.

Клиент взаимодействует только с публичным gateway и может выбирать опубликованный action, передавая payload и, при необходимости, explicit version.

Основной риск заключается в том, что недоверенный клиентский ввод может быть использован для изменения внутренней маршрутизации, предметной политики или server-side context.

Поэтому клиент не должен иметь возможности выбирать или подменять:

- PostgreSQL database;
- PostgreSQL schema;
- target function;
- SQL;
- required policy;
- principal;
- consumer;
- scopes;
- correlationId;
- requestId;
- deadline;
- финальный предметный результат.

## Решение

Trust boundary разделяется на несколько уровней.

### HTTP Gateway

Gateway является внешней границей системы.

Он:

- принимает только разрешённые HTTP routes;
- публикует единственный внешний host port;
- передаёт запрос во внутренний API через Compose DNS;
- не имеет доступа к PostgreSQL;
- не содержит action catalog и предметной логики.

Gateway не рассматривает данные клиентского запроса как доверенные и не принимает решений о target function или database operation.

### Action Runtime API

API является границей аутентификации и формирования доверенного runtime context.

После успешной проверки JWT API извлекает из проверенных claims:

- `principal`;
- `consumer`;
- `scopes`.

API самостоятельно формирует:

- `correlationId`;
- `requestId`;
- `deadline`.

Эти server-side значения не могут быть подменены полями HTTP payload.

Payload рассматривается как недоверенный вход и проверяется против request schema опубликованного action.

### Immutable Action Catalog

Action resolution выполняется только по опубликованному catalog.

Клиент может передать:

- `module`;
- `action`;
- optional `X-Action-Version`.

Клиент не может передать:

- `target_schema`;
- `target_function`;
- SQL;
- required policy;
- request/response schema;
- idempotency configuration.

Target и остальные параметры исполнения берутся из immutable опубликованной версии manifest.

Операционное состояние `enabled` и `is_default` изменяется только через publication CLI и атомарные database operations.

### PostgreSQL `api.invoke`

Все предметные вызовы проходят через единую функцию `api.invoke`, которая является второй доверенной границей после HTTP API.

Она повторно проверяет:

- существование action в catalog;
- выбранную version;
- `enabled` status;
- required policy;
- допустимый target из catalog;
- точную target function signature.

Target function вызывается только после успешного прохождения этих проверок.

Runtime database role получает право на выполнение `api.invoke`, но не получает прямого DML-доступа к предметным таблицам.

### Subject State

Предметные изменения выполняются только зарегистрированными PostgreSQL-функциями внутри транзакционной границы action execution.

Клиент не может перескочить напрямую к предметной таблице или произвольной PostgreSQL-функции.

## Последствия

### Положительные

- новый action может быть опубликован без изменения и пересборки C# API;
- routing и target function контролируются immutable catalog;
- policy проверяется на HTTP boundary и повторно в `api.invoke`;
- server-side context не может быть подменён payload;
- runtime role не получает прямого доступа к предметному DML;
- database остаётся authoritative source для catalog и subject state.

### Отрицательные

- необходимо поддерживать action catalog;
- publication становится отдельным жизненным циклом;
- требуется синхронизация контрактов между manifest, JSON Schema и PostgreSQL target functions;
- часть security checks намеренно выполняется дважды на разных границах.

## Альтернативы

### C# controller на каждый action

Отклонено.

Новый action потребовал бы изменения C# кода и пересборки API. Это нарушает database-first и generic runtime model.

### Клиент передаёт target function

Отклонено.

Это позволило бы недоверенному клиенту выбирать внутренний PostgreSQL object и нарушило бы trust boundary.

### Прямой DML из C# runtime

Отклонено.

Это перенесло бы предметную логику и authoritative mutations из PostgreSQL в API и нарушило бы database-first execution model.

### Одна policy check только на HTTP boundary

Отклонено.

`api.invoke` является отдельной доверенной границей и должен самостоятельно проверять required policy, чтобы прямой вызов database runtime role не мог обойти authorization.
