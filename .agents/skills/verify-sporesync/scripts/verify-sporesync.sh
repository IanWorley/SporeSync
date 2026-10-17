#!/usr/bin/env bash
# Start, doctor-check, and stop one isolated SporeSync backend for verification.
#
#   verify-sporesync.sh start            launch on SPORESYNC_VERIFY_PORT; prints RUN_DIR
#   verify-sporesync.sh doctor RUN_DIR   read-only check of the instance started in RUN_DIR
#   verify-sporesync.sh stop RUN_DIR     stop exactly the processes recorded in RUN_DIR
#
# `elide run` spawns `elide java`, which spawns the JVM that owns the port. Signalling only
# `elide run` orphans the JVM, so the whole chain is recorded and stopped by exact process
# identity: PID plus start time, so a reused PID is never mistaken for ours.
set -euo pipefail

readonly DEFAULT_PORT=18080
readonly DEFAULT_ROOT="/tmp/verify-sporesync"
readonly DEFAULT_BIND_ADDRESS="127.0.0.1"
# Checks always go through loopback, which reaches both a loopback and an all-interfaces bind.
readonly CLIENT_ADDRESS="127.0.0.1"
readonly HEALTH_PATH="/actuator/health"
readonly STATUS_UP='"status":"UP"'
readonly HTTP_OK="200"
readonly READY_TIMEOUT_SECONDS=120
readonly STOP_TIMEOUT_SECONDS=20
readonly POLL_SECONDS=1
readonly CURL_TIMEOUT_SECONDS=2
readonly EXIT_SIGINT=130
readonly EXIT_SIGTERM=143
readonly OWNER_ONLY_MODE=700
readonly INSTANCE_FILE="instance.env"
readonly LOG_FILE="app.log"
readonly EVIDENCE_DIR="evidence"
# instance.env is parsed, never sourced; every value must match its pattern.
readonly PORT_PATTERN='^[0-9]+$'
readonly BIND_PATTERN='^[0-9A-Fa-f.:]+$'
readonly PROCESS_PATTERN='^[0-9]+@[A-Za-z0-9_:.]+$'
readonly SHA1_PATTERN='^[0-9a-f]{40}$'
readonly SHA256_PATTERN='^[0-9a-f]{64}$'
readonly DIRTY_PATTERN='^(clean|dirty)$'

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel)"
BACKEND_DIR="$REPO_ROOT/backend"
PORT="${SPORESYNC_VERIFY_PORT:-$DEFAULT_PORT}"
ROOT="${SPORESYNC_VERIFY_ROOT:-$DEFAULT_ROOT}"
BIND_ADDRESS="${SPORESYNC_VERIFY_BIND:-$DEFAULT_BIND_ADDRESS}"

die() { echo "FAIL: $*" >&2; exit 1; }
ok() { echo "OK:   $*"; }

# Lookups print nothing, rather than fail, when the process or listener is gone.
start_time() { { ps -o lstart= -p "$1" 2>/dev/null || true; } | tr -s ' ' '_' | sed 's/^_//; s/_$//'; }

# A process handle is "PID@start-time".
handle() { echo "$1@$(start_time "$1")"; }

# True while the exact process behind a handle is still running.
running() {
  local pid="${1%%@*}"
  kill -0 "$pid" 2>/dev/null && [ "$(start_time "$pid")" = "${1#*@}" ]
}

# A process is ours only if it is still running and runs from this checkout's backend directory.
owned() {
  running "$1" || return 1
  [ "$(lsof -a -p "${1%%@*}" -d cwd -Fn 2>/dev/null | sed -n 's/^n//p')" = "$BACKEND_DIR" ]
}

listener_pid() { { lsof -nP -t -iTCP:"$1" -sTCP:LISTEN 2>/dev/null || true; } | head -n 1; }

descendants() {
  local child
  for child in $(pgrep -P "$1" 2>/dev/null); do
    echo "$child"
    descendants "$child"
  done
}

health_url() { echo "http://$CLIENT_ADDRESS:$1$HEALTH_PATH"; }

# Hashes HEAD, tracked changes, and untracked files, so uncommitted edits change the fingerprint.
tree_fingerprint() {
  {
    git -C "$REPO_ROOT" rev-parse HEAD
    git -C "$REPO_ROOT" diff HEAD --binary
    git -C "$REPO_ROOT" ls-files -z --others --exclude-standard | while IFS= read -r -d '' file; do
      printf '%s\n' "$file"
      git -C "$REPO_ROOT" hash-object -- "$file"
    done
  } | shasum -a 256 | cut -d ' ' -f 1
}

tree_state() {
  if [ -z "$(git -C "$REPO_ROOT" status --porcelain)" ]; then echo clean; else echo dirty; fi
}

# Sends TERM to every owned handle, waits, then KILLs exact survivors.
stop_handles() {
  local process waited=0 survivors
  for process in "$@"; do
    if owned "$process"; then kill -TERM "${process%%@*}" 2>/dev/null || true; fi
  done
  while [ "$waited" -lt "$STOP_TIMEOUT_SECONDS" ]; do
    survivors=""
    for process in "$@"; do
      if running "$process"; then survivors="$survivors $process"; fi
    done
    [ -z "$survivors" ] && return 0
    sleep "$POLL_SECONDS"
    waited=$((waited + POLL_SECONDS))
  done
  for process in $survivors; do
    if owned "$process"; then kill -KILL "${process%%@*}" 2>/dev/null || true; fi
  done
}

# Refuses a root or file that another account could have planted.
require_private() {
  [ ! -L "$1" ] || die "$1 is a symlink"
  [ -O "$1" ] || die "$1 is not owned by $(id -un)"
}

prepare_root() {
  [ ! -L "$ROOT" ] || die "$ROOT is a symlink"
  mkdir -p "$ROOT"
  require_private "$ROOT"
  chmod "$OWNER_ONLY_MODE" "$ROOT"
}

read_field() {
  local value
  value="$(sed -n "s/^$2=//p" "$1" | head -n 1)"
  [[ "$value" =~ $3 ]] || die "$1 has an invalid $2"
  echo "$value"
}

load_instance() {
  local run_dir="${1:-}" file process
  [ -n "$run_dir" ] || die "usage: $0 $2 RUN_DIR"
  file="$run_dir/$INSTANCE_FILE"
  [ -e "$file" ] || die "no live instance recorded in $file"
  require_private "$run_dir"
  require_private "$file"
  PORT="$(read_field "$file" PORT "$PORT_PATTERN")"
  BIND_ADDRESS="$(read_field "$file" BIND_ADDRESS "$BIND_PATTERN")"
  APP="$(read_field "$file" APP "$PROCESS_PATTERN")"
  CHAIN="$(sed -n 's/^CHAIN=//p' "$file" | head -n 1)"
  [ -n "$CHAIN" ] || die "$file has no CHAIN"
  for process in $CHAIN; do
    [[ "$process" =~ $PROCESS_PATTERN ]] || die "$file has an invalid CHAIN entry"
  done
  STARTED_HEAD="$(read_field "$file" STARTED_HEAD "$SHA1_PATTERN")"
  STARTED_TREE="$(read_field "$file" STARTED_TREE "$SHA256_PATTERN")"
  STARTED_STATE="$(read_field "$file" STARTED_STATE "$DIRTY_PATTERN")"
}

# Until the handle is saved, an exit or signal must stop whatever start launched.
STARTING_PIDS=""
abort_start() {
  local pid all="$STARTING_PIDS" handles=""
  # The poll loop may not have seen the children yet, so look again now.
  for pid in $STARTING_PIDS; do all="$all $(descendants "$pid" | tr '\n' ' ')"; done
  for pid in $all; do handles="$handles $(handle "$pid")"; done
  # shellcheck disable=SC2086
  [ -z "$handles" ] || stop_handles $handles
}

cmd_start() {
  command -v elide >/dev/null || die "elide is not on PATH"
  local want have run_dir launcher deadline found pid chain="" head tree state
  want="$(cat "$REPO_ROOT/.elideversion")"
  have="$(elide --version)"
  case "$have" in "$want"*) ;; *) die "elide $have does not match .elideversion $want" ;; esac
  [ -z "$(listener_pid "$PORT")" ] || die "port $PORT is already in use; pick another SPORESYNC_VERIFY_PORT"
  ok "port $PORT is free"

  prepare_root
  run_dir="$(mktemp -d "$ROOT/$(date +%Y%m%dT%H%M%S)-$PORT.XXXXXX")"
  mkdir "$run_dir/$EVIDENCE_DIR"

  # Fingerprint before launch: `elide run` compiles the tree as it is now.
  head="$(git -C "$REPO_ROOT" rev-parse HEAD)"
  tree="$(tree_fingerprint)"
  state="$(tree_state)"

  cd "$BACKEND_DIR"
  trap abort_start EXIT
  trap 'exit $EXIT_SIGINT' INT
  trap 'exit $EXIT_SIGTERM' TERM
  SERVER_PORT="$PORT" SERVER_ADDRESS="$BIND_ADDRESS" nohup elide run </dev/null >"$run_dir/$LOG_FILE" 2>&1 &
  launcher=$!
  STARTING_PIDS="$launcher"

  deadline=$((SECONDS + READY_TIMEOUT_SECONDS))
  until curl -fsS --max-time "$CURL_TIMEOUT_SECONDS" "$(health_url "$PORT")" 2>/dev/null | grep -q "$STATUS_UP"; do
    # Remember children while the launcher lives, so an early exit still finds an orphaned JVM.
    found="$(descendants "$launcher" | tr '\n' ' ')"
    [ -z "$found" ] || STARTING_PIDS="$launcher $found"
    kill -0 "$launcher" 2>/dev/null || die "launcher exited before ready; see $run_dir/$LOG_FILE"
    [ "$SECONDS" -lt "$deadline" ] || die "not ready within ${READY_TIMEOUT_SECONDS}s; see $run_dir/$LOG_FILE"
    sleep "$POLL_SECONDS"
  done
  STARTING_PIDS="$launcher $(descendants "$launcher" | tr '\n' ' ')"

  pid="$(listener_pid "$PORT")"
  case " $STARTING_PIDS " in *" $pid "*) ;; *) die "port $PORT listener $pid is not a descendant of launcher $launcher" ;; esac
  for found in $STARTING_PIDS; do chain="$chain $(handle "$found")"; done

  cat >"$run_dir/$INSTANCE_FILE" <<EOF
PORT=$PORT
BIND_ADDRESS=$BIND_ADDRESS
APP=$(handle "$pid")
CHAIN=${chain# }
STARTED_HEAD=$head
STARTED_TREE=$tree
STARTED_STATE=$state
EOF
  trap - EXIT INT TERM

  ok "SporeSync UP at $(health_url "$PORT"), bound to $BIND_ADDRESS (JVM pid $pid, launcher pid $launcher)"
  ok "built from HEAD $head, $state working tree (tree sha256 $tree)"
  echo "RUN_DIR=$run_dir"
}

cmd_doctor() {
  load_instance "${1:-}" doctor
  local process failed=0 code body listener tree
  for process in $CHAIN; do
    if owned "$process"; then
      ok "pid ${process%%@*} alive since ${process#*@}, cwd $BACKEND_DIR"
    else
      echo "FAIL: pid ${process%%@*} (started ${process#*@}) is gone or not ours"; failed=1
    fi
  done
  listener="$(listener_pid "$PORT")"
  if [ "$listener" = "${APP%%@*}" ]; then
    ok "port $PORT is owned by JVM pid $listener"
  else
    echo "FAIL: port $PORT listener is '$listener', expected ${APP%%@*}"; failed=1
  fi
  tree="$(tree_fingerprint)"
  if [ "$tree" = "$STARTED_TREE" ]; then
    ok "running build matches HEAD $STARTED_HEAD, $STARTED_STATE working tree (tree sha256 $tree)"
  else
    echo "FAIL: working tree changed since launch (HEAD $STARTED_HEAD -> $(git -C "$REPO_ROOT" rev-parse HEAD)); stop and relaunch"
    failed=1
  fi
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
  local run_dir="${1:-}" process listener
  load_instance "$run_dir" stop
  # shellcheck disable=SC2086
  stop_handles "$APP" $CHAIN
  for process in $CHAIN; do
    if running "$process"; then die "pid ${process%%@*} survived stop"; fi
  done
  listener="$(listener_pid "$PORT")"
  [ -z "$listener" ] || die "port $PORT is still listening (pid $listener)"
  rm "$run_dir/$INSTANCE_FILE"
  ok "stopped $CHAIN; port $PORT is free; evidence kept in $run_dir"
}

case "${1:-}" in
  start) cmd_start ;;
  doctor) cmd_doctor "${2:-}" ;;
  stop) cmd_stop "${2:-}" ;;
  *) die "usage: $0 start | doctor RUN_DIR | stop RUN_DIR" ;;
esac
