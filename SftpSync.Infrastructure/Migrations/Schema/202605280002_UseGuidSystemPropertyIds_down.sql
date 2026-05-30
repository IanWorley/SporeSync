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
