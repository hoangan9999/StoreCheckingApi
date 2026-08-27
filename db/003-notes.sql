-- ============================================================
--  Quick notes (Marketing tab -> Ghi chú).
--  Holds things worth copying rather than retyping: bank details, message
--  templates, sizes.
--
--  Ported from supabase/migration-notes.sql, DROPPING auth.uid() and RLS
--  (owner filtering is handled by EF Core's global query filter instead).
--
--  Postgres runs everything in /docker-entrypoint-initdb.d/ automatically, but ONLY
--  while the data volume is still empty. On an existing deployment this file has to be
--  applied by hand:
--    docker exec -i storechecking-db psql -U storechecking -d storechecking < db/003-notes.sql
-- ============================================================
create extension if not exists "pgcrypto";

create table if not exists public.notes (
  id         uuid primary key default gen_random_uuid(),
  -- No `default auth.uid()` and no foreign key to auth.users: neither exists here. The
  -- value always comes from the token's `sub` claim, set by the API.
  user_id    uuid not null,
  title      text,
  content    text not null default '',
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

-- Listing shows the most recently touched note first, per user.
create index if not exists notes_user_updated_idx
  on public.notes (user_id, updated_at desc);
