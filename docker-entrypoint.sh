#!/bin/sh
set -eu

chown -R app:app /app/keys
exec gosu app dotnet UrabaConecta.Web.dll
