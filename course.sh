#!/usr/bin/env bash

set -e

exec docker compose run --rm -T cli "$@"