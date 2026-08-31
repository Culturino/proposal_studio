using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProposalStudio.Data;

#nullable disable

namespace ProposalStudio.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260831215200_BusinessContact")]
    public partial class BusinessContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instagram",
                table: "businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "businesses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "businesses",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE businesses
                SET phone = '+971 4 295 2131',
                    website = 'houseofpianos-uae.com',
                    instagram = '@houseofpianosuae',
                    address = 'Showroom 41, Street A, Al Quoz 1 (Opposite Al Serkal Avenue) · Dubai, United Arab Emirates'
                WHERE slug = 'house-of-pianos';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "instagram",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "businesses");

            migrationBuilder.DropColumn(
                name: "website",
                table: "businesses");
        }
    }
}
