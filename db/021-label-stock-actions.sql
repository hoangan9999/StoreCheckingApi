-- ============================================================
--  Trừ tồn theo nhãn: bỏ ghép tự động, chuyển sang người quyết định từng nhãn.
--
--  `db/019` dựng trên giả định một dòng chữ trên nhãn có thể suy ra mặt hàng trong kho.
--  Giả định đó SAI, và đây là lý do: nhãn chỉ in **tên + số lượng**, sản phẩm trong lô
--  cũng chỉ có **tên + số lượng**. Hai đầu không có mã nào chung, nên không có cơ sở nào
--  để máy khẳng định "Protect Basic" trên nhãn là dòng sản phẩm nào trong lô nào. Ghép
--  đúng được vài lần đầu là do may, không phải do biết.
--
--  Nên từ nay: **không có gì tự trừ.** Tải nhãn lên chỉ còn làm một việc là gắn nhãn vào
--  video đơn đóng. Trừ tồn là một thao tác riêng, người dùng mở từ tab Lô hàng, nhìn từng
--  nhãn, tự chọn sản phẩm rồi bấm.
--
--  Bảng này vì vậy đổi nghĩa: không còn là "các đơn đã trừ" mà là **"các nhãn đã xử lý"**
--  — đã trừ, hoặc đã cố ý bỏ qua. Đổi tên theo cho khớp: một bảng tên `label_deductions`
--  mà quá nửa số dòng là "không trừ gì cả" chính là kiểu đặt tên đánh lừa người đọc sau.
-- ============================================================

-- ------------------------------------------------------------
--  Bỏ dấu chốt hàng loạt của `db/020`.
--
--  `db/020` đánh dấu MỌI đơn từng đóng gói là "đã xong", để bản tự trừ khỏi trừ đè lên số
--  đã nhập tay. Lý lẽ đó đúng với một hệ thống TỰ trừ. Nay người dùng tự quyết định từng
--  nhãn, nên chốt sẵn hộ là lấy mất đúng cái quyền vừa trao: đơn cũ không bao giờ hiện ra
--  để mà nhìn. Trả chúng về danh sách chưa xử lý — đơn nào đã nhập tay rồi thì bấm Bỏ qua,
--  mất một giây và nhìn thấy được.
--
--  Nhận diện chính xác dòng của `db/020`: nó ghi `sale_group_id` rỗng kèm `units_total`
--  bằng 0, mà ràng buộc `label_deductions_units_sane` bảo đảm hai thứ đó luôn đi cùng
--  nhau. Mọi dòng do app thật sự trừ đều có nhóm bán và số cái lớn hơn 0, nên không dòng
--  nào trong số đó bị đụng tới.
--
--  Phải xoá TRƯỚC khi thêm cột `status`: cột mới mặc định là 'deducted', mà một dòng
--  'deducted' không có nhóm bán sẽ vi phạm ràng buộc hình dạng thêm ở cuối file.
-- ------------------------------------------------------------
--  Tự canh để chạy lại được: sau lần đầu thì bảng đã mang tên mới, và một câu `delete`
--  trỏ vào tên cũ sẽ đổ ngay ở lần dựng lại database thứ hai.
do $$
begin
  if to_regclass('public.label_deductions') is not null then
    execute 'delete from public.label_deductions
             where sale_group_id is null and units_total = 0';
  end if;
end $$;

-- ------------------------------------------------------------
--  Đổi tên bảng, khoá, chỉ mục và cột thời điểm.
--
--  Mọi câu đều tự canh để chạy lại được. `SchemaMigrator` ghi checksum nên thực tế không
--  chạy lại, nhưng một file schema chỉ đúng đúng một lần là cái bẫy để dành cho lần dựng
--  lại database từ đầu.
-- ------------------------------------------------------------
do $$
begin
  if to_regclass('public.label_deductions') is not null
     and to_regclass('public.label_stock_actions') is null then
    execute 'alter table public.label_deductions rename to label_stock_actions';
  end if;

  if exists (select 1 from pg_constraint where conname = 'label_deductions_pkey') then
    execute 'alter table public.label_stock_actions
               rename constraint label_deductions_pkey to label_stock_actions_pkey';
  end if;

  -- `deducted_at` nay đóng dấu cả lần bỏ qua, nên tên cũ nói sai một nửa số dòng.
  if exists (
    select 1 from information_schema.columns
    where table_schema = 'public' and table_name = 'label_stock_actions'
      and column_name = 'deducted_at'
  ) then
    execute 'alter table public.label_stock_actions rename column deducted_at to acted_at';
  end if;
end $$;

alter index if exists label_deductions_group_idx rename to label_stock_actions_group_idx;

-- ------------------------------------------------------------
--  Đã trừ, hay đã cố ý bỏ qua.
--
--  Không có hàng nào nghĩa là "chưa xử lý" — đó mới là thứ hiện trong danh sách chờ. Ba
--  trạng thái mà chỉ lưu hai, vì trạng thái thứ ba là sự vắng mặt: nhãn mới tải lên không
--  phải đi ghi thêm dòng nào, và danh sách chờ không bao giờ lệch với thực tế.
-- ------------------------------------------------------------
alter table public.label_stock_actions
  add column if not exists status text not null default 'deducted';

alter table public.label_stock_actions
  drop constraint if exists label_stock_actions_status_known;
alter table public.label_stock_actions
  add constraint label_stock_actions_status_known
  check (status in ('deducted', 'skipped'));

-- ------------------------------------------------------------
--  Hình dạng dòng phải khớp trạng thái.
--
--  Đã trừ thì BẮT BUỘC có nhóm bán — đó là thứ duy nhất tìm ngược được "đơn này sinh ra
--  những dòng bán nào", mà không có nó thì nút hoàn lại không thể biết phải xoá gì. Bỏ
--  qua thì ngược lại: không sinh dòng bán nào, nên nhóm rỗng và số cái bằng 0 mới là con
--  số thật.
-- ------------------------------------------------------------
alter table public.label_stock_actions drop constraint if exists label_deductions_units_positive;
alter table public.label_stock_actions drop constraint if exists label_deductions_units_sane;
alter table public.label_stock_actions drop constraint if exists label_stock_actions_shape;
alter table public.label_stock_actions
  add constraint label_stock_actions_shape check (
    case status
      when 'deducted' then sale_group_id is not null and units_total > 0
      when 'skipped'  then sale_group_id is null     and units_total = 0
    end
  );

-- Bảng lịch sử chỉ lấy 7 ngày gần nhất, và luôn sắp mới trước.
create index if not exists label_stock_actions_recent_idx
  on public.label_stock_actions (user_id, acted_at desc);
