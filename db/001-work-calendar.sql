-- ============================================================
--  Work calendar schema for the self-hosted PostgreSQL on the NAS.
--  Ported from supabase/migration-work-calendar.sql and
--  supabase/migration-work-month-notes.sql, DROPPING auth.uid() and RLS
--  (owner filtering is handled by EF Core's global query filter instead).
--
--  Postgres runs everything in /docker-entrypoint-initdb.d/ automatically on
--  first start, while the data volume is still empty. Once the volume has data
--  it skips these files entirely.
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Day cells ----------
create table if not exists public.work_days (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  day        date not null,
  note       text not null default '',
  color      text,                       -- colour key ('do', 'cam', 'vang'...) or null
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (user_id, day)
);

-- ---------- Month notes ----------
-- period = first day of the selected month. The calendar cycle runs from the 26th of the
-- previous month to the 25th, so the cycle 26 Sep -> 25 Oct 2026 has period = 2026-10-01.
create table if not exists public.work_month_notes (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  period     date not null,
  content    text not null default '',
  sort       int  not null default 0,
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now()
);

create index if not exists work_month_notes_user_period_idx
  on public.work_month_notes (user_id, period, sort);
