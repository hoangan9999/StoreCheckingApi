-- ============================================================
--  Cài đặt bật/tắt được ngay trên giao diện.
--
--  Trước đây "có tự đăng video lên Fanpage không" nằm ở biến môi trường, muốn đổi phải
--  sửa .env rồi dựng lại container. Đưa vào đây để một cái checkbox là xong.
--
--  Bảng khoá–giá trị chứ không phải mỗi cài đặt một cột: thêm một lựa chọn mới sẽ chỉ là
--  thêm một dòng, không phải một migration đổi cấu trúc bảng.
--
--  Giá trị để `text` chứ không phải `jsonb`: tất cả những gì lưu ở đây đều là một giá trị
--  đơn — bật/tắt, một con số, một chuỗi. jsonb sẽ chỉ thêm dấu nháy quanh chúng.
-- ============================================================
create table if not exists public.app_settings (
  user_id    uuid        not null,
  key        text        not null,
  value      text        not null,
  updated_at timestamptz not null default now(),
  primary key (user_id, key)
);
