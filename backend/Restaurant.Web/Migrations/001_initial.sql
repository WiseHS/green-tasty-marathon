CREATE TABLE IF NOT EXISTS app_documents (
                kind text NOT NULL, id text NOT NULL, payload jsonb NOT NULL,
                PRIMARY KEY(kind,id)
            );
            CREATE INDEX IF NOT EXISTS app_documents_payload ON app_documents USING gin(payload);
            CREATE TABLE IF NOT EXISTS app_images (
                key text PRIMARY KEY, content_type text NOT NULL,
                data bytea NOT NULL, is_public boolean NOT NULL DEFAULT false
            );
            CREATE TABLE IF NOT EXISTS app_users (
                id uuid PRIMARY KEY, email text NOT NULL UNIQUE,
                password_hash text NOT NULL, first_name text NOT NULL,
                last_name text NOT NULL, roles text NOT NULL DEFAULT 'Customer',
                failed_attempts integer NOT NULL DEFAULT 0, locked_until timestamptz,
                token_version integer NOT NULL DEFAULT 0
            );