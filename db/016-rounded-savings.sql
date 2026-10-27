-- ============================================================
--  Ô "làm tròn" cho phần còn lại mỗi tháng.
--
--  Số còn lại thật được tính ra từ thu nhập trừ chi tiêu, nên nó lẻ: 22.775.000. Khi nhìn
--  cả năm thì người dùng muốn tự chốt thành con số tròn (23.000.000) và lấy đó làm căn cứ
--  cộng dồn.
--
--  NULL chứ không phải 0, và đây là điểm quan trọng: NULL nghĩa là "chưa chốt, cứ lấy số
--  tính ra", còn 0 nghĩa là "tháng này chốt bằng không". Để mặc định 0 thì mọi tháng chưa
--  nhập sẽ bị tính thành 0 và tổng cả năm sai hoàn toàn.
--
--  Cho phép âm: tháng tiêu lố thì số còn lại âm, và người dùng vẫn có quyền làm tròn nó.
-- ============================================================
alter table public.monthly_income
  add column if not exists rounded_savings numeric(14,2);
