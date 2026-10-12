-- AbhyasAR: worker accounts, device sessions, training progress, admin dashboard access.
-- The Unity app only ever calls worker_login / submit_attempts with the anon key.
-- The dashboard signs in with Supabase Auth; the admin_* functions check the admins table.

create extension if not exists pgcrypto with schema extensions;

-- ---------------------------------------------------------------- tables

create table public.admins (
    user_id uuid primary key references auth.users (id) on delete cascade
);

create table public.workers (
    id           uuid primary key default gen_random_uuid(),
    worker_code  text not null unique,
    display_name text not null,
    pass_hash    text not null,
    active       boolean not null default true,
    created_at   timestamptz not null default now()
);

create table public.worker_sessions (
    token      text primary key,
    worker_id  uuid not null references public.workers (id) on delete cascade,
    created_at timestamptz not null default now(),
    expires_at timestamptz not null
);
create index worker_sessions_worker_idx on public.worker_sessions (worker_id);

create table public.training_attempts (
    id              uuid primary key,              -- generated on the device => idempotent uploads
    worker_id       uuid not null references public.workers (id) on delete cascade,
    module          text not null,
    scenario        text,
    passed          boolean not null,
    score           integer,
    elapsed_seconds real,
    details         jsonb,
    completed_at    timestamptz not null,
    synced_at       timestamptz not null default now()
);
create index training_attempts_worker_idx on public.training_attempts (worker_id, completed_at desc);

alter table public.admins            enable row level security;
alter table public.workers           enable row level security;
alter table public.worker_sessions   enable row level security;
alter table public.training_attempts enable row level security;

revoke all on public.admins, public.workers, public.worker_sessions, public.training_attempts from anon, authenticated;

-- ---------------------------------------------------------------- helpers

create or replace function public.is_admin()
returns boolean
language sql stable security definer
set search_path = public
as $$
    select exists (select 1 from public.admins where user_id = auth.uid());
$$;

create or replace function public._require_admin()
returns void
language plpgsql stable security definer
set search_path = public
as $$
begin
    if not public.is_admin() then
        raise exception 'not_admin' using errcode = '42501';
    end if;
end;
$$;

-- Unambiguous alphabet (no 0/O, 1/I/L) so printed passwords are easy to type on a phone.
create or replace function public._random_password(p_len int default 8)
returns text
language plpgsql volatile
set search_path = public, extensions
as $$
declare
    alphabet constant text := 'ABCDEFGHJKMNPQRSTUVWXYZ23456789';
    bytes bytea := gen_random_bytes(p_len);
    result text := '';
begin
    for i in 0 .. p_len - 1 loop
        result := result || substr(alphabet, (get_byte(bytes, i) % length(alphabet)) + 1, 1);
    end loop;
    return result;
end;
$$;

create or replace function public._new_worker_code()
returns text
language plpgsql volatile
set search_path = public
as $$
declare
    candidate text;
begin
    loop
        candidate := 'W-' || lpad((floor(random() * 100000))::int::text, 5, '0');
        exit when not exists (select 1 from public.workers where worker_code = candidate);
    end loop;
    return candidate;
end;
$$;

-- ---------------------------------------------------------------- app (anon) API

create or replace function public.worker_login(p_code text, p_password text)
returns jsonb
language plpgsql volatile security definer
set search_path = public, extensions
as $$
declare
    w public.workers;
    v_token text;
    v_expires timestamptz := now() + interval '90 days';
begin
    select * into w from public.workers where worker_code = upper(trim(p_code));

    if w.id is null or not w.active or w.pass_hash <> crypt(p_password, w.pass_hash) then
        raise exception 'invalid_credentials' using errcode = 'P0001';
    end if;

    v_token := encode(gen_random_bytes(32), 'hex');
    insert into public.worker_sessions (token, worker_id, expires_at) values (v_token, w.id, v_expires);
    delete from public.worker_sessions where worker_id = w.id and expires_at < now();

    return jsonb_build_object(
        'worker_id', w.id,
        'worker_code', w.worker_code,
        'display_name', w.display_name,
        'token', v_token,
        'expires_at', v_expires
    );
end;
$$;

-- p_attempts: [{id, module, scenario, passed, score, elapsed_seconds, details_json, completed_at}, ...]
-- Returns {"accepted": [ids]} — every submitted id that is now stored for this worker,
-- including ones already uploaded earlier, so the device can mark them all synced.
create or replace function public.submit_attempts(p_token text, p_attempts jsonb)
returns jsonb
language plpgsql volatile security definer
set search_path = public
as $$
declare
    v_worker uuid;
    v_accepted jsonb;
begin
    select s.worker_id into v_worker
    from public.worker_sessions s
    join public.workers w on w.id = s.worker_id
    where s.token = p_token and s.expires_at > now() and w.active;

    if v_worker is null then
        raise exception 'invalid_token' using errcode = 'P0001';
    end if;

    if jsonb_typeof(p_attempts) <> 'array' or jsonb_array_length(p_attempts) > 100 then
        raise exception 'invalid_batch' using errcode = 'P0001';
    end if;

    insert into public.training_attempts
        (id, worker_id, module, scenario, passed, score, elapsed_seconds, details, completed_at)
    select x.id, v_worker, x.module, x.scenario, x.passed, x.score, x.elapsed_seconds,
           nullif(x.details_json, '')::jsonb, x.completed_at
    from jsonb_to_recordset(p_attempts) as x(
        id uuid, module text, scenario text, passed boolean, score int,
        elapsed_seconds real, details_json text, completed_at timestamptz)
    on conflict (id) do nothing;

    select coalesce(jsonb_agg(t.id), '[]'::jsonb) into v_accepted
    from public.training_attempts t
    where t.worker_id = v_worker
      and t.id in (select (e ->> 'id')::uuid from jsonb_array_elements(p_attempts) e);

    return jsonb_build_object('accepted', v_accepted);
end;
$$;

-- ---------------------------------------------------------------- dashboard (admin) API

create or replace function public.admin_list_workers()
returns table (
    id uuid, worker_code text, display_name text, active boolean, created_at timestamptz,
    attempts bigint, passed bigint, last_activity timestamptz
)
language plpgsql stable security definer
set search_path = public
as $$
begin
    perform public._require_admin();
    return query
        select w.id, w.worker_code, w.display_name, w.active, w.created_at,
               count(t.id), count(t.id) filter (where t.passed), max(t.completed_at)
        from public.workers w
        left join public.training_attempts t on t.worker_id = w.id
        group by w.id
        order by w.created_at desc;
end;
$$;

create or replace function public.admin_create_worker(p_name text)
returns jsonb
language plpgsql volatile security definer
set search_path = public, extensions
as $$
declare
    v_code text := public._new_worker_code();
    v_password text := public._random_password();
    v_id uuid;
begin
    perform public._require_admin();
    if coalesce(trim(p_name), '') = '' then
        raise exception 'name_required' using errcode = 'P0001';
    end if;

    insert into public.workers (worker_code, display_name, pass_hash)
    values (v_code, trim(p_name), crypt(v_password, gen_salt('bf')))
    returning id into v_id;

    return jsonb_build_object('id', v_id, 'worker_code', v_code, 'display_name', trim(p_name), 'password', v_password);
end;
$$;

create or replace function public.admin_reset_password(p_worker_id uuid)
returns jsonb
language plpgsql volatile security definer
set search_path = public, extensions
as $$
declare
    v_password text := public._random_password();
    w public.workers;
begin
    perform public._require_admin();
    update public.workers set pass_hash = crypt(v_password, gen_salt('bf'))
    where id = p_worker_id returning * into w;
    if w.id is null then
        raise exception 'worker_not_found' using errcode = 'P0001';
    end if;
    delete from public.worker_sessions where worker_id = p_worker_id;

    return jsonb_build_object('id', w.id, 'worker_code', w.worker_code, 'display_name', w.display_name, 'password', v_password);
end;
$$;

create or replace function public.admin_set_active(p_worker_id uuid, p_active boolean)
returns void
language plpgsql volatile security definer
set search_path = public
as $$
begin
    perform public._require_admin();
    update public.workers set active = p_active where id = p_worker_id;
    if not p_active then
        delete from public.worker_sessions where worker_id = p_worker_id;
    end if;
end;
$$;

-- Admins read attempts directly (the dashboard filters by worker_id).
grant select on public.training_attempts to authenticated;
create policy "admins read attempts" on public.training_attempts
    for select to authenticated using ((select public.is_admin()));

-- ---------------------------------------------------------------- function grants

revoke execute on all functions in schema public from public, anon, authenticated;

grant execute on function public.worker_login(text, text)          to anon, authenticated;
grant execute on function public.submit_attempts(text, jsonb)       to anon, authenticated;
grant execute on function public.is_admin()                         to authenticated;
grant execute on function public.admin_list_workers()               to authenticated;
grant execute on function public.admin_create_worker(text)          to authenticated;
grant execute on function public.admin_reset_password(uuid)         to authenticated;
grant execute on function public.admin_set_active(uuid, boolean)    to authenticated;
