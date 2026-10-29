-- ============================================================
--  Thứ tự ghi chú do người dùng tự kéo thả (Marketing -> Ghi chú).
--
--  Trước đây danh sách xếp theo `updated_at desc`, nghĩa là sửa một ghi chú là nó nhảy
--  lên đầu. Với một danh sách để copy nhanh thì đó là điều ngược mong muốn: cái hay dùng
--  nhất phải nằm yên một chỗ, không phải chỗ nó vừa bị sửa.
--
--  `position` nhỏ hơn nằm trên. Ghi chú mới nhận `min(position) - 1` nên vẫn vào đầu
--  danh sách, giữ đúng thói quen cũ.
-- ============================================================
alter table public.notes
  add column if not exists position integer not null default 0;

-- Nạp thứ tự ban đầu bằng đúng thứ tự người dùng đang nhìn thấy, để lần đầu mở lên sau
-- khi deploy không thấy danh sách bị xáo.
--
-- Điều kiện `not exists (... position <> 0)` là chốt an toàn: chạy lại file này lần nữa
-- sau khi đã kéo thả sẽ không đạp lên sắp xếp của người dùng. SchemaMigrator vốn chỉ
-- chạy mỗi file một lần, nhưng một lệnh update không tự bảo vệ mình thì sớm muộn cũng có
-- người chạy tay.
update public.notes n
set    position = r.rn
from  (select id,
              row_number() over (partition by user_id order by updated_at desc, id desc) - 1 as rn
       from   public.notes) r
where  n.id = r.id
  and  not exists (select 1 from public.notes where position <> 0);

-- Liệt kê là "ghi chú của tôi, theo thứ tự" — đúng hai cột này.
create index if not exists notes_user_position_idx
  on public.notes (user_id, position);
