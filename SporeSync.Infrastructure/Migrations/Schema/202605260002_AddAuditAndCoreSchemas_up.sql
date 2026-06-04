CREATE SCHEMA audit;
CREATE SCHEMA core;

CREATE TABLE core.system_properties
(
    id varchar(32) PRIMARY KEY,
    property_name varchar(200) NOT NULL UNIQUE,
    property_value varchar(1000) NOT NULL
);
