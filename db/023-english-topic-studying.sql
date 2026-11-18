-- ============================================================
--  Dấu "đang học dang dở" trên từng bài học tiếng Anh.
--
--  Vấn đề có thật: tắt app rồi mở lại thì không còn biết hôm qua đang học bài nào. Danh
--  sách chủ đề trông như nhau cả, và tên bài ("How's work going?") không nói gì về việc
--  đã học tới đâu.
--
--  Vì sao nằm ở database chứ không phải localStorage: cùng lý do với thứ tự ghi chú ở
--  db/017. Bài học hay được mở trên điện thoại lúc rảnh rồi mở tiếp trên PC — đánh dấu ở
--  máy này mà máy kia không thấy thì dấu đó vô dụng đúng lúc cần nhất. Và bản thân danh
--  sách bài học vốn đã phải gọi API mới có, nên để dấu ở đây không thêm ràng buộc nào.
--
--  Vì sao là cờ trên TỪNG bài chứ không phải một ô "bài đang học" duy nhất: người dùng
--  xin "một cái check box trên từng bài học", và học dở vài bài cùng lúc là chuyện bình
--  thường. Một ô duy nhất thì đánh dấu bài thứ hai là lặng lẽ bỏ dấu bài thứ nhất.
-- ============================================================
alter table public.english_topics
  add column if not exists studying boolean not null default false;
