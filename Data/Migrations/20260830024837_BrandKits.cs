using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProposalStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrandKits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Dev databases still have the pre-EF brand_kits row (name/tokens).
            // Fresh databases have no table. Handle both without dropping the
            // businesses.brand_kit_id foreign key.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF NOT EXISTS (
                    SELECT 1 FROM information_schema.tables
                    WHERE table_schema = 'public' AND table_name = 'brand_kits'
                  ) THEN
                    CREATE TABLE brand_kits (
                      id uuid NOT NULL,
                      business_id uuid NOT NULL,
                      colors jsonb NOT NULL,
                      updated_at timestamptz NOT NULL,
                      CONSTRAINT "PK_brand_kits" PRIMARY KEY (id)
                    );
                  ELSE
                    ALTER TABLE brand_kits ADD COLUMN IF NOT EXISTS business_id uuid;
                    ALTER TABLE brand_kits ADD COLUMN IF NOT EXISTS colors jsonb;

                    IF EXISTS (
                      SELECT 1 FROM information_schema.columns
                      WHERE table_schema = 'public' AND table_name = 'brand_kits' AND column_name = 'name'
                    ) THEN
                      ALTER TABLE brand_kits ALTER COLUMN name SET DEFAULT '';
                    END IF;

                    UPDATE brand_kits k
                    SET business_id = b.id
                    FROM businesses b
                    WHERE b.brand_kit_id = k.id
                      AND k.business_id IS NULL;

                    IF EXISTS (
                      SELECT 1 FROM information_schema.columns
                      WHERE table_schema = 'public' AND table_name = 'brand_kits' AND column_name = 'tokens'
                    ) THEN
                      UPDATE brand_kits
                      SET colors = jsonb_build_object(
                        'ink', COALESCE(tokens #>> '{colors,nearBlack}', '#141312'),
                        'ink2', COALESCE(tokens #>> '{colors,black}', '#1E1C1A'),
                        'paper', COALESCE(tokens #>> '{colors,backgroundLightAlt}', '#FBFAF7'),
                        'cream', COALESCE(tokens #>> '{colors,backgroundLight}', '#E6E0D4'),
                        'gold', COALESCE(tokens #>> '{colors,primary}', '#9A8154'),
                        'goldBright', COALESCE(tokens #>> '{colors,primaryOnDark}', '#B59A6A'),
                        'text', COALESCE(tokens #>> '{colors,textPrimary}', '#1A1815'),
                        'muted', COALESCE(tokens #>> '{colors,textMuted}', '#6E6860'),
                        'line', COALESCE(tokens #>> '{colors,borderSubtle}', '#DCD6CB')
                      )
                      WHERE colors IS NULL;
                    END IF;

                    UPDATE brand_kits SET colors = '{}'::jsonb WHERE colors IS NULL;
                    DELETE FROM brand_kits WHERE business_id IS NULL;

                    ALTER TABLE brand_kits ALTER COLUMN business_id SET NOT NULL;
                    ALTER TABLE brand_kits ALTER COLUMN colors SET NOT NULL;
                  END IF;

                  IF NOT EXISTS (
                    SELECT 1 FROM pg_class c
                    JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname = 'public' AND c.relname = 'IX_brand_kits_business_id'
                  ) THEN
                    CREATE UNIQUE INDEX "IX_brand_kits_business_id" ON brand_kits (business_id);
                  END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS "IX_brand_kits_business_id";
                ALTER TABLE brand_kits DROP COLUMN IF EXISTS business_id;
                ALTER TABLE brand_kits DROP COLUMN IF EXISTS colors;
                """);
        }
    }
}
