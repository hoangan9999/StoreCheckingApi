-- ============================================================
--  A saved sentence keeps the sentence that came before it.
--
--  Saving an answer on its own turns out to be nearly useless when reviewing weeks later:
--  "Yes, for about three years" says nothing without the question it answered. The line
--  before is what makes a saved sentence readable again.
--
--  Stored on the row rather than as a link to another saved sentence: the preceding line
--  usually was never saved and has no row to point at, and a conversation turn is not
--  worth a table of its own.
--
--  A separate file rather than an edit to 002: SchemaMigrator records a checksum for every
--  file it has applied and refuses to start if one changes underneath it.
-- ============================================================

-- Empty, not null, for every row already saved — there is no way to recover the context of
-- something saved before this column existed, and an empty string reads the same as "none".
alter table public.speaking_saved
  add column if not exists context text not null default '';
