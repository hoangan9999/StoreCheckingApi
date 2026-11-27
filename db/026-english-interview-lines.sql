-- ============================================================
--  Bài phỏng vấn kỹ thuật: thêm hai loại dòng 'level' và 'note'.
--
--  Nguồn là một định dạng dán vào KHÁC với bài hội thoại cũ, nhưng vẫn là bài hội thoại:
--
--      🎤 PHỎNG VẤN 1: C# Fundamentals & OOP
--
--      [Junior]
--      Interviewer: What's the difference between value types and reference types?
--      Value type và reference type khác nhau thế nào?
--      You: A value type holds its data directly...
--      Value type chứa trực tiếp dữ liệu...
--
--      💡 Giải thích:
--      ...giải thích tiếng Việt, có thể kèm ví dụ code...
--
--      [Middle] – câu hỏi đào sâu
--      ...
--
--      Cụm từ hay	Nghĩa
--      holds its data directly	chứa trực tiếp dữ liệu
--
--  Phần hỏi đáp và bảng cụm từ đã có sẵn chỗ: `kind='line'` với speaker teacher/you, và
--  `kind='phrase'`. Chỉ thiếu hai thứ:
--
--    * 'level' — nhãn mức của nhóm câu hỏi ngay sau nó ("Junior", "Middle", "Senior",
--      "Junior → Middle"). `en` giữ nhãn, `vi` giữ phần ghi chú đi kèm nếu có
--      ("câu hỏi đào sâu").
--    * 'note'  — khối 💡 Giải thích. Tiếng Việt thuần, nhiều dòng, có thể kèm code, nên
--      `vi` giữ nguyên văn cả khối và `en` RỖNG.
--
--  ⚠️ 'note' là loại dòng ĐẦU TIÊN được phép có `en` rỗng. Ràng buộc `en text not null`
--  vẫn giữ (rỗng khác null); phần chặn nằm ở tầng ứng dụng, nơi `BuildLines` đổi sang
--  đòi `vi` cho đúng loại này. Để nó đòi `en` thì cả khối giải thích phải nhét vào cột
--  tiếng Anh — lúc đó nút "Ẩn tiếng Việt" không ẩn được nó, và phần đọc thành tiếng sẽ
--  đọc tiếng Việt bằng giọng en-US.
--
--  ⚠️ Phải nới CẢ HAI ràng buộc, y như db/024 đã phải làm với 'para'.
--  `..._speaker_fits_kind` viết bằng `case kind when ... end`: thêm loại mới mà không
--  thêm nhánh thì CASE trả NULL, mà **CHECK gặp NULL là coi như đạt** — lúc đó một dòng
--  'note' mang speaker='teacher' lọt qua và giao diện vẽ nó thành câu thoại.
-- ============================================================
alter table public.english_lesson_lines
  drop constraint if exists english_lesson_lines_kind_known;

alter table public.english_lesson_lines
  add constraint english_lesson_lines_kind_known
  check (kind in ('line', 'phrase', 'para', 'level', 'note'));

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
    end
  );
