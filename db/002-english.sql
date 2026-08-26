-- ============================================================
--  English practice schema.
--  Ported from supabase/migration-english.sql and
--  supabase/migration-speaking-saved.sql, DROPPING auth.uid() and RLS
--  (owner filtering is handled by EF Core's global query filter instead).
--
--  Postgres runs everything in /docker-entrypoint-initdb.d/ automatically, but ONLY
--  while the data volume is still empty. On an existing deployment this file has to be
--  applied by hand:
--    docker exec -i storechecking-db psql -U storechecking -d storechecking < db/002-english.sql
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Saved vocabulary ----------
-- `data` holds the whole AI result (meaning + one example sentence per tense) as JSON,
-- so a saved word can be reviewed in full without calling the AI again.
create table if not exists public.english_words (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  word       text not null,
  data       jsonb not null,
  created_at timestamptz not null default now()
);

create index if not exists english_words_user_created_idx
  on public.english_words (user_id, created_at desc);

-- ---------- Sentences saved during speaking practice ----------
-- `note` records where the sentence came from ("câu trả lời mẫu", "cách nói tự nhiên hơn"…).
create table if not exists public.speaking_saved (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  text       text not null,
  note       text not null default '',
  created_at timestamptz not null default now()
);

create index if not exists speaking_saved_user_created_idx
  on public.speaking_saved (user_id, created_at desc);
