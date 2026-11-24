-- ============================================================
--  Bài luyện DỊCH: một đoạn tiếng Việt, bảng từ gợi ý, và bản dịch tiếng Anh.
--
--  Khác bài hội thoại ở chỗ dùng: đọc đoạn tiếng Việt, tự dịch trong đầu, rồi mở bản
--  tiếng Anh ra đối chiếu. Nội dung do AI sinh ra (Gemini qua hàm serverless trên
--  Vercel) khi người dùng bấm nút, mỗi lần hai đề.
--
--  ── Vì sao DÙNG LẠI hai bảng cũ chứ không dựng bảng mới ──
--
--  Vì hình dạng thật sự giống nhau, không phải ghép gượng: cả hai đều là một chủ đề có
--  tên, chứa một danh sách cặp (tiếng Anh, tiếng Việt) có thứ tự. Bảng từ gợi ý CHÍNH LÀ
--  `kind='phrase'` đang có — không khác một chữ nào. Chỉ thiếu một loại dòng cho đoạn
--  văn, và một chỗ để biết chủ đề này thuộc mục nào.
--
--  Dựng bảng riêng là nhân đôi mọi thứ đang chạy tốt: đường lưu, đường xoá, cách ly theo
--  người dùng, cờ `studying`, và cả phần đọc thành tiếng. Đổi lấy đúng một cột.
--
--  ⚠️ `kind` trên chủ đề là thứ CHIA HAI DANH SÁCH. Thiếu nó thì bài dịch trôi vào mục
--  Bài học và ngược lại — hai mục vẽ nội dung theo hai cách khác hẳn nhau, nên lẫn là
--  hiện ra sai chứ không phải hiện ra xấu.
-- ============================================================
alter table public.english_topics
  add column if not exists kind text not null default 'lesson';

alter table public.english_topics
  drop constraint if exists english_topics_kind_known;

alter table public.english_topics
  add constraint english_topics_kind_known
  check (kind in ('lesson', 'translate'));

-- Liệt kê luôn là "chủ đề của tôi THUỘC MỤC NÀY, theo thứ tự" — đúng ba cột này.
create index if not exists english_topics_user_kind_pos_idx
  on public.english_topics (user_id, kind, position, created_at desc);

-- ------------------------------------------------------------
--  Loại dòng mới: 'para' — một đoạn văn, tiếng Việt ở cột `vi`, bản dịch ở cột `en`.
--
--  ⚠️ Phải nới CẢ HAI ràng buộc, không phải một. `..._speaker_fits_kind` viết bằng
--  `case kind when 'line' … when 'phrase' … end`: thêm 'para' mà không thêm nhánh thì
--  CASE trả NULL, mà CHECK gặp NULL là **coi như đạt**. Lúc đó một dòng 'para' mang
--  speaker='teacher' lọt qua, và giao diện sẽ vẽ nó thành câu thoại.
-- ------------------------------------------------------------
alter table public.english_lesson_lines
  drop constraint if exists english_lesson_lines_kind_known;

alter table public.english_lesson_lines
  add constraint english_lesson_lines_kind_known
  check (kind in ('line', 'phrase', 'para'));

alter table public.english_lesson_lines
  drop constraint if exists english_lesson_lines_speaker_fits_kind;

alter table public.english_lesson_lines
  add constraint english_lesson_lines_speaker_fits_kind
  check (
    case kind
      when 'line'   then speaker in ('teacher', 'you')
      when 'phrase' then speaker = ''
      when 'para'   then speaker = ''
    end
  );
