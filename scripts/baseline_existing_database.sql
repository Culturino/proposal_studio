-- Baseline an existing database against the Initial migration.
--
-- Only for databases whose schema was built by hand before migrations existed. It records
-- Initial as already applied so EF skips it; running the migration for real against these
-- tables would fail on the first CREATE TABLE. A genuinely empty database needs none of
-- this — just run `dotnet ef database update`.
--
-- The Initial migration was verified to reproduce the hand-built schema exactly
-- (164 columns, 33 indexes, no differences), so recording it here is safe.

CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId"    character varying(150) NOT NULL,
    "ProductVersion" character varying(32)  NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260827002702_Initial', '8.0.28')
ON CONFLICT ("MigrationId") DO NOTHING;
