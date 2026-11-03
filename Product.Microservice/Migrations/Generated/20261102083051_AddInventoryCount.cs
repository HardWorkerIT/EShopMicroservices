using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Product.Microservice.Migrations.Generated
{
    /// <inheritdoc />
    public partial class AddInventoryCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InventoryCount",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InventoryCount",
                table: "Products");
        }
    }
}
