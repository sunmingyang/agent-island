#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT
swiftc \
    Sources/Trigger/Trigger.swift \
    Sources/Trigger/TriggerSafetyStore.swift \
    Tests/TriggerSafetyAuthTests.swift \
    -o "$tmpdir/trigger-safety-auth-tests"
"$tmpdir/trigger-safety-auth-tests"
