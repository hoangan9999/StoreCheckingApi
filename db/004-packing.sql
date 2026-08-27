-- ============================================================
--  Packing videos (Đóng gói tab).
--  Metadata only. The video files themselves already live on the NAS, uploaded through
--  nas.service.ts — Supabase never held them, so there is nothing to move but rows.
--
--  Ported from supabase/migration-packing.sql + migration-packing-filename.sql,
--  DROPPING auth.uid() and RLS (EF Core's global query filter replaces both).
-- ============================================================
create extension if not exists "pgcrypto";

create table if not exists public.packing_videos (
  id          uuid primary key default gen_random_uuid(),
  -- No `default auth.uid()` and no foreign key to auth.users: neither exists here. The
  -- value always comes from the token's `sub` claim, set by the API.
  user_id     uuid not null,
  order_code  text not null,          -- order code, from a QR scan or typed in
  seq         integer not null,       -- which recording this is for that order (1, 2, 3...)
  note        text,
  filename    text,                   -- actual file on the NAS: <order_code>_<seq>.<ext>
  recorded_at timestamptz not null default now(),
  created_at  timestamptz not null default now()
);

create index if not exists idx_packing_order on public.packing_videos (user_id, order_code);
create index if not exists idx_packing_time  on public.packing_videos (user_id, recorded_at);
