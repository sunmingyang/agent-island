#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")/.."

tmpdir="$(mktemp -d)"
trap 'rm -rf "$tmpdir"' EXIT

swiftc \
    Sources/Usage/GrokBilling.swift \
    Sources/Usage/GeminiCredentials.swift \
    Sources/Usage/GeminiQuota.swift \
    Tests/GeminiParsingTests.swift \
    -o "$tmpdir/gemini-parsing-tests"

"$tmpdir/gemini-parsing-tests"
