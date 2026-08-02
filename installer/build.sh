#!/usr/bin/env bash
set -euo pipefail

installer_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
version="$(python3 -c 'import json, pathlib; print(json.loads(pathlib.Path("src/Installer.Win/Payload/release-manifest.json").read_text())["version"])' 2>/dev/null)"
exec python3 "${installer_root}/tools/release.py" \
    --version "${version}" \
    --echoforge-root "${installer_root}" \
    --skip-prepare \
    "$@"
