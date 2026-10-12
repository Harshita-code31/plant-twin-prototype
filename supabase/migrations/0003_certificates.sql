-- AbhyasAR: server-backed certificates and public QR verification.
-- Apply after 0001_init.sql and 0002_admin_dashboard.sql.

create table if not exists public.certificates (
    id         uuid primary key,
    worker_id  uuid not null references public.workers (id) on delete cascade,
    module     text not null check (module in ('fire_safety', 'machine_training')),
    score      integer not null check (score between 80 and 100),
    issued_at  timestamptz not null,
    synced_at  timestamptz not null default now()
);

create index if not exists certificates_worker_idx
    on public.certificates (worker_id, issued_at desc);

alter table public.certificates enable row level security;
revoke all on public.certificates from anon, authenticated;

-- The device submits certificates only through this worker-session-gated function.
-- A certificate is accepted only when synced attempts already contain a passed practical
-- and a passed quiz for the same module. SyncService uploads attempts before calling this.
create or replace function public.submit_certificates(p_token text, p_certificates jsonb)
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
    if jsonb_typeof(p_certificates) <> 'array' or jsonb_array_length(p_certificates) > 20 then
        raise exception 'invalid_batch' using errcode = 'P0001';
    end if;

    insert into public.certificates (id, worker_id, module, score, issued_at)
    select x.id, v_worker, x.module, x.score, x.issued_at
    from jsonb_to_recordset(p_certificates) as x(
        id uuid, module text, score integer, issued_at timestamptz)
    where x.module in ('fire_safety', 'machine_training')
      and x.score between 80 and 100
      and x.issued_at <= now() + interval '5 minutes'
      and exists (
          select 1 from public.training_attempts t
          where t.worker_id = v_worker and t.module = x.module and t.passed
            and coalesce(t.scenario, '') like '%/Quiz'
      )
      and exists (
          select 1 from public.training_attempts t
          where t.worker_id = v_worker and t.module = x.module and t.passed
            and coalesce(t.scenario, '') not like '%/Quiz'
      )
    on conflict (id) do nothing;

    select coalesce(jsonb_agg(c.id), '[]'::jsonb) into v_accepted
    from public.certificates c
    where c.worker_id = v_worker
      and c.id in (select (e ->> 'id')::uuid from jsonb_array_elements(p_certificates) e);

    return jsonb_build_object('accepted', v_accepted);
end;
$$;

-- Safe public lookup used by the GitHub Pages QR route. The UUID is the bearer lookup key;
-- worker login codes, attempt history, session tokens and admin data are never returned.
create or replace function public.verify_certificate(p_certificate_id uuid)
returns jsonb
language sql stable security definer
set search_path = public
as $$
    select coalesce(
        (
            select jsonb_build_object(
                'valid', true,
                'certificate_id', c.id,
                'trainee_name', w.display_name,
                'module', c.module,
                'score', c.score,
                'issued_at', c.issued_at
            )
            from public.certificates c
            join public.workers w on w.id = c.worker_id
            where c.id = p_certificate_id and w.active
        ),
        jsonb_build_object('valid', false)
    );
$$;

create or replace function public.admin_list_certificates()
returns table (
    id uuid,
    worker_id uuid,
    module text,
    score integer,
    issued_at timestamptz,
    synced_at timestamptz
)
language plpgsql stable security definer
set search_path = public
as $$
begin
    perform public._require_admin();
    return query
        select c.id, c.worker_id, c.module, c.score, c.issued_at, c.synced_at
        from public.certificates c
        order by c.issued_at desc;
end;
$$;

revoke execute on function public.submit_certificates(text, jsonb) from public;
revoke execute on function public.verify_certificate(uuid) from public;
revoke execute on function public.admin_list_certificates() from public, anon;

grant execute on function public.submit_certificates(text, jsonb) to anon, authenticated;
grant execute on function public.verify_certificate(uuid) to anon, authenticated;
grant execute on function public.admin_list_certificates() to authenticated;
