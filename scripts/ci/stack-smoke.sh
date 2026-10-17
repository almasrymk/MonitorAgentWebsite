#!/usr/bin/env bash
# Checks the production stack from deploy/ the way a visitor reaches it: through Caddy on https://localhost.
set -euo pipefail
base=https://localhost
fail=0

code() { curl -sk -o /dev/null -w '%{http_code}' "$base$1"; }
expect() {
  local got; got=$(code "$1")
  if [[ "$got" == "$2" ]]; then echo "ok   $1 -> $got"; else echo "FAIL $1 -> $got (expected $2)"; fail=1; fi
}
header() {
  if curl -skI "$base$1" | grep -qi "^$2"; then echo "ok   $1 has $2"; else echo "FAIL $1 is missing $2"; fail=1; fi
}

for _ in $(seq 1 60); do [[ $(code /health/ready) == 200 ]] && break; sleep 5; done

expect /health/ready 200
expect / 302
expect /ar 200
expect /en/pricing 200
expect /ar/demo 200
expect /ar/terms 200
expect /portal 308
expect /portal/ 200
expect /portal/sign-in 200
expect /admin/ 200
expect /api/v1/auth/csrf 204
expect /api/v1/account/me 401
header /ar content-security-policy
header /ar strict-transport-security
header /portal/ content-security-policy
header /admin/ x-content-type-options
header /admin/ x-robots-tag

exit $fail
