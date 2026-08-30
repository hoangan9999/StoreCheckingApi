-- ============================================================
--  Kho ảnh và kho video tự sinh (tab Tiện ích).
--
--  Mỗi ngày một tiến trình nền chọn 10-15 ảnh từ kho, nhờ AI nhìn ảnh nhận ra là xe gì
--  rồi viết kịch bản, cho giọng Adam đọc, và ghép thành video dọc 9:16 để đăng TikTok.
--
--  Chỉ lưu thông tin, KHÔNG lưu file. Ảnh và video nằm trên đĩa trong một named volume
--  của Docker; cột `filename` là sợi dây nối hai bên — cùng cách packing_videos đang làm.
--  Lý do: dữ liệu nhị phân trong Postgres làm bản sao lưu phình từ vài trăm KB lên hàng
--  GB, mà bản sao lưu hằng ngày lại đang được đẩy qua mạng sang NAS.
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Kho ảnh ----------
create table if not exists public.media_images (
  -- Không có `default auth.uid()` và không khoá ngoại sang auth.users: ở đây không có cái
  -- nào cả. Giá trị luôn lấy từ claim `sub` của token, do API đặt.
  id            uuid primary key default gen_random_uuid(),
  user_id       uuid not null,

  filename      text not null,          -- tên file thật trên đĩa
  original_name text not null default '',  -- tên lúc người dùng tải lên, chỉ để hiển thị
  content_type  text not null default 'image/jpeg',
  bytes         bigint not null default 0,
  width         integer,
  height        integer,

  -- Đếm số lần ảnh đã được dùng trong một video. Bộ chọn ưu tiên ảnh ÍT dùng nhất, nên
  -- 5 video trong ngày không đụng nhau và ảnh cũ không bị bỏ quên mãi.
  use_count     integer not null default 0,
  last_used_at  timestamptz,

  uploaded_at   timestamptz not null default now()
);

-- Kho ảnh xem theo ngày tải lên.
create index if not exists idx_media_images_time
  on public.media_images (user_id, uploaded_at desc);

-- Bộ chọn hằng ngày sắp theo đúng thứ tự này: ít dùng nhất trước, lâu chưa dùng nhất trước.
create index if not exists idx_media_images_pick
  on public.media_images (user_id, use_count, last_used_at nulls first);

-- Cùng một file không được ghi hai dòng.
create unique index if not exists idx_media_images_file
  on public.media_images (user_id, filename);

-- ---------- Kho video tự sinh ----------
create table if not exists public.generated_videos (
  id           uuid primary key default gen_random_uuid(),
  user_id      uuid not null,

  filename     text,                    -- null cho tới khi ghép xong
  title        text not null default '',
  script       text not null default '',  -- lời AI viết, cũng là lời giọng Adam đọc
  duration_sec numeric(6,2),
  bytes        bigint,

  -- pending -> writing -> voicing -> rendering -> ready | error
  -- Ghi rõ từng chặng để khi hỏng còn biết hỏng ở đâu: AI viết lỗi, giọng đọc lỗi, hay
  -- ffmpeg lỗi — ba nguyên nhân rất khác nhau và cách xử cũng khác.
  status       text not null default 'pending',
  error        text,

  -- Ảnh đã dùng, giữ lại để xem lại video nào ghép từ ảnh nào.
  image_ids    uuid[] not null default '{}',

  -- Ngày của mẻ video, để đếm "hôm nay đã đủ 5 chưa" mà không phụ thuộc múi giờ của máy chủ.
  batch_day    date not null default (now() at time zone 'Asia/Ho_Chi_Minh')::date,

  created_at   timestamptz not null default now(),
  finished_at  timestamptz
);

create index if not exists idx_generated_videos_day
  on public.generated_videos (user_id, batch_day desc, created_at desc);

create index if not exists idx_generated_videos_status
  on public.generated_videos (user_id, status);
