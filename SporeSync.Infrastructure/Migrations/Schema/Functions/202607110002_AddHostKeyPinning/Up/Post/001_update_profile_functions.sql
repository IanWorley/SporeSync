DROP FUNCTION IF EXISTS core.upsert_sftp_connection_profile(uuid, varchar(200), varchar(255), integer, varchar(200), text, text, text, boolean);
DROP FUNCTION IF EXISTS core.get_sftp_connection_profile(uuid);
DROP FUNCTION IF EXISTS core.get_sftp_connection_profiles();

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
    host_key_fingerprint_sha256 varchar(100),
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
           p.host_key_fingerprint_sha256,
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
    host_key_fingerprint_sha256 varchar(100),
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
           p.host_key_fingerprint_sha256,
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
    p_host_key_fingerprint_sha256 varchar(100),
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
    host_key_fingerprint_sha256 varchar(100),
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
        host_key_fingerprint_sha256,
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
        p_host_key_fingerprint_sha256,
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
        host_key_fingerprint_sha256 = EXCLUDED.host_key_fingerprint_sha256,
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
        saved_profile.host_key_fingerprint_sha256,
        saved_profile.is_default;
$$;
