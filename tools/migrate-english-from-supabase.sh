#!/usr/bin/env bash
# ============================================================
#  Move the English feature's production data from Supabase to the NAS Postgres.
#  Covers two tables: english_words, speaking_saved.
#
#  Counts rows on both sides before and after, so a partial copy is visible rather than
#  silent. Copies data only — the schema on the NAS is created by db/002-english.sql and
#  deliberately differs (no auth.uid(), no RLS).
#
#  user_id needs no remapping: the .NET API validates Supabase's own JWT, so the `sub`
#  claim is the same UUID that Supabase wrote into these rows.
#
#  Usage:
#    export SUPABASE_DB_URL='postgresql://postgres:PASSWORD@db.xxxx.supabase.co:5432/postgres'
#    ./tools/migrate-english-from-supabase.sh
#
#  The password is Supabase -> Project Settings -> Database (NOT the anon/publishable key).
#  Pass it through the environment so it never lands in shell history or in this repo.
# ============================================================
set -euo pipefail

PG_IMAGE="postgres:16-alpine"
NAS_CONTAINER="${NAS_CONTAINER:-storechecking-db}"
NAS_USER="${NAS_USER:-storechecking}"
NAS_DB="${NAS_DB:-storechecking}"
TABLES=(english_words speaking_saved)
DUMP_DIR="${DUMP_DIR:-./_migration}"

if [[ -z "${SUPABASE_DB_URL:-}" ]]; then
  echo "ERROR: set SUPABASE_DB_URL first. See the header of this script." >&2
  exit 1
fi

mkdir -p "$DUMP_DIR"

# --- Row counts on Supabase, before anything is touched -------------------------------
echo "=== Rows on Supabase ==="
declare -A before
for t in "${TABLES[@]}"; do
  n=$(docker run --rm -e PGURL="$SUPABASE_DB_URL" "$PG_IMAGE" \
        psql "$SUPABASE_DB_URL" -tAc "select count(*) from public.$t")
  before[$t]=$n
  printf "  %-16s %s\n" "$t" "$n"
done

# --- Row counts already on the NAS ----------------------------------------------------
echo
echo "=== Rows already on the NAS (should normally be 0) ==="
for t in "${TABLES[@]}"; do
  n=$(docker exec "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -tAc "select count(*) from public.$t" 2>/dev/null || echo "TABLE MISSING")
  printf "  %-16s %s\n" "$t" "$n"
  if [[ "$n" == "TABLE MISSING" ]]; then
    echo >&2
    echo "ERROR: table $t does not exist on the NAS yet. Apply the schema first:" >&2
    echo "  docker exec -i $NAS_CONTAINER psql -U $NAS_USER -d $NAS_DB < db/002-english.sql" >&2
    exit 1
  fi
  if [[ "$n" != "0" ]]; then
    echo >&2
    echo "ERROR: $t already holds $n rows on the NAS. Refusing to run — a second pass" >&2
    echo "would duplicate everything. Empty the table first if you meant to re-import:" >&2
    echo "  docker exec $NAS_CONTAINER psql -U $NAS_USER -d $NAS_DB -c 'truncate table $t'" >&2
    exit 1
  fi
done

# --- Dump ------------------------------------------------------------------------------
echo
echo "=== Dumping (data only) ==="
ARGS=()
for t in "${TABLES[@]}"; do ARGS+=(--table="public.$t"); done
docker run --rm "$PG_IMAGE" \
  pg_dump "$SUPABASE_DB_URL" --data-only --no-owner --no-privileges "${ARGS[@]}" \
  > "$DUMP_DIR/english-data.sql"
echo "  wrote $DUMP_DIR/english-data.sql ($(wc -c < "$DUMP_DIR/english-data.sql") bytes)"

# --- Restore ---------------------------------------------------------------------------
# The NAS database container publishes no port on purpose, so feed psql from inside it.
echo
echo "=== Restoring into the NAS ==="
docker exec -i "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -v ON_ERROR_STOP=1 \
  < "$DUMP_DIR/english-data.sql" > /dev/null
echo "  done"

# --- Verify ----------------------------------------------------------------------------
echo
echo "=== Verifying ==="
fail=0
for t in "${TABLES[@]}"; do
  after=$(docker exec "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -tAc "select count(*) from public.$t")
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
