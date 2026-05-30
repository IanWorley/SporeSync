ALTER TABLE core.system_properties
ADD COLUMN id_uuid uuid NOT NULL DEFAULT gen_random_uuid();

ALTER TABLE core.system_properties
DROP CONSTRAINT system_properties_pkey;

ALTER TABLE core.system_properties
DROP COLUMN id;

ALTER TABLE core.system_properties
RENAME COLUMN id_uuid TO id;

ALTER TABLE core.system_properties
ADD CONSTRAINT system_properties_pkey PRIMARY KEY (id);
