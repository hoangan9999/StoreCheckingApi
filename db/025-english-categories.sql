-- ============================================================
--  Phân loại chủ đề tiếng Anh: "Công việc", "Du lịch", "Giao tiếp hàng ngày"…
--
--  Vấn đề có thật: bài học dán vào mỗi ngày một ít, tới lúc danh sách dài ra thì ô tìm
--  theo tên không cứu được nữa — phải nhớ bài đó tên gì mới tìm thấy. Phân loại là thứ
--  cho phép nhìn đúng một nhóm mà không phải nhớ gì cả.
--
--  ── Vì sao là BẢNG RIÊNG chứ không phải một cột text trên chủ đề ──
--
--  Đúng bài học đã rút ra ở `db/018` với `product_kinds`: **`id` là danh tính, `name`
--  chỉ là chữ hiện ra**. Để loại là một chuỗi nằm trên từng chủ đề thì:
--
--    * gõ lệch một chữ ("Công việc" / "Công Việc" / "Công việc ") là sinh loại mới mà
--      không có gì báo — y như `'Basic'` và `'Basic '` đã gặp thật trên dữ liệu kho;
--    * đổi tên một loại phải sửa từng chủ đề, mà sửa sót một cái thì nó rơi ra khỏi
--      nhóm và chỉ lộ ra lúc đi tìm.
--
--  Có bảng riêng thì đổi tên loại là UPDATE một dòng, mọi chủ đề theo liền vì chúng giữ
--  `category_id` chứ không giữ chữ.
--
--  ⚠️ **Chỉ còn ĐÚNG MỘT phép so chuỗi trong cả tính năng**: unique index dưới đây, và
--  nó chỉ chạy lúc TẠO để khỏi sinh hai "Công việc". Không có chỗ nào tra cứu loại bằng
--  tên. Thêm vào là mở lại đúng cái bẫy vừa bỏ.
--
--  ⚠️ **Một danh sách loại DÙNG CHUNG cho cả hai mục** (Bài học và Luyện dịch). Loại nói
--  về nội dung ("Du lịch"), không nói về kiểu bài; tách đôi thì cùng một loại phải tạo
--  hai lần rồi hai bên trôi khác nhau. Lọc thì vẫn lọc trong từng mục, vì danh sách chủ
--  đề vốn đã tách theo `kind`.
-- ============================================================
create table if not exists public.english_categories (
  id         uuid        not null default gen_random_uuid(),
  user_id    uuid        not null,

  -- Chữ hiện ra. KHÔNG phải danh tính — đừng bao giờ tra loại bằng cột này.
  name       text        not null,

  created_at timestamptz not null default now(),

  constraint english_categories_pkey primary key (id)
);

-- Chốt duy nhất còn so chuỗi, và chỉ chạy lúc tạo: hai "Công việc" trong cùng một danh
-- sách thì cái thứ hai là lỗi gõ, không phải loại mới. `btrim` + `lower` vì chính dữ
-- liệu kho đã từng có `'Basic'` lẫn `'Basic '` nằm cạnh nhau.
create unique index if not exists english_categories_user_name_idx
  on public.english_categories (user_id, lower(btrim(name)));

create index if not exists english_categories_user_created_idx
  on public.english_categories (user_id, created_at);

-- ------------------------------------------------------------
--  Chủ đề thuộc loại nào. `null` = chưa phân loại, và đó là trạng thái HỢP LỆ: mọi bài
--  đang có đều bắt đầu từ đó, và dán bài mới thì cũng chưa biết xếp vào đâu.
--
--  ⚠️ `on delete set null` — xoá một loại KHÔNG được xoá bài. Người ta xoá cái nhãn,
--  không xoá nội dung; `cascade` ở đây là một nút "xoá hết bài trong nhóm" nằm ngay cạnh
--  nút đổi tên, không có gì báo trước. Bài rơi về "chưa phân loại", thấy được, xếp lại
--  được.
-- ------------------------------------------------------------
alter table public.english_topics
  add column if not exists category_id uuid null;

alter table public.english_topics
  drop constraint if exists english_topics_category_fk;

alter table public.english_topics
  add constraint english_topics_category_fk
  foreign key (category_id) references public.english_categories(id) on delete set null;

create index if not exists english_topics_user_category_idx
  on public.english_topics (user_id, category_id);
