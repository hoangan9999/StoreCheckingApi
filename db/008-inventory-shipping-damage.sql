-- ============================================================
--  Fixes for 007-inventory.sql, ported from supabase/migration-shipping-damage.sql.
--
--  That file was read for its product_damages table and its check_stock() function, and
--  four other things in it were missed. The first one surfaced the hard way: copying the
--  sales data failed with
--      ERROR: column "shipping_fee" of relation "sales" does not exist
--  and the rest were found by reading the whole file properly afterwards.
--
--  A separate file rather than an edit to 007: SchemaMigrator records a checksum for every
--  file it has applied and refuses to start if one changes underneath it. Editing an
--  applied file would mean the database and the repository disagree with nobody noticing —
--  which is exactly what that check exists to prevent. New facts go in a new file.
-- ============================================================

-- ---------- 1. The missing column ----------
-- Shipping paid out of the sale, subtracted from revenue. Recorded against the first line
-- of a multi-item sale, 0 on the rest.
alter table public.sales add column if not exists shipping_fee numeric(14,2) not null default 0;

-- ---------- 2. Damage must not exceed available stock either ----------
-- The mirror of check_stock: that one stops overselling, this one stops writing off more
-- units as damaged than actually remain. Same reasoning for keeping it in the database —
-- it holds even when a bug in the application would let it through.
create or replace function public.check_damage()
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
                    where product_id = p.id), 0)
       - coalesce((select sum(quantity) from public.product_damages
                    where product_id = p.id and id <> new.id), 0)
    into available
    from public.products p
   where p.id = new.product_id;

  if new.quantity > available then
    raise exception 'Không đủ tồn để ghi hư: còn %, yêu cầu %', available, new.quantity;
  end if;
  return new;
end;
$$;

drop trigger if exists trg_check_damage on public.product_damages;
create trigger trg_check_damage
  before insert or update on public.product_damages
  for each row execute function public.check_damage();

-- ---------- 3. Both views were wrong in two ways ----------
-- They reported revenue WITHOUT subtracting shipping, and left out damaged stock entirely,
-- so remaining quantities counted written-off units as still on the shelf.
--
-- DROP then CREATE, not CREATE OR REPLACE: damaged_qty lands in the middle of the column
-- list, and replace cannot reorder or insert columns.
--
-- No `with (security_invoker = true)` here. That belongs to Supabase's row level security;
-- ownership is filtered by EF Core's global query filters instead.

drop view if exists public.product_stock;
create view public.product_stock as
select
  p.id, p.user_id, p.batch_id, p.name, p.quantity, p.sell_price, p.created_at,
  coalesce(s.sold_qty, 0)                                           as sold_qty,
  coalesce(d.damaged_qty, 0)                                        as damaged_qty,
  p.quantity - coalesce(s.sold_qty, 0) - coalesce(d.damaged_qty, 0) as remaining,
  coalesce(s.revenue, 0)                                            as revenue
from public.products p
left join (
  select product_id,
         sum(quantity)                             as sold_qty,
         sum(quantity * sell_price - shipping_fee) as revenue
  from public.sales
  group by product_id
) s on s.product_id = p.id
left join (
  select product_id, sum(quantity) as damaged_qty
  from public.product_damages
  group by product_id
) d on d.product_id = p.id;

drop view if exists public.batch_summary;
create view public.batch_summary as
select
  b.id, b.user_id, b.name, b.import_date, b.total_cost, b.note, b.created_at,
  coalesce(pr.product_count, 0) as product_count,
  coalesce(pr.total_qty, 0)     as total_qty,
  coalesce(sa.sold_qty, 0)      as sold_qty,
  coalesce(da.damaged_qty, 0)   as damaged_qty,
  coalesce(pr.total_qty, 0) - coalesce(sa.sold_qty, 0) - coalesce(da.damaged_qty, 0)
                                as remaining_qty,
  coalesce(sa.revenue, 0)                as revenue,
  coalesce(sa.revenue, 0) - b.total_cost as profit
from public.batches b
left join (
  select batch_id, count(*) as product_count, sum(quantity) as total_qty
  from public.products
  group by batch_id
) pr on pr.batch_id = b.id
left join (
  select batch_id,
         sum(quantity)                             as sold_qty,
         sum(quantity * sell_price - shipping_fee) as revenue
  from public.sales
  group by batch_id
) sa on sa.batch_id = b.id
left join (
  select batch_id, sum(quantity) as damaged_qty
  from public.product_damages
  group by batch_id
) da on da.batch_id = b.id;
