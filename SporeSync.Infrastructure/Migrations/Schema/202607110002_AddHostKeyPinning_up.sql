ALTER TABLE core.sftp_connection_profiles
    ADD COLUMN host_key_fingerprint_sha256 varchar(100) NULL;
