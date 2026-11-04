-- ============================================================
--  Trừ tồn kho từ nhãn vận đơn.
--
--  Tab Đóng gói đã quét mã vận đơn, và nhãn tải lên đã đọc được danh sách món
--  (`labels/<mã>.json` trên NAS: `name`, `variant`, `qty`). Thiếu đúng một thứ để nối hai
--  đầu đó với kho: trên nhãn không có mã nào trỏ về mặt hàng.
--
--  ⚠️ KHÔNG ghép gần đúng. Ghép mờ hai chuỗi chữ tự do rồi trừ tồn là trừ nhầm mặt hàng
--  mà không có gì báo lỗi, chỉ phát hiện khi kiểm kho lệch — lúc đó không truy lại được
--  đơn nào gây ra. Chưa ghép thì không trừ.
-- ============================================================

-- ------------------------------------------------------------
--  Bút danh: chữ trên nhãn là mấy cái của mặt hàng nào.
--
--  Đây là bảng BÚT DANH, không phải bảng ánh xạ một-một: nhiều dòng cùng trỏ về một
--  `kind_id`. Chữ trên nhãn do TikTok/SPX in nên mình không kiểm soát, và đó là chỗ duy
--  nhất còn phải so chuỗi. Dựng thành bút danh để chuyện đó không đau:
--
--  - Đổi tên phân loại bên TikTok → chuỗi mới KHÔNG khớp bút danh nào → rơi vào danh
--    sách "chưa ghép", **nhìn thấy được**. Thêm một bút danh nữa là xong, bút danh cũ để
--    nguyên cũng vô hại.
--  - Đổi tên mặt hàng bên kho → **không ảnh hưởng gì**, vì ở đây giữ `kind_id` chứ không
--    giữ tên.
--
--  Khoá tra cứu là (`source_name`, `variant`) đã chuẩn hoá: cắt khoảng trắng hai đầu, bóp
--  khoảng trắng giữa, hạ chữ thường. `display` giữ nguyên văn một lần thấy đầu tiên, vì
--  khoá đã hạ chữ thường thì hiện ra người dùng không nhận ra tên món của mình.
--
--  `kind_id IS NULL` nghĩa là **bỏ qua dòng này** — món không theo dõi tồn (xe Hot
--  Wheels). Nhờ có hàng đó, một chuỗi lạ chỉ lạ đúng một lần: ghép, hoặc bỏ qua. Không có
--  nó thì mọi đơn xe nằm mãi trong "chưa ghép" và danh sách đó thành vô dụng.
-- ------------------------------------------------------------
create table if not exists public.label_item_aliases (
  id           uuid        not null default gen_random_uuid(),
  user_id      uuid        not null,

  -- Khoá tra cứu, đã chuẩn hoá.
  source_name  text        not null,
  variant      text        not null,

  -- Nguyên văn, chỉ để hiện ra.
  display      text        not null,

  -- NULL = bỏ qua dòng này. Xoá mặt hàng thì bút danh đi theo: một bút danh trỏ vào mặt
  -- hàng không còn tồn tại sẽ im lặng không trừ gì, đúng kiểu hỏng không dấu vết.
  kind_id      uuid        null references public.product_kinds(id) on delete cascade,

  -- Một món trên nhãn bằng mấy cái trong kho. "Protect Basic (Combo 2 cái )" = 2.
  -- Thiếu nó là trừ sai gấp đôi, gấp mười mà con số vẫn trông hợp lý.
  units        integer     not null default 1,

  created_at   timestamptz not null default now(),
  updated_at   timestamptz not null default now(),

  constraint label_item_aliases_pkey primary key (id),
  constraint label_item_aliases_key unique (user_id, source_name, variant),
  constraint label_item_aliases_units_positive check (units > 0),

  -- Bỏ qua thì không có hệ số nào có nghĩa; ép về 1 để không ai đọc `units` của một dòng
  -- bỏ qua rồi tưởng nó có ý nghĩa.
  constraint label_item_aliases_skip_has_no_units
    check (kind_id is not null or units = 1)
);

create index if not exists label_item_aliases_user_idx
  on public.label_item_aliases (user_id);

-- Tra "mặt hàng này đang nhận những bút danh nào", để hiện khi sửa mặt hàng.
create index if not exists label_item_aliases_kind_idx
  on public.label_item_aliases (kind_id);

-- ------------------------------------------------------------
--  Đơn nào đã trừ tồn rồi.
--
--  ⚠️ Đây là chỗ chống trừ HAI LẦN, và nó phải là một bản ghi thật trong database:
--
--  - Không để trong `labels/<mã>.json` trên NAS: tải lại nhãn là ghi đè cả file, cờ bị
--    xoá, và lần tách sau trừ thêm một lần nữa.
--  - Không suy từ bảng `sales`: danh sách đó bị lọc theo ngày và chặn số dòng ở tầng trên,
--    nên "không thấy trong danh sách" không có nghĩa là "chưa trừ".
--
--  Khoá chính là (`user_id`, `order_code`) nên trừ hai lần không phải là lỗi cần nhớ
--  kiểm — database tự chặn.
--
--  `sale_group_id` LUÔN có giá trị ở đường tự động, kể cả đơn một dòng. Đường bán tay để
--  `sale_group_id` null khi chỉ có một dòng (giữ cho dòng cũ và mới trông giống nhau),
--  nhưng ở đây cần trả lời được "đơn này sinh ra những dòng bán nào" để còn hoàn lại
--  được, nên không bỏ trống.
-- ------------------------------------------------------------
create table if not exists public.label_deductions (
  user_id       uuid        not null,

  -- Mã vận đơn — đúng cái tab Đóng gói quét và cũng là tên file nhãn trên NAS.
  order_code    text        not null,

  sale_group_id uuid        not null,

  -- Tổng số CÁI đã trừ, để đối chiếu mà không phải cộng lại từ bảng sales.
  units_total   integer     not null,

  deducted_at   timestamptz not null default now(),

  constraint label_deductions_pkey primary key (user_id, order_code),
  constraint label_deductions_units_positive check (units_total > 0)
);

-- Tìm ngược từ nhóm bán về đơn, khi xoá một đơn bán và cần bỏ cả dấu đã trừ.
create index if not exists label_deductions_group_idx
  on public.label_deductions (user_id, sale_group_id);
