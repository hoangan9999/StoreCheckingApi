-- ============================================================
--  Inventory (Nhập hàng / Kho hàng / Bán hàng): batches, products, sales, damages,
--  two roll-up views, and the trigger that refuses to oversell.
--
--  Ported from supabase/schema.sql + migration-sales-global.sql + migration-sale-note.sql
--  + migration-shipping-damage.sql, DROPPING auth.uid(), the foreign keys to auth.users,
--  and RLS. The views also drop `with (security_invoker = true)`, which only meant
--  anything alongside RLS policies.
--
--  This is the most valuable data in the whole application, which is why it migrates last.
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Batches ----------
create table if not exists public.batches (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid not null,
  name        text not null,
  import_date date not null default current_date,
  total_cost  numeric(14,2) not null default 0,   -- what the batch cost to buy, for profit
  note        text,
  priority    int,                                -- manual display order; null = unset
  created_at  timestamptz not null default now()
);

-- ---------- Products within a batch ----------
create table if not exists public.products (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid not null,
  batch_id    uuid not null references public.batches(id) on delete cascade,
  name        text not null,
  quantity    integer not null check (quantity >= 0),        -- how many came in
  sell_price  numeric(14,2) not null check (sell_price >= 0),
  created_at  timestamptz not null default now()
);

-- ---------- Sales ----------
create table if not exists public.sales (
  id            uuid primary key default gen_random_uuid(),
  user_id       uuid not null,
  product_id    uuid not null references public.products(id) on delete cascade,
  batch_id      uuid not null references public.batches(id) on delete cascade,
  quantity      integer not null check (quantity > 0),
  sell_price    numeric(14,2) not null check (sell_price >= 0),  -- price at the time of sale
  note          text,
  sale_group_id uuid,                    -- ties the lines of one multi-item sale together
  sold_at       timestamptz not null default now()
);

-- ---------- Damaged in shipping ----------
create table if not exists public.product_damages (
  id          uuid primary key default gen_random_uuid(),
  user_id     uuid not null,
  product_id  uuid not null references public.products(id) on delete cascade,
  batch_id    uuid not null references public.batches(id) on delete cascade,
  quantity    integer not null check (quantity > 0),
  note        text,
  created_at  timestamptz not null default now()
);

create index if not exists idx_products_batch  on public.products (batch_id);
create index if not exists idx_sales_product   on public.sales (product_id);
create index if not exists idx_sales_batch     on public.sales (batch_id);
create index if not exists idx_sales_group     on public.sales (sale_group_id);
create index if not exists idx_damages_product on public.product_damages (product_id);
create index if not exists idx_damages_batch   on public.product_damages (batch_id);

-- ============================================================
--  Refuse to sell more than is in stock.
--
--  This is business logic that lives in the database, and it is kept there on purpose.
--  On Supabase it meant a bug in the app still could not oversell, and that property is
--  worth keeping: the Application layer checks too, so the user gets a civil message
--  instead of a raised exception, but the database stays the last line of defence. Same
--  reasoning as the owner query filters — do not let one layer carry it alone.
--
--  Plain plpgsql with no auth.uid() anywhere, so it ports across unchanged.
--
--  `id <> new.id` excludes the row being written, which makes the sum correct for UPDATE
--  as well as INSERT.
-- ============================================================
create or replace function public.check_stock()
returns trigger
language plpgsql
security definer
set search_path = public
as $$
declare
  available integer;
begin
  select p.quantity
       - coalesce((select sum(quantity) from public.sales
                    where product_id = p.id and id <> new.id), 0)
       - coalesce((select sum(quantity) from public.product_damages
                    where product_id = p.id), 0)
    into available
    from public.products p
   where p.id = new.product_id;

  if new.quantity > available then
    raise exception 'Không đủ tồn kho: còn %, yêu cầu bán %', available, new.quantity;
  end if;
  return new;
end;
$$;

drop trigger if exists trg_check_stock on public.sales;
create trigger trg_check_stock
  before insert or update on public.sales
  for each row execute function public.check_stock();

-- ============================================================
--  Roll-up views
--  Both keep user_id in their output, which is what lets EF Core apply the same owner
--  filter to them as to a table.
-- ============================================================

-- Stock per product
create or replace view public.product_stock as
select
  p.id, p.user_id, p.batch_id, p.name, p.quantity, p.sell_price, p.created_at,
  coalesce(s.sold_qty, 0)              as sold_qty,
  p.quantity - coalesce(s.sold_qty, 0) as remaining,
  coalesce(s.revenue, 0)               as revenue
from public.products p
left join (
  select product_id,
         sum(quantity)              as sold_qty,
         sum(quantity * sell_price) as revenue
  from public.sales
  group by product_id
) s on s.product_id = p.id;

-- Per-batch summary
create or replace view public.batch_summary as
select
  b.id, b.user_id, b.name, b.import_date, b.total_cost, b.note, b.created_at,
  coalesce(pr.product_count, 0)                        as product_count,
  coalesce(pr.total_qty, 0)                            as total_qty,
  coalesce(sa.sold_qty, 0)                             as sold_qty,
  coalesce(pr.total_qty, 0) - coalesce(sa.sold_qty, 0) as remaining_qty,
  coalesce(sa.revenue, 0)                              as revenue,
  coalesce(sa.revenue, 0) - b.total_cost               as profit
from public.batches b
left join (
  select batch_id, count(*) as product_count, sum(quantity) as total_qty
  from public.products group by batch_id
) pr on pr.batch_id = b.id
left join (
  select batch_id, sum(quantity) as sold_qty, sum(quantity * sell_price) as revenue
  from public.sales group by batch_id
) sa on sa.batch_id = b.id;
