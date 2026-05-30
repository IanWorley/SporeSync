DROP FUNCTION IF EXISTS core.insert_system_property_if_missing(uuid, varchar(200), varchar(1000));
DROP FUNCTION IF EXISTS core.upsert_system_property(uuid, varchar(200), varchar(1000));
DROP FUNCTION IF EXISTS core.get_system_property(varchar);
