-- AbhyasAR: admin dashboard additions (worker management + admin management).
-- Additive only: no existing table, column or function from 0001_init.sql is changed.
-- Safe to run more than once (create or replace / if not exists).

-- ---------------------------------------------------------------- worker management

create or replace function public.admin_rename_worker(p_worker_id uuid, p_name text)
returns void
language plpgsql volatile security definer
set search_path = public
as $$
begin
    perform public._require_admin();
    if coalesce(trim(p_name), '') = '' then
        raise exception 'name_required' using errcode = 'P0001';
    end if;
    update public.workers set display_name = trim(p_name) where id = p_worker_id;
    if not found then
        raise exception 'worker_not_found' using errcode = 'P0001';
    end if;
end;
$$;

-- Permanently removes a worker, their device sessions and ALL their synced training attempts
-- (ON DELETE CASCADE). The dashboard asks the admin to type the worker ID to confirm.
create or replace function public.admin_delete_worker(p_worker_id uuid)
returns void
language plpgsql volatile security definer
set search_path = public
as $$
begin
    perform public._require_admin();
    delete from public.workers where id = p_worker_id;
    if not found then
        raise exception 'worker_not_found' using errcode = 'P0001';
    end if;
end;
$$;

-- Creates one worker per non-empty name; returns their one-time credentials in input order.
create or replace function public.admin_bulk_create_workers(p_names text[])
returns jsonb
language plpgsql volatile security definer
set search_path = public, extensions
as $$
declare
    v_name text;
    v_code text;
    v_password text;
    v_id uuid;
    v_out jsonb := '[]'::jsonb;
begin
    perform public._require_admin();
    if p_names is null or array_length(p_names, 1) is null then
        raise exception 'names_required' using errcode = 'P0001';
    end if;
    if array_length(p_names, 1) > 200 then
        raise exception 'too_many_names' using errcode = 'P0001';
    end if;

    foreach v_name in array p_names loop
        v_name := trim(v_name);
        continue when v_name = '';
        v_code := public._new_worker_code();
        v_password := public._random_password();
        insert into public.workers (worker_code, display_name, pass_hash)
        values (v_code, v_name, crypt(v_password, gen_salt('bf')))
        returning id into v_id;
        v_out := v_out || jsonb_build_object('id', v_id, 'worker_code', v_code, 'display_name', v_name, 'password', v_password);
    end loop;
    return v_out;
end;
$$;

-- ---------------------------------------------------------------- admin management
-- New admin accounts are still created in Supabase (Authentication -> Users); these functions
-- grant/revoke dashboard access for an existing account.

create or replace function public.admin_list_admins()
returns table (user_id uuid, email text, added_at timestamptz, last_sign_in_at timestamptz, is_me boolean)
language plpgsql stable security definer
set search_path = public, auth
as $$
begin
    perform public._require_admin();
    return query
        select a.user_id, u.email::text, u.created_at, u.last_sign_in_at, a.user_id = auth.uid()
        from public.admins a
        join auth.users u on u.id = a.user_id
        order by u.email;
end;
$$;

create or replace function public.admin_add_admin(p_email text)
returns void
language plpgsql volatile security definer
set search_path = public, auth
as $$
declare
    v_user uuid;
begin
    perform public._require_admin();
    select id into v_user from auth.users where lower(email) = lower(trim(p_email));
    if v_user is null then
        raise exception 'user_not_found' using errcode = 'P0001';
    end if;
    insert into public.admins (user_id) values (v_user) on conflict do nothing;
end;
$$;

create or replace function public.admin_remove_admin(p_user_id uuid)
returns void
language plpgsql volatile security definer
set search_path = public
as $$
begin
    perform public._require_admin();
    if p_user_id = auth.uid() then
        raise exception 'cannot_remove_self' using errcode = 'P0001';
    end if;
    if (select count(*) from public.admins) <= 1 then
        raise exception 'last_admin' using errcode = 'P0001';
    end if;
    delete from public.admins where user_id = p_user_id;
end;
$$;

-- ---------------------------------------------------------------- grants

revoke execute on function public.admin_rename_worker(uuid, text)       from public, anon;
revoke execute on function public.admin_delete_worker(uuid)             from public, anon;
revoke execute on function public.admin_bulk_create_workers(text[])     from public, anon;
revoke execute on function public.admin_list_admins()                   from public, anon;
revoke execute on function public.admin_add_admin(text)                 from public, anon;
revoke execute on function public.admin_remove_admin(uuid)              from public, anon;

grant execute on function public.admin_rename_worker(uuid, text)        to authenticated;
grant execute on function public.admin_delete_worker(uuid)              to authenticated;
grant execute on function public.admin_bulk_create_workers(text[])      to authenticated;
grant execute on function public.admin_list_admins()                    to authenticated;
grant execute on function public.admin_add_admin(text)                  to authenticated;
grant execute on function public.admin_remove_admin(uuid)               to authenticated;
