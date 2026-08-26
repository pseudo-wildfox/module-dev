# ADR-001: Trust Boundary

## Статус

Accepted

## Контекст

Система публикует PostgreSQL-функции как HTTP actions.

Основной риск:
клиент может попытаться повлиять на внутреннюю маршрутизацию,
предметную политику или контекст выполнения.

Клиент не должен выбирать:

- PostgreSQL database;
- schema;
- function;
- SQL;
- policy;
- финальный результат.

## Решение

Trust boundary разделяется на несколько уровней.

### HTTP Gateway

Gateway является внешней границей.

Его ответственность:

- принимать только разрешённые маршруты;
- скрывать внутренний API;
- не содержать catalog;
- не иметь доступа к PostgreSQL.

Gateway не доверяет клиентскому запросу.

### Action Runtime API

API формирует доверенный runtime context после проверки JWT.

В context входят:

- principal;
- consumer;
- scopes;
- correlationId;
- requestId;
- deadline.

Эти значения никогда не берутся из payload.

### Action Catalog

Target функция выбирается только из опубликованного manifest.

Клиент передает:

- module;
- action;
- optional version.

Клиент не передает:

- target_schema;
- target_function.

### PostgreSQL api.invoke

Все предметные вызовы проходят через:

api.invoke(...)

Функция повторно проверяет:

- существование action;
- enabled status;
- version;
- policy.

## Последствия

Положительные:

- новый action добавляется через publication flow;
- API не требует пересборки;
- невозможно вызвать произвольную SQL-функцию.

Отрицательные:

- требуется поддерживать catalog;
- publication становится отдельным жизненным циклом.

## Альтернативы

### C# controller на каждый action

Отклонено.

Причина:

- новый action потребовал бы изменения кода;
- нарушается database-first подход.

### Клиент передает имя функции

Отклонено.

Причина:

- нарушает trust boundary;
- позволяет выбирать внутренние объекты базы.