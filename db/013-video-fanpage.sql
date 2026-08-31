-- ============================================================
--  Video dựng xong thì đăng thẳng lên Fanpage.
--
--  Ba cột chứ không phải một cờ true/false:
--   - `posted_at`   đăng lúc nào (null = chưa đăng)
--   - `fb_post_id`  id bài trên Facebook, để mở thẳng bài đó ra xem
--   - `post_error`  vì sao đăng hỏng, giữ nguyên lời Facebook nói
--
--  Đăng hỏng KHÔNG được tính là video hỏng: file đã dựng xong và vẫn tải về đăng tay
--  được. Nên lỗi đăng nằm ở cột riêng, `status` vẫn là `ready`.
-- ============================================================
alter table public.generated_videos
  add column if not exists posted_at  timestamptz,
  add column if not exists fb_post_id text,
  add column if not exists post_error text;

-- Tìm "video nào chưa đăng" — cột thưa nên chỉ đánh chỉ mục phần chưa đăng.
create index if not exists generated_videos_unposted_idx
  on public.generated_videos (user_id, created_at desc)
  where posted_at is null;
