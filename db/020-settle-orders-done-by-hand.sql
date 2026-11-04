-- ============================================================
--  Chốt mọi đơn đã có trước khi app biết tự trừ tồn.
--
--  Toàn bộ số tồn của những đơn đó đã được nhập TAY rồi. Không có file này thì cứ tải lại
--  một nhãn cũ — việc vẫn làm thường xuyên để in lại — là đơn đó `packed = true`, app trừ
--  thêm một lần nữa, **đè lên con số đã nhập tay và không có gì báo**. Chỉ lộ ra khi kiểm
--  kho lệch, mà lúc đó không truy lại được đơn nào gây ra.
--
--  Cách chốt là dùng đúng cơ chế chống trừ hai lần đã có: ghi một dòng `label_deductions`
--  cho từng đơn. Từ đó khoá chính `(user_id, order_code)` tự lo phần còn lại, mãi mãi.
--
--  ⚠️ Khối nạp dưới đây chạy **đúng một lần**, và đó là điều kiện để nó đúng:
--  `SchemaMigrator` ghi tên file kèm checksum vào `schema_history` nên không chạy lại lần
--  nào nữa. Nếu nó chạy lại sau này thì mọi đơn đóng gói MỚI cũng bị đánh dấu là đã chốt,
--  tức là không bao giờ được trừ tồn. Vì vậy **đừng bao giờ sửa file này** — sửa là
--  checksum lệch và API từ chối khởi động (đúng như thiết kế), còn xoá dòng của nó trong
--  `schema_history` thì sẽ chạy lại và gây đúng cái hỏng vừa nói.
-- ============================================================

-- ------------------------------------------------------------
--  `sale_group_id` cho phép rỗng: NULL = "đã chốt ngoài app".
--
--  Đường tự động thì LUÔN có nhóm, kể cả đơn một dòng, để còn trả lời được "đơn này sinh
--  ra những dòng bán nào". Còn mấy đơn nhập tay thì không có dòng bán nào do app ghi, nên
--  gán một nhóm bịa ra sẽ là một id trỏ vào chỗ không có gì — thà để trống và nói đúng.
--
--  `units_total` cũng vậy: 0 là con số thật cho một đơn app không trừ gì cả.
-- ------------------------------------------------------------
alter table public.label_deductions
  alter column sale_group_id drop not null;

alter table public.label_deductions
  drop constraint if exists label_deductions_units_positive;

-- Bỏ cả ràng buộc mới trước khi thêm: `add constraint` không có `if not exists`, nên thiếu
-- dòng này là chạy lại lần hai sẽ đổ. `schema_history` lo việc không chạy lại, nhưng một
-- file schema mà chỉ chạy đúng được một lần là cái bẫy để dành cho lần dựng lại database.
alter table public.label_deductions
  drop constraint if exists label_deductions_units_sane;

alter table public.label_deductions
  add constraint label_deductions_units_sane check (
    case when sale_group_id is null then units_total = 0 else units_total > 0 end
  );

-- ------------------------------------------------------------
--  Nạp: mọi đơn đã đóng gói tính tới lúc này đều coi như đã chốt.
--
--  Lấy theo `packing_videos` chứ không theo nhãn đã tải lên: cái cần chặn là "đơn này đã
--  vào sổ bằng tay rồi", mà bằng chứng cho việc đó là đơn đã đóng gói — nhãn có thể tải
--  lên sau, hoặc chưa tải bao giờ.
-- ------------------------------------------------------------
-- `null::uuid`, không phải `null` trần: Postgres suy một `null` không ép kiểu trong
-- `select` thành `text` và từ chối ghi vào cột `uuid`. Lần đầu viết thiếu chỗ này,
-- migration đổ, schema không dựng xong và 160 contract test đỏ cùng lúc — tức là API cũng
-- sẽ không khởi động nổi.
insert into public.label_deductions (user_id, order_code, sale_group_id, units_total)
select distinct p.user_id, p.order_code, null::uuid, 0
from public.packing_videos p
where p.order_code is not null and btrim(p.order_code) <> ''
on conflict (user_id, order_code) do nothing;
