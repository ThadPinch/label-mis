using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabelsMis.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMasterData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customer",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Terms = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TaxExempt = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultMarkupPct = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SalesRepId = table.Column<Guid>(type: "uuid", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customer", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Customer_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Customer_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinishingOperation",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    DefaultSetupMinutes = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    DefaultRunSpeedFpm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    EquipmentName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CostPerHour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinishingOperation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinishingOperation_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FinishingOperation_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Ink",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    InkSet = table.Column<int>(type: "integer", nullable: false),
                    ClickRatePer1000 = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsWhite = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ink", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ink_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Ink_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Press",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PressType = table.Column<int>(type: "integer", nullable: false),
                    WebWidthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxRepeatIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    MinRepeatIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    MaxColors = table.Column<int>(type: "integer", nullable: false),
                    SpeedFpm = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    SetupMinutes = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    CostPerHour = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    IsClickBased = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Press", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Press_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Press_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Supplier",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Terms = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DefaultLeadTimeDays = table.Column<int>(type: "integer", nullable: false),
                    AccountNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supplier", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Supplier_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Supplier_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Address",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddressType = table.Column<int>(type: "integer", nullable: false),
                    Street1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Street2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    State = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Zip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Address", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Address_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Address_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Address_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Contact",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contact_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contact_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contact_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Die",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DieType = table.Column<int>(type: "integer", nullable: false),
                    Shape = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LabelAcrossIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    LabelAroundIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    CornerRadiusIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    GutterAcrossIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    GutterAroundIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    LabelsAcross = table.Column<int>(type: "integer", nullable: false),
                    LabelsAround = table.Column<int>(type: "integer", nullable: false),
                    RepeatLengthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    WebWidthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupplierPartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UsageCount = table.Column<int>(type: "integer", nullable: false),
                    RetiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Die", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Die_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Die_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Die_Customer_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "public",
                        principalTable: "Customer",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Die_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "public",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Stock",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FaceMaterial = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Adhesive = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Liner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TotalCaliperMil = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    WidthIn = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierPartNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CostPerMsi = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    MinOrderQtyLf = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stock", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Stock_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stock_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Stock_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "public",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierContact",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierContact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierContact_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierContact_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierContact_Supplier_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "public",
                        principalTable: "Supplier",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DieUsage",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DieId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: true),
                    UsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedById = table.Column<Guid>(type: "uuid", nullable: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DieUsage", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DieUsage_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DieUsage_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DieUsage_AspNetUsers_UsedById",
                        column: x => x.UsedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DieUsage_Die_DieId",
                        column: x => x.DieId,
                        principalSchema: "public",
                        principalTable: "Die",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockCostHistory",
                schema: "public",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StockId = table.Column<Guid>(type: "uuid", nullable: false),
                    CostPerMsi = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedById = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false, defaultValue: new Guid("00000000-0000-0000-0000-000000000001")),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ModifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCostHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCostHistory_AspNetUsers_CreatedById",
                        column: x => x.CreatedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCostHistory_AspNetUsers_ModifiedById",
                        column: x => x.ModifiedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCostHistory_AspNetUsers_RecordedById",
                        column: x => x.RecordedById,
                        principalSchema: "public",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCostHistory_Stock_StockId",
                        column: x => x.StockId,
                        principalSchema: "public",
                        principalTable: "Stock",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Address_CreatedById",
                schema: "public",
                table: "Address",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Address_CustomerId",
                schema: "public",
                table: "Address",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Address_ModifiedById",
                schema: "public",
                table: "Address",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_CreatedById",
                schema: "public",
                table: "Contact",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_CustomerId",
                schema: "public",
                table: "Contact",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contact_ModifiedById",
                schema: "public",
                table: "Contact",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Code",
                schema: "public",
                table: "Customer",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customer_CreatedById",
                schema: "public",
                table: "Customer",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_ModifiedById",
                schema: "public",
                table: "Customer",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Customer_Name",
                schema: "public",
                table: "Customer",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Die_CreatedById",
                schema: "public",
                table: "Die",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Die_CustomerId",
                schema: "public",
                table: "Die",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Die_ModifiedById",
                schema: "public",
                table: "Die",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Die_SupplierId",
                schema: "public",
                table: "Die",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_DieUsage_CreatedById",
                schema: "public",
                table: "DieUsage",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_DieUsage_DieId",
                schema: "public",
                table: "DieUsage",
                column: "DieId");

            migrationBuilder.CreateIndex(
                name: "IX_DieUsage_ModifiedById",
                schema: "public",
                table: "DieUsage",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_DieUsage_UsedById",
                schema: "public",
                table: "DieUsage",
                column: "UsedById");

            migrationBuilder.CreateIndex(
                name: "IX_FinishingOperation_Code",
                schema: "public",
                table: "FinishingOperation",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinishingOperation_CreatedById",
                schema: "public",
                table: "FinishingOperation",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_FinishingOperation_ModifiedById",
                schema: "public",
                table: "FinishingOperation",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Ink_Code",
                schema: "public",
                table: "Ink",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ink_CreatedById",
                schema: "public",
                table: "Ink",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Ink_ModifiedById",
                schema: "public",
                table: "Ink",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Press_Code",
                schema: "public",
                table: "Press",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Press_CreatedById",
                schema: "public",
                table: "Press",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Press_ModifiedById",
                schema: "public",
                table: "Press",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_Code",
                schema: "public",
                table: "Stock",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stock_CreatedById",
                schema: "public",
                table: "Stock",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_ModifiedById",
                schema: "public",
                table: "Stock",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_Stock_SupplierId",
                schema: "public",
                table: "Stock",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostHistory_CreatedById",
                schema: "public",
                table: "StockCostHistory",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostHistory_ModifiedById",
                schema: "public",
                table: "StockCostHistory",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostHistory_RecordedById",
                schema: "public",
                table: "StockCostHistory",
                column: "RecordedById");

            migrationBuilder.CreateIndex(
                name: "IX_StockCostHistory_StockId",
                schema: "public",
                table: "StockCostHistory",
                column: "StockId");

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_Code",
                schema: "public",
                table: "Supplier",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_CreatedById",
                schema: "public",
                table: "Supplier",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Supplier_ModifiedById",
                schema: "public",
                table: "Supplier",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierContact_CreatedById",
                schema: "public",
                table: "SupplierContact",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierContact_ModifiedById",
                schema: "public",
                table: "SupplierContact",
                column: "ModifiedById");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierContact_SupplierId",
                schema: "public",
                table: "SupplierContact",
                column: "SupplierId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Address",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Contact",
                schema: "public");

            migrationBuilder.DropTable(
                name: "DieUsage",
                schema: "public");

            migrationBuilder.DropTable(
                name: "FinishingOperation",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Ink",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Press",
                schema: "public");

            migrationBuilder.DropTable(
                name: "StockCostHistory",
                schema: "public");

            migrationBuilder.DropTable(
                name: "SupplierContact",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Die",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Stock",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Customer",
                schema: "public");

            migrationBuilder.DropTable(
                name: "Supplier",
                schema: "public");
        }
    }
}
