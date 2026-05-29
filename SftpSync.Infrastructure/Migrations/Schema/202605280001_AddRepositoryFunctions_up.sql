CREATE FUNCTION core.insert_system_property_if_missing(
    p_id varchar(32),
    p_property_name varchar(200),
    p_property_value varchar(1000))
RETURNS TABLE (
    id varchar(32),
    property_name varchar(200),
    property_value varchar(1000))
LANGUAGE sql
AS $$
    INSERT INTO core.system_properties (id, property_name, property_value)
    VALUES (p_id, p_property_name, p_property_value)
    ON CONFLICT (property_name)
    DO NOTHING;

    SELECT sp.id, sp.property_name, sp.property_value
    FROM core.system_properties sp
    WHERE sp.property_name = p_property_name;
$$;

CREATE FUNCTION core.get_sftp_connection_profiles()
RETURNS TABLE (
    id uuid,
    name varchar(200),
    host varchar(255),
    port integer,
    username varchar(200),
    encrypted_password text,
    encrypted_private_key text,
    encrypted_private_key_passphrase text,
    is_default boolean)
LANGUAGE sql
AS $$
    SELECT p.id,
           p.name,
           p.host,
           p.port,
           p.username,
           p.encrypted_password,
           p.encrypted_private_key,
           p.encrypted_private_key_passphrase,
           p.is_default
    FROM core.sftp_connection_profiles p
    ORDER BY p.is_default DESC, p.name;
$$;

CREATE FUNCTION core.get_sftp_connection_profile(p_id uuid)
RETURNS TABLE (
    id uuid,
    name varchar(200),
    host varchar(255),
    port integer,
    username varchar(200),
    encrypted_password text,
    encrypted_private_key text,
    encrypted_private_key_passphrase text,
    is_default boolean)
LANGUAGE sql
AS $$
    SELECT p.id,
           p.name,
           p.host,
           p.port,
           p.username,
           p.encrypted_password,
           p.encrypted_private_key,
           p.encrypted_private_key_passphrase,
           p.is_default
    FROM core.sftp_connection_profiles p
    WHERE p.id = p_id;
$$;

CREATE FUNCTION core.upsert_sftp_connection_profile(
    p_id uuid,
    p_name varchar(200),
    p_host varchar(255),
    p_port integer,
    p_username varchar(200),
    p_encrypted_password text,
    p_encrypted_private_key text,
    p_encrypted_private_key_passphrase text,
    p_is_default boolean)
RETURNS TABLE (
    id uuid,
    name varchar(200),
    host varchar(255),
    port integer,
    username varchar(200),
    encrypted_password text,
    encrypted_private_key text,
    encrypted_private_key_passphrase text,
    is_default boolean)
LANGUAGE sql
AS $$
    WITH unset_default AS (
        UPDATE core.sftp_connection_profiles profiles
        SET is_default = false,
            updated_at = now()
        WHERE p_is_default
          AND profiles.is_default = true
          AND profiles.id <> p_id
        RETURNING 1
    )
    INSERT INTO core.sftp_connection_profiles AS saved_profile (
        id,
        name,
        host,
        port,
        username,
        encrypted_password,
        encrypted_private_key,
        encrypted_private_key_passphrase,
        is_default)
    VALUES (
        p_id,
        p_name,
        p_host,
        p_port,
        p_username,
        p_encrypted_password,
        p_encrypted_private_key,
        p_encrypted_private_key_passphrase,
        p_is_default)
    ON CONFLICT (id)
    DO UPDATE SET
        name = EXCLUDED.name,
        host = EXCLUDED.host,
        port = EXCLUDED.port,
        username = EXCLUDED.username,
        encrypted_password = EXCLUDED.encrypted_password,
        encrypted_private_key = EXCLUDED.encrypted_private_key,
        encrypted_private_key_passphrase = EXCLUDED.encrypted_private_key_passphrase,
        is_default = EXCLUDED.is_default,
        updated_at = now()
    RETURNING
        saved_profile.id,
        saved_profile.name,
        saved_profile.host,
        saved_profile.port,
        saved_profile.username,
        saved_profile.encrypted_password,
        saved_profile.encrypted_private_key,
        saved_profile.encrypted_private_key_passphrase,
        saved_profile.is_default;
$$;

CREATE FUNCTION core.has_any_sftp_connection_profile_encrypted_secrets()
RETURNS boolean
LANGUAGE sql
AS $$
    SELECT EXISTS (
        SELECT 1
        FROM core.sftp_connection_profiles p
        WHERE p.encrypted_password IS NOT NULL
           OR p.encrypted_private_key IS NOT NULL
           OR p.encrypted_private_key_passphrase IS NOT NULL
    );
$$;

CREATE FUNCTION core.get_sftp_sync_jobs()
RETURNS TABLE (
    id uuid,
    connection_profile_id uuid,
    name varchar(200),
    source_path varchar(1000),
    destination_path varchar(1000),
    polling_interval_seconds integer,
    is_enabled boolean,
    last_polled_at timestamptz)
LANGUAGE sql
AS $$
    SELECT j.id,
           j.connection_profile_id,
           j.name,
           j.source_path,
           j.destination_path,
           j.polling_interval_seconds,
           j.is_enabled,
           j.last_polled_at
    FROM core.sftp_sync_jobs j
    ORDER BY j.name;
$$;

CREATE FUNCTION core.get_sftp_sync_job(p_id uuid)
RETURNS TABLE (
    id uuid,
    connection_profile_id uuid,
    name varchar(200),
    source_path varchar(1000),
    destination_path varchar(1000),
    polling_interval_seconds integer,
    is_enabled boolean,
    last_polled_at timestamptz)
LANGUAGE sql
AS $$
    SELECT j.id,
           j.connection_profile_id,
           j.name,
           j.source_path,
           j.destination_path,
           j.polling_interval_seconds,
           j.is_enabled,
           j.last_polled_at
    FROM core.sftp_sync_jobs j
    WHERE j.id = p_id;
$$;

CREATE FUNCTION core.upsert_sftp_sync_job(
    p_id uuid,
    p_connection_profile_id uuid,
    p_name varchar(200),
    p_source_path varchar(1000),
    p_destination_path varchar(1000),
    p_polling_interval_seconds integer,
    p_is_enabled boolean)
RETURNS TABLE (
    id uuid,
    connection_profile_id uuid,
    name varchar(200),
    source_path varchar(1000),
    destination_path varchar(1000),
    polling_interval_seconds integer,
    is_enabled boolean,
    last_polled_at timestamptz)
LANGUAGE sql
AS $$
    INSERT INTO core.sftp_sync_jobs (
        id,
        connection_profile_id,
        name,
        source_path,
        destination_path,
        polling_interval_seconds,
        is_enabled)
    VALUES (
        p_id,
        p_connection_profile_id,
        p_name,
        p_source_path,
        p_destination_path,
        p_polling_interval_seconds,
        p_is_enabled)
    ON CONFLICT (id)
    DO UPDATE SET
        connection_profile_id = EXCLUDED.connection_profile_id,
        name = EXCLUDED.name,
        source_path = EXCLUDED.source_path,
        destination_path = EXCLUDED.destination_path,
        polling_interval_seconds = EXCLUDED.polling_interval_seconds,
        is_enabled = EXCLUDED.is_enabled,
        updated_at = now()
    RETURNING id,
              connection_profile_id,
              name,
              source_path,
              destination_path,
              polling_interval_seconds,
              is_enabled,
              last_polled_at;
$$;

CREATE FUNCTION core.get_sftp_sync_run(p_id uuid)
RETURNS TABLE (
    id uuid,
    job_id uuid,
    job_name varchar(200),
    status varchar(32),
    started_at timestamptz,
    completed_at timestamptz,
    total_file_count integer,
    completed_file_count integer,
    skipped_file_count integer,
    failed_file_count integer,
    total_bytes bigint,
    downloaded_bytes bigint,
    current_bytes_per_second numeric(20, 2),
    error_message text)
LANGUAGE sql
AS $$
    SELECT r.id,
           r.job_id,
           j.name AS job_name,
           r.status,
           r.started_at,
           r.completed_at,
           r.total_file_count,
           r.completed_file_count,
           r.skipped_file_count,
           r.failed_file_count,
           r.total_bytes,
           r.downloaded_bytes,
           r.current_bytes_per_second,
           r.error_message
    FROM core.sftp_sync_runs r
    INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id
    WHERE r.id = p_id;
$$;

CREATE FUNCTION core.count_sftp_sync_runs(
    p_statuses text[],
    p_search text)
RETURNS bigint
LANGUAGE sql
AS $$
    SELECT count(*)
    FROM core.sftp_sync_runs r
    INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id
    WHERE (p_statuses IS NULL OR r.status = ANY(p_statuses))
      AND (
            p_search IS NULL
            OR j.name ILIKE p_search
            OR EXISTS (
                SELECT 1
                FROM core.download_queue_items qi
                WHERE qi.sync_run_id = r.id
                  AND (qi.remote_path ILIKE p_search OR qi.destination_path ILIKE p_search)
            )
          );
$$;

CREATE FUNCTION core.get_sftp_sync_runs(
    p_statuses text[],
    p_search text,
    p_sort_by text,
    p_sort_direction text,
    p_page_size integer,
    p_offset integer)
RETURNS TABLE (
    id uuid,
    job_id uuid,
    job_name varchar(200),
    status varchar(32),
    started_at timestamptz,
    completed_at timestamptz,
    total_file_count integer,
    completed_file_count integer,
    skipped_file_count integer,
    failed_file_count integer,
    total_bytes bigint,
    downloaded_bytes bigint,
    current_bytes_per_second numeric(20, 2),
    error_message text)
LANGUAGE sql
AS $$
    WITH filtered_runs AS (
        SELECT r.id,
               r.job_id,
               j.name AS job_name,
               r.status,
               r.started_at,
               r.completed_at,
               r.total_file_count,
               r.completed_file_count,
               r.skipped_file_count,
               r.failed_file_count,
               r.total_bytes,
               r.downloaded_bytes,
               r.current_bytes_per_second,
               r.error_message
        FROM core.sftp_sync_runs r
        INNER JOIN core.sftp_sync_jobs j ON j.id = r.job_id
        WHERE (p_statuses IS NULL OR r.status = ANY(p_statuses))
          AND (
                p_search IS NULL
                OR j.name ILIKE p_search
                OR EXISTS (
                    SELECT 1
                    FROM core.download_queue_items qi
                    WHERE qi.sync_run_id = r.id
                      AND (qi.remote_path ILIKE p_search OR qi.destination_path ILIKE p_search)
                )
              )
    )
    SELECT fr.id,
           fr.job_id,
           fr.job_name,
           fr.status,
           fr.started_at,
           fr.completed_at,
           fr.total_file_count,
           fr.completed_file_count,
           fr.skipped_file_count,
           fr.failed_file_count,
           fr.total_bytes,
           fr.downloaded_bytes,
           fr.current_bytes_per_second,
           fr.error_message
    FROM filtered_runs fr
    ORDER BY
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'status' THEN fr.status::text
                WHEN 'jobName' THEN fr.job_name::text
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'status' THEN fr.status::text
                WHEN 'jobName' THEN fr.job_name::text
            END
        END DESC,
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'size' THEN fr.total_bytes::numeric
                WHEN 'progress' THEN CASE WHEN fr.total_bytes = 0 THEN 0 ELSE fr.downloaded_bytes::numeric / fr.total_bytes END
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'size' THEN fr.total_bytes::numeric
                WHEN 'progress' THEN CASE WHEN fr.total_bytes = 0 THEN 0 ELSE fr.downloaded_bytes::numeric / fr.total_bytes END
            END
        END DESC,
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'completedAt' THEN fr.completed_at
                ELSE fr.started_at
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'completedAt' THEN fr.completed_at
                ELSE fr.started_at
            END
        END DESC,
        fr.started_at DESC,
        fr.id
    LIMIT p_page_size OFFSET p_offset;
$$;

CREATE FUNCTION core.get_download_queue_items(
    p_run_id uuid,
    p_statuses text[],
    p_search text,
    p_sort_by text,
    p_sort_direction text,
    p_page_size integer,
    p_offset integer)
RETURNS TABLE (
    id uuid,
    job_id uuid,
    sync_run_id uuid,
    remote_path varchar(2000),
    destination_path varchar(2000),
    file_size_bytes bigint,
    remote_modified_at timestamptz,
    status varchar(32),
    bytes_downloaded bigint,
    current_bytes_per_second numeric(20, 2),
    retry_count integer,
    handled_reason varchar(100),
    error_message text,
    queued_at timestamptz,
    started_at timestamptz,
    completed_at timestamptz,
    updated_at timestamptz)
LANGUAGE sql
AS $$
    WITH filtered_items AS (
        SELECT qi.id,
               qi.job_id,
               qi.sync_run_id,
               qi.remote_path,
               qi.destination_path,
               qi.file_size_bytes,
               qi.remote_modified_at,
               qi.status,
               qi.bytes_downloaded,
               qi.current_bytes_per_second,
               qi.retry_count,
               qi.handled_reason,
               qi.error_message,
               qi.queued_at,
               qi.started_at,
               qi.completed_at,
               qi.updated_at
        FROM core.download_queue_items qi
        WHERE qi.sync_run_id = p_run_id
          AND (p_statuses IS NULL OR qi.status = ANY(p_statuses))
          AND (p_search IS NULL OR qi.remote_path ILIKE p_search OR qi.destination_path ILIKE p_search)
    )
    SELECT fi.id,
           fi.job_id,
           fi.sync_run_id,
           fi.remote_path,
           fi.destination_path,
           fi.file_size_bytes,
           fi.remote_modified_at,
           fi.status,
           fi.bytes_downloaded,
           fi.current_bytes_per_second,
           fi.retry_count,
           fi.handled_reason,
           fi.error_message,
           fi.queued_at,
           fi.started_at,
           fi.completed_at,
           fi.updated_at
    FROM filtered_items fi
    ORDER BY
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'status' THEN fi.status::text
                WHEN 'basename' THEN regexp_replace(fi.remote_path, '^.*/', '')
                WHEN 'path' THEN fi.remote_path::text
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'status' THEN fi.status::text
                WHEN 'basename' THEN regexp_replace(fi.remote_path, '^.*/', '')
                WHEN 'path' THEN fi.remote_path::text
            END
        END DESC,
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'size' THEN fi.file_size_bytes::numeric
                WHEN 'progress' THEN CASE WHEN fi.file_size_bytes = 0 THEN 0 ELSE fi.bytes_downloaded::numeric / fi.file_size_bytes END
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'size' THEN fi.file_size_bytes::numeric
                WHEN 'progress' THEN CASE WHEN fi.file_size_bytes = 0 THEN 0 ELSE fi.bytes_downloaded::numeric / fi.file_size_bytes END
            END
        END DESC,
        CASE WHEN lower(p_sort_direction) = 'asc' THEN
            CASE p_sort_by
                WHEN 'completedAt' THEN fi.completed_at
                ELSE fi.queued_at
            END
        END ASC,
        CASE WHEN lower(p_sort_direction) <> 'asc' THEN
            CASE p_sort_by
                WHEN 'completedAt' THEN fi.completed_at
                ELSE fi.queued_at
            END
        END DESC,
        fi.queued_at DESC,
        fi.id
    LIMIT p_page_size OFFSET p_offset;
$$;

CREATE FUNCTION core.count_download_queue_items(
    p_run_id uuid,
    p_statuses text[],
    p_search text)
RETURNS bigint
LANGUAGE sql
AS $$
    SELECT count(*)
    FROM core.download_queue_items qi
    WHERE qi.sync_run_id = p_run_id
      AND (p_statuses IS NULL OR qi.status = ANY(p_statuses))
      AND (p_search IS NULL OR qi.remote_path ILIKE p_search OR qi.destination_path ILIKE p_search);
$$;
