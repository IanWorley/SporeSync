CREATE FUNCTION core.get_system_property(p_property_name varchar(200))
RETURNS TABLE (
    id varchar(32),
    property_name varchar(200),
    property_value varchar(1000))
LANGUAGE sql
AS $$
    SELECT id, property_name, property_value
    FROM core.system_properties
    WHERE property_name = p_property_name;
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
