using LabelsMis.Tools.Importers;

if (args.Length < 2)
{
    Console.WriteLine("Usage: LabelsMis.Tools <importer> <csv-path> [--connection <conn>]");
    Console.WriteLine("Importers: customers, stocks, products, opening-ar");
    return 1;
}

var importer = args[0].ToLowerInvariant();
var csvPath = args[1];
var connection = args.Length >= 4 && args[2] == "--connection" ? args[3] : null;
var actorId = Guid.Parse("00000000-0000-0000-0000-000000000099");

if (!File.Exists(csvPath))
{
    Console.Error.WriteLine($"File not found: {csvPath}");
    return 1;
}

ImportResult result = importer switch
{
    "customers" => await CustomerImporter.ImportAsync(csvPath, actorId, connection),
    "stocks" => await StockImporter.ImportAsync(csvPath, actorId, connection),
    "products" => await ProductImporter.ImportAsync(csvPath, actorId, connection),
    "opening-ar" => await OpeningBalanceImporter.ImportAsync(csvPath, actorId, connection),
    _ => throw new InvalidOperationException($"Unknown importer: {importer}")
};

Console.WriteLine($"Imported: {result.SuccessCount}, Skipped: {result.SkippedCount}");
foreach (var error in result.Errors)
{
    Console.Error.WriteLine(error);
}

return result.Errors.Count > 0 ? 1 : 0;
