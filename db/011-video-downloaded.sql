-- ============================================================
--  Đánh dấu video đã được tải về.
--
--  Video dựng ra để đăng TikTok, mỗi ngày năm cái. Không có dấu này thì sau vài ngày
--  không còn biết cái nào đã lấy, cái nào chưa — và thứ tự trong danh sách không giúp
--  được gì vì video hỏng cũng nằm lẫn vào đó.
--
--  Ghi thời điểm chứ không phải cờ đúng/sai: biết THÊM được là tải lúc nào, mà vẫn trả
--  lời được câu hỏi "đã tải chưa" y hệt.
-- ============================================================
alter table public.generated_videos
  add column if not exists downloaded_at timestamptz;
