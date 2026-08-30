using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProposalStudio.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrandKitLogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "logo_file",
                table: "brand_kits",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "logo_file",
                table: "brand_kits");
        }
    }
}
