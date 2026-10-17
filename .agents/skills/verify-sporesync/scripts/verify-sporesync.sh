#!/usr/bin/env bash
# Start, doctor-check, and stop one isolated SporeSync backend for verification.
#
#   verify-sporesync.sh start            launch on SPORESYNC_VERIFY_PORT; prints RUN_DIR
#   verify-sporesync.sh doctor RUN_DIR   read-only check of the instance started in RUN_DIR
#   verify-sporesync.sh stop RUN_DIR     stop exactly the processes recorded in RUN_DIR
#
# `elide run` spawns `elide java`, which spawns the JVM that owns the port. Signalling only
# `elide run` orphans the JVM, so the whole chain is recorded and stopped by exact PID.
set -euo pipefail

readonly DEFAULT_PORT=18080
readonly DEFAULT_ROOT="/tmp/verify-sporesync"
readonly BIND_ADDRESS="127.0.0.1"
readonly HEALTH_PATH="/actuator/health"
readonly STATUS_UP='"status":"UP"'
readonly HTTP_OK="200"
readonly READY_TIMEOUT_SECONDS=120
readonly STOP_TIMEOUT_SECONDS=20
readonly POLL_SECONDS=1
readonly CURL_TIMEOUT_SECONDS=2
readonly INSTANCE_FILE="instance.env"
readonly LOG_FILE="app.log"
readonly EVIDENCE_DIR="evidence"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
BACKEND_DIR="$REPO_ROOT/backend"
PORT="${SPORESYNC_VERIFY_PORT:-$DEFAULT_PORT}"
ROOT="${SPORESYNC_VERIFY_ROOT:-$DEFAULT_ROOT}"

die() { echo "FAIL: $*" >&2; exit 1; }
ok() { echo "OK:   $*"; }

alive() { kill -0 "$1" 2>/dev/null; }

# A PID is ours only if it is alive and running from this checkout's backend directory.
owned() {
  alive "$1" || return 1
  [ "$(lsof -a -p "$1" -d cwd -Fn 2>/dev/null | sed -n 's/^n//p')" = "$BACKEND_DIR" ]
}

listener_pid() { lsof -nP -t -iTCP:"$1" -sTCP:LISTEN 2>/dev/null | head -n 1; }

descendants() {
  local child
  for child in $(pgrep -P "$1" 2>/dev/null); do
    echo "$child"
    descendants "$child"
  done
}

health_url() { echo "http://$BIND_ADDRESS:$1$HEALTH_PATH"; }

# Sends TERM to every owned PID, waits, then KILLs exact survivors.
stop_pids() {
  local pid waited=0 survivors
  for pid in "$@"; do owned "$pid" && kill -TERM "$pid" 2>/dev/null || true; done
  while [ "$waited" -lt "$STOP_TIMEOUT_SECONDS" ]; do
    survivors=""
    for pid in "$@"; do alive "$pid" && survivors="$survivors $pid"; done
    [ -z "$survivors" ] && return 0
    sleep "$POLL_SECONDS"
    waited=$((waited + POLL_SECONDS))
  done
  for pid in $survivors; do owned "$pid" && kill -KILL "$pid" 2>/dev/null || true; done
}

load_instance() {
  local run_dir="${1:-}"
  [ -n "$run_dir" ] || die "usage: $0 $2 RUN_DIR"
  [ -f "$run_dir/$INSTANCE_FILE" ] || die "no live instance recorded in $run_dir/$INSTANCE_FILE"
  # shellcheck disable=SC1090
  . "$run_dir/$INSTANCE_FILE"
}

cmd_start() {
  command -v elide >/dev/null || die "elide is not on PATH"
  local want have run_dir launcher deadline pid chain
  want="$(cat "$REPO_ROOT/.elideversion")"
  have="$(elide --version)"
  case "$have" in "$want"*) ;; *) die "elide $have does not match .elideversion $want" ;; esac
  [ -z "$(listener_pid "$PORT")" ] && ok "port $PORT is free" || die "port $PORT is already in use; pick another SPORESYNC_VERIFY_PORT"

  run_dir="$ROOT/$(date +%Y%m%dT%H%M%S)-$PORT"
  mkdir -p "$run_dir/$EVIDENCE_DIR"

  cd "$BACKEND_DIR"
  SERVER_PORT="$PORT" SERVER_ADDRESS="$BIND_ADDRESS" nohup elide run </dev/null >"$run_dir/$LOG_FILE" 2>&1 &
  launcher=$!

  deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
  until curl -fsS --max-time "$CURL_TIMEOUT_SECONDS" "$(health_url "$PORT")" 2>/dev/null | grep -q "$STATUS_UP"; do
    if ! alive "$launcher" || [ "$SECONDS" -ge "$deadline" ]; then
      # shellcheck disable=SC2046
      stop_pids $(descendants "$launcher") "$launcher"
      die "not ready within ${READY_TIMEOUT_SECONDS}s; see $run_dir/$LOG_FILE"
    fi
    sleep "$POLL_SECONDS"
  done

  pid="$(listener_pid "$PORT")"
  chain="$launcher $(descendants "$launcher" | tr '\n' ' ')"
  case " $chain " in *" $pid "*) ;; *)
    # shellcheck disable=SC2086
    stop_pids $chain
    die "port $PORT listener $pid is not a descendant of launcher $launcher"
  esac

  cat >"$run_dir/$INSTANCE_FILE" <<EOF
PORT=$PORT
APP_PID=$pid
LAUNCHER_PID=$launcher
CHAIN_PIDS="$chain"
STARTED_HEAD=$(git -C "$REPO_ROOT" rev-parse HEAD)
EOF
  ok "SporeSync UP at $(health_url "$PORT") (JVM pid $pid, launcher pid $launcher)"
  echo "RUN_DIR=$run_dir"
}

cmd_doctor() {
  load_instance "${1:-}" doctor
  local pid failed=0 code body head
  for pid in $CHAIN_PIDS; do
    owned "$pid" && ok "pid $pid alive, cwd $BACKEND_DIR" || { echo "FAIL: pid $pid not alive or not ours"; failed=1; }
  done
  [ "$(listener_pid "$PORT")" = "$APP_PID" ] && ok "port $PORT is owned by JVM pid $APP_PID" \
    || { echo "FAIL: port $PORT listener is '$(listener_pid "$PORT")', expected $APP_PID"; failed=1; }
  head="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  [ "$head" = "$STARTED_HEAD" ] && ok "running build matches HEAD $head" \
    || { echo "FAIL: started at $STARTED_HEAD but HEAD is now $head; stop and relaunch"; failed=1; }
  body="$(curl -sS --max-time "$CURL_TIMEOUT_SECONDS" -w '\n%{http_code}' "$(health_url "$PORT")" 2>&1 || true)"
  code="$(echo "$body" | tail -n 1)"
  body="$(echo "$body" | sed '$d')"
  if [ "$code" = "$HTTP_OK" ] && echo "$body" | grep -q "$STATUS_UP"; then
    ok "GET $HEALTH_PATH -> $code $body"
  else
    echo "FAIL: GET $HEALTH_PATH -> $code $body"; failed=1
  fi
  return "$failed"
}

cmd_stop() {
  local run_dir="${1:-}"
  load_instance "$run_dir" stop
  # shellcheck disable=SC2086
  stop_pids "$APP_PID" $CHAIN_PIDS
  local pid
  for pid in $CHAIN_PIDS; do alive "$pid" && die "pid $pid survived stop"; done
  [ -z "$(listener_pid "$PORT")" ] || die "port $PORT is still listening"
  rm "$run_dir/$INSTANCE_FILE"
  ok "stopped pids $CHAIN_PIDS; port $PORT is free; evidence kept in $run_dir"
}

case "${1:-}" in
  start) cmd_start ;;
  doctor) cmd_doctor "${2:-}" ;;
  stop) cmd_stop "${2:-}" ;;
  *) die "usage: $0 start | doctor RUN_DIR | stop RUN_DIR" ;;
esac
