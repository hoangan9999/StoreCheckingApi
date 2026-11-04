-- ============================================================
--  Mặt hàng (`product_kinds`) — danh tính ổn định cho một thứ được bán.
--
--  Tới trước file này, "Basic" không tồn tại như một thứ gì cả: chỉ có mấy dòng
--  `products` rời rạc, mỗi lô một dòng, tình cờ trùng tên. Đo trên dữ liệu thật:
--  `Basic` nằm ở 3 lô, `Premium` 3 lô, `Basic Dài` và `Stransport` mỗi cái 2 lô — và
--  có cả `'Basic'` lẫn `'Basic '` (dấu cách cuối) là hai dòng khác nhau.
--
--  Thiếu khái niệm này thì mọi thứ muốn chỉ vào "Basic" đều phải **so chuỗi tên**, mà
--  so chuỗi thì đổi tên một lần là hỏng âm thầm: chỗ chỉ vào nó vẫn chạy, vẫn không
--  báo lỗi, chỉ là không khớp gì nữa. Vì vậy ở đây sinh ra một `id`, và từ nay mọi chỗ
--  cần nói "mặt hàng nào" đều dùng `id` đó.
--
--  `products.name` vẫn còn và vẫn là chữ hiện trên giao diện. Nó KHÔNG còn là danh tính,
--  nhưng cũng chưa phải bản chiếu tự động — và hiện tại không cần, vì **chưa có đường nào
--  đổi tên một mặt hàng**: chỗ ghi duy nhất là thêm/sửa sản phẩm, và chỗ đó tìm-hoặc-tạo
--  mặt hàng theo đúng cái tên vừa gõ, nên hai bên không thể lệch nhau.
--
--  ⚠️ Nếu sau này thêm chức năng ĐỔI TÊN MẶT HÀNG thì phải xử lý chỗ này, chọn một trong
--  hai — đừng để trống: cập nhật `products.name` trong cùng transaction, hoặc chuyển view
--  `product_stock` sang đọc tên từ `product_kinds` rồi bỏ hẳn `products.name`. Để nguyên
--  là tên mặt hàng đổi mà mọi màn hình vẫn hiện tên cũ.
-- ============================================================

create table if not exists public.product_kinds (
  id         uuid        not null default gen_random_uuid(),
  user_id    uuid        not null,

  -- Chữ hiện ra, đổi tự do. KHÔNG phải khoá — `id` mới là khoá.
  name       text        not null,

  created_at timestamptz not null default now(),

  constraint product_kinds_pkey primary key (id)
);

-- Chặn tạo trùng mặt hàng do khác dấu cách hoặc khác hoa thường. Đây là phép so chuỗi
-- DUY NHẤT còn lại, và nó chỉ dùng lúc TẠO để khỏi sinh hai "Basic" — không có chỗ nào
-- tra cứu bằng tên nữa.
create unique index if not exists product_kinds_name_key
  on public.product_kinds (user_id, lower(btrim(name)));

create index if not exists product_kinds_user_idx
  on public.product_kinds (user_id);

-- ------------------------------------------------------------
--  Nối `products` vào mặt hàng.
--
--  Để `null` được vì cột thêm vào bảng đã có dữ liệu; phần nạp bên dưới điền hết, và API
--  tự tìm-hoặc-tạo mặt hàng mỗi lần thêm/sửa sản phẩm nên thực tế không bao giờ trống.
-- ------------------------------------------------------------
alter table public.products
  add column if not exists kind_id uuid null references public.product_kinds(id);

create index if not exists products_kind_idx
  on public.products (kind_id);

-- ------------------------------------------------------------
--  Nạp lần đầu: mỗi tên sản phẩm khác nhau thành một mặt hàng.
--
--  Cả khối này chạy lại được — API nạp `db/*.sql` mỗi lần khởi động, nên không được
--  sinh thêm mặt hàng ở lần chạy thứ hai.
-- ------------------------------------------------------------
insert into public.product_kinds (user_id, name)
select p.user_id, min(btrim(p.name))
from public.products p
group by p.user_id, lower(btrim(p.name))
on conflict (user_id, lower(btrim(name))) do nothing;

update public.products p
set kind_id = k.id
from public.product_kinds k
where k.user_id = p.user_id
  and lower(btrim(k.name)) = lower(btrim(p.name))
  and p.kind_id is distinct from k.id;

-- `products.name` từ nay là bản chiếu, nên kéo nó về đúng tên mặt hàng. Việc này cũng
-- dọn luôn `'Basic '` thành `'Basic'`.
update public.products p
set name = k.name
from public.product_kinds k
where p.kind_id = k.id and p.name <> k.name;

-- ------------------------------------------------------------
--  Thêm `kind_id` vào view tồn kho.
--
--  Để trừ tồn thì phải hỏi được "mặt hàng này còn mấy cái, rải ở những lô nào" trong một
--  lượt. Không có cột này thì phải lấy từng dòng `products` rồi đếm tồn từng dòng một —
--  mỗi lần mở màn hình là chục lượt gọi database cho một câu hỏi vốn một câu trả lời.
--
--  `drop` rồi `create` chứ không `create or replace`: replace không chèn được cột, chỉ
--  thêm được vào cuối danh sách cũ. Cột mới vẫn để ở CUỐI để thứ tự cột cũ không đổi.
--  `batch_summary` đọc thẳng từ `products`/`sales`/`product_damages` nên không bị ảnh
--  hưởng khi view này bị bỏ đi một nhịp.
-- ------------------------------------------------------------
drop view if exists public.product_stock;
create view public.product_stock as
select
  p.id, p.user_id, p.batch_id, p.name, p.quantity, p.sell_price, p.created_at,
  coalesce(s.sold_qty, 0)                                           as sold_qty,
  coalesce(d.damaged_qty, 0)                                        as damaged_qty,
  p.quantity - coalesce(s.sold_qty, 0) - coalesce(d.damaged_qty, 0) as remaining,
  coalesce(s.revenue, 0)                                            as revenue,
  p.kind_id
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
