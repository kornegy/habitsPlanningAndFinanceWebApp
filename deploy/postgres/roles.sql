-- Least-privilege roles for Personal Platform.
--   pp_owner : owns the "app" schema; used ONLY by the migration job.
--   pp_app   : used by the running application; DML only, no DDL, no superuser, no ownership.
-- Executed by psql with variables: db_name, owner_password, app_password.

CREATE ROLE pp_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE pp_app LOGIN PASSWORD :'app_password' CONNECTION LIMIT 100;

REVOKE ALL ON DATABASE :"db_name" FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE :"db_name" TO pp_owner;
GRANT CONNECT ON DATABASE :"db_name" TO pp_app;

REVOKE ALL ON SCHEMA public FROM PUBLIC;
CREATE SCHEMA app AUTHORIZATION pp_owner;
GRANT USAGE ON SCHEMA app TO pp_app;

-- Every table/sequence the migration role creates later is automatically usable (DML only) by the app role.
ALTER DEFAULT PRIVILEGES FOR ROLE pp_owner IN SCHEMA app GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO pp_app;
ALTER DEFAULT PRIVILEGES FOR ROLE pp_owner IN SCHEMA app GRANT USAGE, SELECT ON SEQUENCES TO pp_app;

ALTER ROLE pp_owner SET search_path = app;
ALTER ROLE pp_app SET search_path = app;
-- Server-side safety nets against runaway queries and abandoned transactions.
ALTER ROLE pp_app SET statement_timeout = '30s';
ALTER ROLE pp_app SET idle_in_transaction_session_timeout = '60s';
ALTER ROLE pp_app SET lock_timeout = '10s';
