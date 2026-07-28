#!/bin/sh
set -eu

chown -R app:app /app/keys /app/media
exec gosu app dotnet UrabaConecta.Web.dll
