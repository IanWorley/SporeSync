DROP FUNCTION IF EXISTS core.insert_system_property_if_missing(uuid, varchar(200), varchar(1000));
DROP FUNCTION IF EXISTS core.upsert_system_property(uuid, varchar(200), varchar(1000));
DROP FUNCTION IF EXISTS core.get_system_property(varchar);

ALTER TABLE core.system_properties
ADD COLUMN id_text varchar(32) NOT NULL DEFAULT replace(gen_random_uuid()::text, '-', '');

ALTER TABLE core.system_properties
DROP CONSTRAINT system_properties_pkey;

ALTER TABLE core.system_properties
DROP COLUMN id;

ALTER TABLE core.system_properties
RENAME COLUMN id_text TO id;

ALTER TABLE core.system_properties
ADD CONSTRAINT system_properties_pkey PRIMARY KEY (id);

CREATE FUNCTION core.get_system_property(p_property_name varchar(200))
RETURNS TABLE (
    id varchar(32),
    property_name varchar(200),
    property_value varchar(1000))
LANGUAGE sql
AS $$
    SELECT sp.id, sp.property_name, sp.property_value
    FROM core.system_properties sp
    WHERE sp.property_name = p_property_name;
$$;

CREATE FUNCTION core.upsert_system_property(
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
    DO UPDATE SET property_value = EXCLUDED.property_value
    RETURNING id, property_name, property_value;
$$;

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
