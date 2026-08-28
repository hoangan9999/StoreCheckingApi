#!/usr/bin/env bash
# ============================================================
#  Copy EVERY table from Supabase to the NAS in one pass.
#
#  RUNS ON THE NAS HOST over SSH, not inside a container and not on the PC. It needs the
#  Docker CLI, because the dump has to happen in a PostgreSQL 17 image:
#
#    Supabase runs 17.6 while the NAS container is postgres:16-alpine, and pg_dump REFUSES
#    to dump a server newer than itself ("aborting because of server version mismatch").
#    psql is not fussy that way, so counting still works from the 16 container — only the
#    dump needs a throwaway 17 image.
#
#    ssh <user>@192.168.1.76
#    export SUPABASE_DB_URL='postgresql://postgres.<ref>:PASSWORD@aws-0-<region>.pooler.supabase.com:5432/postgres'
#    bash ~/mig.sh
#
#  Use the SESSION POOLER connection string, from the dashboard's Connect dialog. Not the
#  direct connection: db.<ref>.supabase.co now resolves to IPv6 only, and the NAS has no
#  IPv6, which fails as "Address not available" — reading like a dead server rather than an
#  addressing problem. Not the transaction pooler on 6543 either: it cannot serve pg_dump.
#  The user name carries the project ref (postgres.<ref>), and a password starting with @
#  must be written %40.
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
# ============================================================
#
# pipefail is NOT optional here. Without it the status of `pg_dump | psql` is psql's alone,
# and psql happily succeeds on the empty input a failed pg_dump leaves behind — which is
# exactly how an earlier version of this script reported "OK" for fifteen tables it had
# copied nothing into.
set -uo pipefail

NAS_CONTAINER="${NAS_CONTAINER:-storechecking-db}"
NAS_USER="${NAS_USER:-storechecking}"
NAS_DB="${NAS_DB:-storechecking}"

# Synology needs root for the Docker socket. Run `sudo -v` once first so the password
# prompt does not appear in the middle of the loop.
DOCKER="${DOCKER:-sudo docker}"

# Must be at least as new as the Supabase server.
DUMP_IMAGE="${DUMP_IMAGE:-postgres:17-alpine}"

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

# Counting runs through the NAS container's own psql for both sides: it already exists, and
# psql talks to any server version.
nas() { $DOCKER exec "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -tAc "$1"; }
sup() { $DOCKER exec "$NAS_CONTAINER" psql "$SUPABASE_DB_URL" -tAc "$1"; }

echo "Kéo ảnh $DUMP_IMAGE (chỉ lần đầu)…"
$DOCKER pull -q "$DUMP_IMAGE" >/dev/null

echo
echo "=== TRƯỚC ==="
printf "%-26s %-10s %s\n" "BẢNG" "SUPABASE" "NAS"
for t in $TABLES; do
  printf "%-26s %-10s %s\n" "$t" "$(sup "select count(*) from public.$t")" "$(nas "select count(*) from public.$t")"
done

echo
echo "=== CHÉP ==="
for t in $TABLES; do
  have=$(nas "select count(*) from public.$t")

  if [ "$have" != "0" ]; then
    echo "  BỎ QUA  $t (NAS đã có $have dòng)"
    continue
  fi

  # `SET transaction_timeout` is new in PostgreSQL 17 and unknown to 16, so the dump's
  # preamble would abort the restore under ON_ERROR_STOP. Dropping that one line is enough;
  # every other SET pg_dump emits has existed for years.
  if $DOCKER run --rm "$DUMP_IMAGE" \
       pg_dump "$SUPABASE_DB_URL" --data-only --no-owner --no-privileges --table="public.$t" \
     | sed '/^SET transaction_timeout/d' \
     | $DOCKER exec -i "$NAS_CONTAINER" psql -U "$NAS_USER" -d "$NAS_DB" -v ON_ERROR_STOP=1 -q
  then
    echo "  OK      $t -> $(nas "select count(*) from public.$t") dòng"
  else
    echo "  HỎNG    $t"
  fi
done

echo
echo "=== SAU ==="
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
if [ -z "$bad" ]; then
  echo "Xong. Số dòng khớp ở cả hai bên."
  echo "Dữ liệu trên Supabase KHÔNG bị xoá — để nguyên tới khi app chạy ổn một thời gian."
else
  echo "Số dòng lệch:$bad" >&2
  echo "Đừng cắt app sang cho tới khi giải quyết xong." >&2
  exit 1
fi
