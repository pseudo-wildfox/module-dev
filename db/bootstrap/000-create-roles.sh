#!/bin/bash
set -e


psql -v ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname "$POSTGRES_DB" <<EOSQL

CREATE ROLE course_owner NOLOGIN;


CREATE ROLE course_migrator
    LOGIN
    PASSWORD '${COURSE_MIGRATOR_PASSWORD}';


CREATE ROLE course_publisher
    LOGIN
    PASSWORD '${COURSE_PUBLISHER_PASSWORD}';


CREATE ROLE course_runtime
    LOGIN
    PASSWORD '${COURSE_RUNTIME_PASSWORD}';


GRANT course_owner TO course_migrator;

EOSQL