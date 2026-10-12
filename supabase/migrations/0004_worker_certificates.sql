-- Let a signed-in worker retrieve only their own issued certificates.
-- Apply after 0003_certificates.sql.
create or replace function public.worker_list_certificates(p_token text)
returns jsonb
language plpgsql stable security definer
set search_path = public
as $$
declare
    v_worker uuid;
    v_certificates jsonb;
begin
    select s.worker_id into v_worker
    from public.worker_sessions s
    join public.workers w on w.id = s.worker_id
    where s.token = p_token and s.expires_at > now() and w.active;

    if v_worker is null then
        raise exception 'invalid_token' using errcode = 'P0001';
    end if;

    select coalesce(jsonb_agg(jsonb_build_object(
        'id', c.id,
        'module', c.module,
        'score', c.score,
        'issued_at', c.issued_at
    ) order by c.issued_at desc), '[]'::jsonb)
    into v_certificates
    from public.certificates c
    where c.worker_id = v_worker;

    return jsonb_build_object('certificates', v_certificates);
end;
$$;

revoke execute on function public.worker_list_certificates(text) from public;
grant execute on function public.worker_list_certificates(text) to anon, authenticated;
