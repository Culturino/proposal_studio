using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProposalStudio.Data;

#nullable disable

namespace ProposalStudio.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260831234000_DropCategorySortOrder")]
    public partial class DropCategorySortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sort_order",
                table: "product_categories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sort_order",
                table: "product_categories",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
