-- ============================================================
--  Expenses (Chi tiêu tab): categories, spending, monthly income, and two roll-up views.
--
--  Ported from supabase/schema-expenses.sql, DROPPING auth.uid(), the foreign keys to
--  auth.users, and RLS. The views also drop `with (security_invoker = true)`: that setting
--  exists to make a view respect the caller's RLS policies, and there are no policies here
--  — the API filters by owner through EF Core's global query filters instead.
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Categories ----------
create table if not exists public.expense_categories (
  id             uuid primary key default gen_random_uuid(),
  user_id        uuid not null,
  name           text not null,
  monthly_budget numeric(14,2),                       -- null = no budget set
  type           text not null default 'variable' check (type in ('fixed','variable')),
  icon           text,                                -- emoji
  daily_limit    numeric(14,2),                       -- null = no per-day warning
  note           text,
  sort_order     integer not null default 0,
  created_at     timestamptz not null default now()
);

-- ---------- Spending ----------
create table if not exists public.expenses (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid not null,
  -- `on delete restrict` is kept: deleting a category that still has spending against it
  -- would silently lose the history of where the money went.
  category_id uuid not null references public.expense_categories(id) on delete restrict,
  spent_on    date not null default current_date,
  description text,
  amount      numeric(14,2) not null check (amount >= 0),
  note        text,
  created_at  timestamptz not null default now()
);

-- ---------- Monthly income ----------
create table if not exists public.monthly_income (
  id      uuid primary key default gen_random_uuid(),
  user_id uuid not null,
  year    integer not null,
  month   integer not null check (month between 1 and 12),
  income  numeric(14,2) not null default 0,
  note    text,
  unique (user_id, year, month)
);

create index if not exists idx_expenses_cat  on public.expenses (category_id);
create index if not exists idx_expenses_date on public.expenses (spent_on);
create index if not exists idx_expenses_user_date on public.expenses (user_id, spent_on desc);

-- ============================================================
--  Roll-up views
--  Both keep user_id in their output, which is what lets EF Core apply the same owner
--  filter to them as to a table.
-- ============================================================

-- Spending per category per month
create or replace view public.v_expense_month_category as
select
  user_id,
  category_id,
  extract(year  from spent_on)::int as year,
  extract(month from spent_on)::int as month,
  sum(amount) as spent,
  count(*)    as tx_count
from public.expenses
group by user_id, category_id, 3, 4;

-- Total spending per month, across every category
create or replace view public.v_expense_month_total as
select
  user_id,
  extract(year  from spent_on)::int as year,
  extract(month from spent_on)::int as month,
  sum(amount) as spent
from public.expenses
group by user_id, 2, 3;
