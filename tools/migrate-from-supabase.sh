#!/usr/bin/env bash
# ============================================================
#  Move one module's production data from Supabase to the NAS Postgres.
#  Which tables to copy is given as arguments, so this one script serves every module
#  in turn rather than being cloned per feature.
#
#  Counts rows on both sides before and after, so a partial copy is visible rather than
#  silent. Copies data only — the schema on the NAS comes from db/*.sql and deliberately
#  differs (no auth.uid(), no RLS).
#
#  user_id needs no remapping: the .NET API validates Supabase's own JWT, so the `sub`
#  claim is the same UUID that Supabase wrote into these rows.
#
#  Run this AT CUT-OVER, not before. Copy early and Supabase keeps taking writes, the two
#  sides drift, and the whole thing has to be redone.
#
#  RUNS ON THE NAS, over SSH. It needs Docker and it needs to reach the
#  storechecking-db container, neither of which exists on the development machine.
#
#  Usage (from /volume1/docker/storechecking):
#    export SUPABASE_DB_URL='postgresql://postgres:PASSWORD@db.xxxx.supabase.co:5432/postgres'
#    DOCKER="sudo docker" ./tools/migrate-from-supabase.sh notes
#    DOCKER="sudo docker" ./tools/migrate-from-supabase.sh expense_categories expenses
#
#  Name parent tables BEFORE their children: they are copied in the order given, and a
#  child restored first fails its foreign key.
#
#  The password is Supabase -> Project Settings -> Database (NOT the anon/publishable key),
#  and the DIRECT connection on port 5432 — the pooler on 6543 cannot serve pg_dump.
#  Pass it through the environment so it never lands in shell history or in this repo.
# ============================================================
set -euo pipefail

PG_IMAGE="postgres:16-alpine"
NAS_CONTAINER="${NAS_CONTAINER:-storechecking-db}"
NAS_USER="${NAS_USER:-storechecking}"
NAS_DB="${NAS_DB:-storechecking}"
DUMP_DIR="${DUMP_DIR:-./_migration}"

# Synology needs root for the Docker socket, so the NAS runs this as DOCKER="sudo docker".
# Left as plain `docker` by default for anywhere that does not.
DOCKER="${DOCKER:-docker}"

TABLES=("$@")
if [[ ${#TABLES[@]} -eq 0 ]]; then
  echo "ERROR: name at least one table, e.g. ./tools/migrate-from-supabase.sh notes" >&2
  exit 1
fi

# One dump file per run, named after what it holds, so re-running for another module
# does not overwrite the previous evidence.
DUMP_FILE="$DUMP_DIR/$(IFS=-; echo "${TABLES[*]}")-data.sql"

if [[ -z "${SUPABASE_DB_URL:-}" ]]; then
  echo "ERROR: set SUPABASE_DB_URL first. See the header of this script." >&2
  exit 1
fi

mkdir -p "$DUMP_DIR"

# --- Row counts on Supabase, before anything is touched -------------------------------
echo "=== Rows on Supabase ==="
declare -A before
for t in "${TABLES[@]}"; do
  n=$($DOCKER run --rm -e PGURL="$SUPABASE_DB_URL" "$PG_IMAGE" \
        psql "$SUPABASE_DB_URL" -tAc "select count(*) from public.$t")
  before[$t]=$n
  printf "  %-16s %s\n" "$t" "$n"
done

# --- Row counts already on the NAS ----------------------------------------------------
echo
echo "=== Rows already on the NAS (should normally be 0) ==="
for t in "${TABLES[@]}"; do
  n=$($DOCKER exec "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -tAc "select count(*) from public.$t" 2>/dev/null || echo "TABLE MISSING")
  printf "  %-16s %s\n" "$t" "$n"
  if [[ "$n" == "TABLE MISSING" ]]; then
    echo >&2
    echo "ERROR: table $t does not exist on the NAS yet." >&2
    echo "The API applies db/*.sql itself when it starts, so this means either the table" >&2
    echo "has no schema file yet, or the running image predates the one that adds it." >&2
    echo "Check what has been applied:" >&2
    echo "  $DOCKER exec $NAS_CONTAINER psql -U $NAS_USER -d $NAS_DB -c 'table schema_history'" >&2
    exit 1
  fi
  if [[ "$n" != "0" ]]; then
    echo >&2
    echo "ERROR: $t already holds $n rows on the NAS. Refusing to run — a second pass" >&2
    echo "would duplicate everything. Empty the table first if you meant to re-import:" >&2
    echo "  $DOCKER exec $NAS_CONTAINER psql -U $NAS_USER -d $NAS_DB -c 'truncate table $t'" >&2
    exit 1
  fi
done

# --- Dump and restore, ONE TABLE AT A TIME, in the order given ------------------------
# One pg_dump with several --table arguments would be shorter, and wrong: it emits the
# tables in its own order, with no regard for foreign keys. expenses references
# expense_categories, and sales references products — restoring a child before its parent
# fails the constraint. Naming tables parent-first and copying them one by one is what
# makes the order guaranteed rather than lucky.
#
# The NAS database container publishes no port on purpose, so psql is fed from inside it.
echo
echo "=== Copying (data only), in the order given ==="
: > "$DUMP_FILE"
for t in "${TABLES[@]}"; do
  one="$DUMP_DIR/$t-data.sql"
  $DOCKER run --rm "$PG_IMAGE" \
    pg_dump "$SUPABASE_DB_URL" --data-only --no-owner --no-privileges --table="public.$t" \
    > "$one"
  printf "  %-24s dumped %s bytes" "$t" "$(wc -c < "$one")"

  $DOCKER exec -i "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -v ON_ERROR_STOP=1 \
    < "$one" > /dev/null
  echo " -> restored"

  # Keep a combined copy too, so one run leaves one artefact to look at afterwards.
  cat "$one" >> "$DUMP_FILE"
done

# --- Verify ----------------------------------------------------------------------------
echo
echo "=== Verifying ==="
fail=0
for t in "${TABLES[@]}"; do
  after=$($DOCKER exec "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -tAc "select count(*) from public.$t")
  if [[ "$after" == "${before[$t]}" ]]; then
    printf "  [OK]   %-16s %s rows\n" "$t" "$after"
  else
    printf "  [FAIL] %-16s Supabase=%s NAS=%s\n" "$t" "${before[$t]}" "$after"
    fail=1
  fi
done

echo
if [[ $fail -eq 0 ]]; then
  echo "Row counts match on both sides."
  echo "Supabase data was NOT deleted — leave it until the Angular app has run on the new"
  echo "API for a while, then remove it by hand."
else
  echo "MISMATCH — do not switch the app over. Investigate before going further." >&2
  exit 1
fi
