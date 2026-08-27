-- ============================================================
--  Marketing (Marketing tab): groups to post into, posts, what was posted where, and the
--  queue the Vercel cron job works through.
--
--  Ported from supabase/migration-marketing.sql + migration-marketing-image.sql +
--  migration-marketing-images.sql + migration-post-queue.sql +
--  migration-post-queue-images.sql, DROPPING auth.uid(), the foreign keys to auth.users,
--  and RLS.
--
--  NOT ported: the storage.buckets / storage.objects policies from
--  migration-marketing-image.sql. Supabase Storage has no counterpart here — images go to
--  the NAS through nas.service.ts, and the owner is re-uploading them by hand.
--
--  Still unsolved when this module switches over: api/cron-post.js on Vercel reads and
--  writes post_queue with SUPABASE_SERVICE_ROLE_KEY, machine to machine, with no user
--  logged in. The .NET API only accepts Supabase user tokens, so that cron needs a
--  service credential of its own BEFORE post_queue leaves Supabase.
-- ============================================================
create extension if not exists "pgcrypto";

-- ---------- Groups posted into ----------
create table if not exists public.marketing_groups (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  name       text not null,
  platform   text not null default 'facebook',   -- facebook | zalo | other
  url        text,
  created_at timestamptz not null default now()
);

-- ---------- Posts ----------
create table if not exists public.marketing_posts (
  id           uuid primary key default gen_random_uuid(),
  user_id      uuid not null,
  content      text not null,
  scheduled_at timestamptz,
  -- image_url came first and holds a single image; images superseded it and holds the
  -- whole list. Both are kept because existing rows still carry the older column.
  image_url    text,
  images       jsonb not null default '[]'::jsonb,
  created_at   timestamptz not null default now()
);

-- ---------- Which post went into which group ----------
create table if not exists public.marketing_post_targets (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  post_id    uuid not null references public.marketing_posts(id) on delete cascade,
  group_id   uuid not null references public.marketing_groups(id) on delete cascade,
  posted     boolean not null default true,
  posted_at  timestamptz not null default now(),
  unique (post_id, group_id)
);

-- ---------- Queue the cron job posts from ----------
create table if not exists public.post_queue (
  id         uuid primary key default gen_random_uuid(),
  user_id    uuid not null,
  name       text not null,
  price      numeric not null default 0,
  -- Holds a Supabase Storage public URL on the rows copied over. Every one of them has to
  -- be rewritten to its NAS address once the images are re-uploaded.
  image_url  text not null,
  images     jsonb not null default '[]'::jsonb,
  note       text,                     -- extra hint for the AI (e.g. "bản chase, hiếm")
  status     text not null default 'pending',   -- pending | posted | error
  fb_post_id text,                     -- Facebook post id once it went out
  error      text,                     -- why the last attempt failed
  posted_at  timestamptz,
  created_at timestamptz not null default now()
);

create index if not exists post_queue_pending_idx on public.post_queue (status, created_at);
create index if not exists marketing_posts_user_created_idx on public.marketing_posts (user_id, created_at desc);
create index if not exists marketing_post_targets_post_idx on public.marketing_post_targets (post_id);
