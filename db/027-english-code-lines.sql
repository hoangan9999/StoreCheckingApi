-- ============================================================
--  Bài phỏng vấn kỹ thuật, bản format thứ hai: tách ĐOẠN CODE ra thành dòng riêng.
--
--  Khác với bản ghi ở db/026 đúng hai chỗ:
--
--    1. Đoạn code không còn nằm trong khối giải thích nữa mà đứng RIÊNG, ngay trước nó:
--
--           You: Boxing happens when a value type is converted to object.
--           Boxing xảy ra khi value type được chuyển thành object.
--
--           csharp
--           int x = 42;
--           object o = x;      // boxing: allocates on the heap
--
--           💡 Explanation: Every time a value type is boxed, ...
--           Giải thích: Mỗi lần boxing, ...
--
--    2. Khối giải thích giờ là một CẶP Anh–Việt, nên dòng 'note' dùng cả `en` lẫn `vi`
--       thay vì chỉ `vi` như trước. Không cần đổi gì trong database cho chuyện này —
--       hai cột vốn đã có sẵn; chỉ tầng ứng dụng đổi phép kiểm từ "phải có `vi`" thành
--       "phải có ít nhất một trong hai".
--
--  Nên migration này chỉ thêm đúng một loại dòng: 'code'.
--
--  ⚠️ `en` giữ NGUYÊN VĂN đoạn code, `vi` giữ TÊN NGÔN NGỮ ("csharp") — đây là chỗ duy
--  nhất trong cả bảng mà cột `vi` không mang tiếng Việt. Để tên ngôn ngữ lẫn vào trong
--  `en` thì dòng "csharp" hiện ra như một dòng code lạc lõng ở đầu khối, mà bỏ hẳn nó đi
--  thì mất thông tin tô màu cú pháp sau này.
--
--  ⚠️ Lại phải nới CẢ HAI ràng buộc, lần thứ ba liên tiếp (db/024 với 'para', db/026 với
--  'level'/'note'). `..._speaker_fits_kind` viết bằng `case kind when ... end`: thiếu
--  nhánh thì CASE trả NULL, mà **CHECK gặp NULL là coi như đạt**.
-- ============================================================
alter table public.english_lesson_lines
  drop constraint if exists english_lesson_lines_kind_known;

alter table public.english_lesson_lines
  add constraint english_lesson_lines_kind_known
  check (kind in ('line', 'phrase', 'para', 'level', 'note', 'code'));

alter table public.english_lesson_lines
  drop constraint if exists english_lesson_lines_speaker_fits_kind;

alter table public.english_lesson_lines
  add constraint english_lesson_lines_speaker_fits_kind
  check (
    case kind
      when 'line'   then speaker in ('teacher', 'you')
      when 'phrase' then speaker = ''
      when 'para'   then speaker = ''
      when 'level'  then speaker = ''
      when 'note'   then speaker = ''
      when 'code'   then speaker = ''
    end
  );
