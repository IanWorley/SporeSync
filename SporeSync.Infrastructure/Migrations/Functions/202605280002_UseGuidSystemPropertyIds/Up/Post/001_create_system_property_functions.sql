CREATE FUNCTION core.get_system_property(p_property_name varchar(200))
RETURNS TABLE (
    id uuid,
    property_name varchar(200),
    property_value varchar(1000))
LANGUAGE sql
AS $$
    SELECT sp.id, sp.property_name, sp.property_value
    FROM core.system_properties sp
    WHERE sp.property_name = p_property_name;
$$;

CREATE FUNCTION core.upsert_system_property(
    p_id uuid,
    p_property_name varchar(200),
    p_property_value varchar(1000))
RETURNS TABLE (
    id uuid,
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
    p_id uuid,
    p_property_name varchar(200),
    p_property_value varchar(1000))
RETURNS TABLE (
    id uuid,
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
