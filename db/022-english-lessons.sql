-- ============================================================
--  Bài học tiếng Anh theo chủ đề.
--
--  Mỗi chủ đề là một đoạn hội thoại Teacher/You, câu tiếng Anh kèm câu tiếng Việt, và
--  cuối bài là bảng "Cụm từ hay". Nội dung do AI sinh ra ở nơi khác rồi dán vào app, nên
--  thứ cần lưu là KẾT QUẢ đã tách sẵn hai cột — không phải nguyên khối chữ.
--
--  Tách hai cột mới là chỗ có giá trị: nhờ đó mới ẩn được phần tiếng Việt để tự kiểm tra,
--  mà đó chính là thứ biến một đống chữ thành bài luyện thật. Lưu nguyên khối thì app chỉ
--  là chỗ chứa file text.
-- ============================================================

create table if not exists public.english_topics (
  id         uuid        not null default gen_random_uuid(),
  user_id    uuid        not null,

  title      text        not null,

  -- Thứ tự người dùng tự sắp. Chủ đề mới nhận min(position) - 1 nên vào đầu danh sách,
  -- giống hệt cách `notes` làm ở db/017.
  position   integer     not null default 0,

  created_at timestamptz not null default now(),
  updated_at timestamptz not null default now(),

  constraint english_topics_pkey primary key (id)
);

create index if not exists english_topics_user_pos_idx
  on public.english_topics (user_id, position, created_at desc);

-- ------------------------------------------------------------
--  Một dòng trong bài — câu hội thoại HOẶC một cụm từ hay.
--
--  Một bảng chứ không phải hai, vì hai thứ đó cùng hình dạng thật chứ không phải ghép
--  gượng: cả hai đều là một cặp (tiếng Anh, tiếng Việt) có thứ tự trong bài. Khác nhau
--  đúng ở chỗ câu hội thoại có người nói. Tách đôi ra là nhân đôi mọi thứ — hai bảng,
--  hai đường sửa, hai đường sắp thứ tự — để đổi lấy một cột.
--
--  `on delete cascade`: xoá chủ đề thì mọi dòng đi theo. Không có dòng nào sống sót
--  ngoài một chủ đề, nên để chúng mồ côi là chỉ tạo rác không ai nhìn thấy.
-- ------------------------------------------------------------
create table if not exists public.english_lesson_lines (
  id       uuid    not null default gen_random_uuid(),
  user_id  uuid    not null,
  topic_id uuid    not null references public.english_topics(id) on delete cascade,

  -- 'line' = câu hội thoại, 'phrase' = một dòng trong bảng Cụm từ hay.
  kind     text    not null,

  -- 'teacher' hoặc 'you'. Rỗng với dòng cụm từ — cụm từ không có ai nói cả.
  speaker  text    not null default '',

  en       text    not null,
  vi       text    not null default '',

  position integer not null,

  constraint english_lesson_lines_pkey primary key (id),

  constraint english_lesson_lines_kind_known
    check (kind in ('line', 'phrase')),

  -- Người nói chỉ có nghĩa với câu hội thoại. Không chốt thì một dòng cụm từ mang
  -- speaker='teacher' trông vẫn hợp lệ, và giao diện sẽ vẽ nó thành câu thoại.
  constraint english_lesson_lines_speaker_fits_kind
    check (
      case kind
        when 'line'   then speaker in ('teacher', 'you')
        when 'phrase' then speaker = ''
      end
    )
);

create index if not exists english_lesson_lines_topic_idx
  on public.english_lesson_lines (topic_id, position);

create index if not exists english_lesson_lines_user_idx
  on public.english_lesson_lines (user_id);
