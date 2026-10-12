-- AbhyasAR: accept a certificate when the practical scored at least the pass mark.
-- Apply after 0003_certificates.sql.
--
-- Why: the app passes a training on its weighted score (practice 40% + quiz 60%, pass at 80), and a
-- practical can score e.g. 90 while one PASS step was imperfect -- the device then stores it with
-- passed = false. 0003 required a practical with passed = true, so such a worker got a certificate on
-- the phone that the server rejected (its QR would never verify). A practical attempt now counts when it
-- either passed or scored 80 or more. The quiz rule is unchanged.

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
          where t.worker_id = v_worker and t.module = x.module
            and (t.passed or t.score >= 80)
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

-- create or replace keeps the grants from 0003, but restate them so this file is self-contained.
revoke execute on function public.submit_certificates(text, jsonb) from public;
grant execute on function public.submit_certificates(text, jsonb) to anon, authenticated;
