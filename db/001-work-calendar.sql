-- ============================================================
--  Lịch làm — schema cho Postgres tự dựng trên NAS.
--  Chuyển từ supabase/migration-work-calendar.sql và
--  supabase/migration-work-month-notes.sql, BỎ phần auth.uid() và RLS
--  (việc lọc theo chủ sở hữu do global query filter của EF Core lo).
--
--  File này nằm trong /docker-entrypoint-initdb.d/ nên Postgres TỰ CHẠY
--  lần đầu khi volume còn trống. Volume đã có dữ liệu thì nó bỏ qua.
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Ô ngày ----------
create table if not exists public.work_days (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  day        date not null,
  note       text not null default '',
  color      text,                       -- khoá màu ('do','cam','vang'...) hoặc null
  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),
  unique (user_id, day)
);

-- ---------- Ghi chú chung theo tháng ----------
-- period = ngày 1 của tháng đang chọn. Chu kỳ 26/9 -> 25/10/2026 thì period = 2026-10-01.
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
