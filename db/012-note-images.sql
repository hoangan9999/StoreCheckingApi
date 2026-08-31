-- ============================================================
--  Ghi chú đính kèm được nhiều ảnh.
--
--  Chỉ lưu TÊN FILE, còn ảnh nằm trên đĩa trong volume `media`, thư mục `notes` — tách
--  hẳn khỏi `images` là kho ảnh cho video tự sinh. Để chung thì ảnh chụp màn hình, ảnh
--  hoá đơn, ảnh mẫu tin nhắn sẽ lọt vào video bán xe.
--
--  Mảng text chứ không phải bảng riêng: ảnh của một ghi chú luôn được đọc trọn gói cùng
--  ghi chú đó, không bao giờ truy vấn riêng lẻ, nên một bảng nối chỉ thêm việc.
-- ============================================================
alter table public.notes
  add column if not exists images text[] not null default '{}';
