-- ============================================================
--  Bài viết tự sinh cho Fanpage — song song với video, cùng khung giờ.
--
--  Mỗi bài MỘT ảnh (khác video là 10-15 ảnh). Nội dung do AI nhìn ảnh mà viết.
--
--  Cả mẻ 5 bài được viết trong MỘT lượt gọi Gemini rồi để dành, mỗi khung giờ đăng một
--  bài. Hạn mức Gemini bị chặn ở SỐ LƯỢT GỌI (20/ngày) chứ không phải dung lượng — gọi
--  riêng từng bài sẽ ngốn 5 lượt cho việc mà một lượt làm được.
--
--  `posted_at`/`fb_post_id`/`post_error` giống hệt bảng video, và cũng vì cùng một lý do:
--  đăng hỏng không phải bài hỏng, nội dung vẫn còn đó để đăng lại.
-- ============================================================
create table if not exists public.generated_posts (
  id          uuid        primary key default gen_random_uuid(),
  user_id     uuid        not null,

  -- Ảnh dùng cho bài. Không khoá ngoại cứng: ảnh bị xoá khỏi kho thì bài đã đăng vẫn nên
  -- còn lại như một mẩu lịch sử, chứ không biến mất theo.
  image_id    uuid        not null,

  title       text        not null default '',
  content     text        not null default '',

  status      text        not null default 'pending',   -- pending | ready | error
  error       text,

  batch_day   date        not null,
  created_at  timestamptz not null default now(),

  posted_at   timestamptz,
  fb_post_id  text,
  post_error  text
);

-- "Hôm nay đã có mẻ chưa" và "bài nào chưa đăng" — hai câu hỏi duy nhất bảng này phải trả.
create index if not exists generated_posts_day_idx
  on public.generated_posts (user_id, batch_day desc, created_at);

create index if not exists generated_posts_unposted_idx
  on public.generated_posts (user_id, created_at)
  where posted_at is null;
