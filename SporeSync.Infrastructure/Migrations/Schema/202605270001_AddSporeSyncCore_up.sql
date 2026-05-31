CREATE TABLE core.sftp_connection_profiles
(
    id uuid PRIMARY KEY,
    name varchar(200) NOT NULL,
    host varchar(255) NOT NULL,
    port integer NOT NULL DEFAULT 22,
    username varchar(200) NOT NULL,
    encrypted_password text NULL,
    encrypted_private_key text NULL,
    encrypted_private_key_passphrase text NULL,
    is_default boolean NOT NULL DEFAULT false,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_sftp_connection_profiles_port CHECK (port > 0 AND port <= 65535),
    CONSTRAINT ck_sftp_connection_profiles_has_auth CHECK (
        encrypted_password IS NOT NULL OR encrypted_private_key IS NOT NULL
    )
);

CREATE UNIQUE INDEX ux_sftp_connection_profiles_default
ON core.sftp_connection_profiles (is_default)
WHERE is_default;

CREATE TABLE core.sftp_sync_jobs
(
    id uuid PRIMARY KEY,
    connection_profile_id uuid NOT NULL REFERENCES core.sftp_connection_profiles(id),
    name varchar(200) NOT NULL,
    source_path varchar(1000) NOT NULL,
    destination_path varchar(1000) NOT NULL,
    polling_interval_seconds integer NOT NULL DEFAULT 120,
    is_enabled boolean NOT NULL DEFAULT true,
    last_polled_at timestamptz NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_sftp_sync_jobs_polling_interval CHECK (polling_interval_seconds >= 30)
);

CREATE TABLE core.sftp_sync_runs
(
    id uuid PRIMARY KEY,
    job_id uuid NOT NULL REFERENCES core.sftp_sync_jobs(id),
    status varchar(32) NOT NULL,
    started_at timestamptz NOT NULL DEFAULT now(),
    completed_at timestamptz NULL,
    total_file_count integer NOT NULL DEFAULT 0,
    completed_file_count integer NOT NULL DEFAULT 0,
    skipped_file_count integer NOT NULL DEFAULT 0,
    failed_file_count integer NOT NULL DEFAULT 0,
    total_bytes bigint NOT NULL DEFAULT 0,
    downloaded_bytes bigint NOT NULL DEFAULT 0,
    current_bytes_per_second numeric(20, 2) NULL,
    error_message text NULL,
    CONSTRAINT ck_sftp_sync_runs_status CHECK (
        status IN ('queued', 'scanning', 'downloading', 'completed', 'failed', 'cancelled')
    )
);

CREATE TABLE core.download_queue_items
(
    id uuid PRIMARY KEY,
    job_id uuid NOT NULL REFERENCES core.sftp_sync_jobs(id),
    sync_run_id uuid NULL REFERENCES core.sftp_sync_runs(id),
    remote_path varchar(2000) NOT NULL,
    destination_path varchar(2000) NOT NULL,
    file_size_bytes bigint NOT NULL,
    remote_modified_at timestamptz NULL,
    status varchar(32) NOT NULL DEFAULT 'queued',
    bytes_downloaded bigint NOT NULL DEFAULT 0,
    current_bytes_per_second numeric(20, 2) NULL,
    retry_count integer NOT NULL DEFAULT 0,
    handled_reason varchar(100) NULL,
    error_message text NULL,
    queued_at timestamptz NOT NULL DEFAULT now(),
    started_at timestamptz NULL,
    completed_at timestamptz NULL,
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_download_queue_items_status CHECK (
        status IN ('queued', 'comparing', 'downloading', 'completed', 'failed', 'skipped')
    ),
    CONSTRAINT ck_download_queue_items_file_size CHECK (file_size_bytes >= 0),
    CONSTRAINT ck_download_queue_items_bytes_downloaded CHECK (bytes_downloaded >= 0)
);

CREATE UNIQUE INDEX ux_download_queue_items_job_remote_path
ON core.download_queue_items (job_id, remote_path);

CREATE INDEX ix_download_queue_items_job_status
ON core.download_queue_items (job_id, status);

CREATE INDEX ix_download_queue_items_sync_run
ON core.download_queue_items (sync_run_id);
