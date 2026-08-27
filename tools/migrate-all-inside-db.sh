#!/usr/bin/env bash
# ============================================================
#  Copy EVERY table from Supabase to the NAS in one pass.
#
#  RUNS INSIDE THE storechecking-db CONTAINER, not on the NAS host and not on the PC.
#  That container is postgres:16-alpine, so it already has psql and pg_dump, it can reach
#  Supabase over the internet, and it reaches the local database over its unix socket.
#  No Docker and therefore no SSH needed — Container Manager's terminal is enough.
#
#    Container Manager -> Container -> storechecking-db -> Details -> Terminal -> Create
#
#  Copying everything at once is only safe because this application has a single user who
#  is not entering new data during the migration. With anyone still writing to Supabase,
#  every table copied before its cut-over would go stale and have to be copied again.
#
#  orders is deliberately absent: it stays on Supabase for good, because the public /order
#  page customers scan must not depend on the house network.
#
#  Safe to re-run. Any table that already holds rows on the NAS is SKIPPED, never appended
#  to — pg_dump --data-only has no notion of "already there", so a second pass over a full
#  table would silently double it.
#
#  Usage, pasted into that terminal:
#    export SUPABASE_DB_URL='postgresql://postgres:PASSWORD@db.xxxx.supabase.co:5432/postgres'
#    ./migrate-all-inside-db.sh            # or paste the body directly
#
#  Use the SESSION POOLER connection string, from the dashboard's Connect dialog:
#
#    postgresql://postgres.<project-ref>:PASSWORD@aws-0-<region>.pooler.supabase.com:5432/postgres
#
#  Not the direct connection: db.<project-ref>.supabase.co now resolves to IPv6 only, and a
#  container without IPv6 fails with "Address not available", which reads like the server
#  is down rather than like an addressing problem. Not the transaction pooler on 6543
#  either — that one cannot serve pg_dump. The session pooler has IPv4 and does.
#
#  Note the user name carries the project ref: postgres.<project-ref>, not plain postgres.
#
#  The password is the DATABASE password (Project Settings -> Database), not the anon or
#  service_role key.
# ============================================================
set -uo pipefail

NAS_USER="${NAS_USER:-storechecking}"
NAS_DB="${NAS_DB:-storechecking}"

# Parent tables BEFORE their children. expenses references expense_categories, products
# references batches, sales and product_damages reference both — a child restored first
# fails its foreign key.
TABLES="
work_days
work_month_notes
english_words
speaking_saved
notes
packing_videos
expense_categories
expenses
monthly_income
marketing_groups
marketing_posts
marketing_post_targets
post_queue
batches
products
sales
product_damages
"

if [ -z "${SUPABASE_DB_URL:-}" ]; then
  echo "ERROR: export SUPABASE_DB_URL first. See the header of this script." >&2
  exit 1
fi

sup() { psql "$SUPABASE_DB_URL" -tAc "$1"; }
nas() { psql -U "$NAS_USER" -d "$NAS_DB" -tAc "$1"; }

echo "=== Before ==="
printf "%-26s %-10s %s\n" "TABLE" "SUPABASE" "NAS"
for t in $TABLES; do
  printf "%-26s %-10s %s\n" "$t" "$(sup "select count(*) from public.$t")" "$(nas "select count(*) from public.$t")"
done

echo
echo "=== Copying ==="
failed=""
for t in $TABLES; do
  have=$(nas "select count(*) from public.$t")

  if [ "$have" != "0" ]; then
    echo "  SKIP    $t (NAS đã có $have dòng)"
    continue
  fi

  if pg_dump "$SUPABASE_DB_URL" --data-only --no-owner --no-privileges --table="public.$t" \
     | psql -U "$NAS_USER" -d "$NAS_DB" -v ON_ERROR_STOP=1 -q; then
    echo "  OK      $t -> $(nas "select count(*) from public.$t") dòng"
  else
    echo "  HỎNG    $t"
    failed="$failed $t"
  fi
done

echo
echo "=== After ==="
bad=""
for t in $TABLES; do
  a=$(sup "select count(*) from public.$t")
  b=$(nas "select count(*) from public.$t")
  if [ "$a" = "$b" ]; then
    printf "  [OK]   %-26s %s\n" "$t" "$b"
  else
    printf "  [LỆCH] %-26s Supabase=%s NAS=%s\n" "$t" "$a" "$b"
    bad="$bad $t"
  fi
done

echo
if [ -z "$bad" ] && [ -z "$failed" ]; then
  echo "Xong. Số dòng khớp ở cả hai bên."
  echo "Dữ liệu trên Supabase KHÔNG bị xoá — để nguyên tới khi app chạy ổn một thời gian."
else
  [ -n "$failed" ] && echo "Chép hỏng:$failed" >&2
  [ -n "$bad" ] && echo "Số dòng lệch:$bad" >&2
  echo "Đừng cắt app sang cho tới khi giải quyết xong." >&2
  exit 1
fi
