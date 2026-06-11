using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml;
using ClosedXML.Excel;
using Orderly.Core.Models;
using Orderly.Data.Sqlite;

namespace Orderly.Data.Services;

public sealed partial class CloudInventoryWorkspaceService
{
    private const long MaxInventoryWorkbookBytes = 10 * 1024 * 1024;
    private const long MaxInventoryWorkbookExpandedBytes = 32 * 1024 * 1024;
    private const long MaxInventoryWorkbookEntryBytes = 12 * 1024 * 1024;
    private const long MaxInventoryWorkbookWorksheetXmlBytes = 8 * 1024 * 1024;
    private const long MaxInventoryWorkbookSharedStringsBytes = 8 * 1024 * 1024;
    private const long MaxInventoryWorkbookStylesBytes = 2 * 1024 * 1024;
    private const int MaxInventoryWorkbookZipEntries = 256;
    private const int MaxInventoryWorkbookWorksheetCount = 8;
    private const int MaxInventoryWorkbookCompressionRatio = 100;
    private const int MaxInventoryWorkbookDataRows = 500;
    private const int MaxInventoryWorkbookHeaderCharacters = 128;
    private const int MaxInventoryWorkbookCellCharacters = 1024;
    private const int WorkbookBackupRetentionCount = 5;
    private const string WorkbookBackupTimestampFormat = "yyyyMMdd-HHmmss";

    private static List<InventoryWorkbookRow> LoadWorkbookRows(string workbookPath, out List<InventoryImportRowError> errors)
    {
        using var workbookStream = OpenSafeWorkbookReadStream(workbookPath);
        EnsureWorkbookPackageIsSafe(workbookStream);
        using var workbook = new XLWorkbook(workbookStream);
        var worksheet = workbook.Worksheets.FirstOrDefault(static sheet => string.Equals(sheet.Name, "Inventory", StringComparison.OrdinalIgnoreCase))
            ?? workbook.Worksheets.FirstOrDefault()
            ?? throw new InvalidOperationException("Excel 中没有可读取的工作表。");

        var headerRow = worksheet.FirstRowUsed() ?? throw new InvalidOperationException("Excel 缺少表头。");
        var headerMap = BuildHeaderMap(headerRow);

        EnsureRequiredHeader(headerMap, MaterialCodeAliases, "物料编码");
        EnsureRequiredHeader(headerMap, MaterialNameAliases, "材料名称");

        var rows = new List<InventoryWorkbookRow>();
        errors = new List<InventoryImportRowError>();

        var lastRowNumber = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
        if (lastRowNumber - headerRow.RowNumber() > MaxInventoryWorkbookDataRows)
        {
            throw new InvalidOperationException($"Excel 数据行超过单次导入上限（{MaxInventoryWorkbookDataRows} 行）。");
        }

        var processedDataRows = 0;
        for (var rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                continue;
            }

            var materialCode = ReadCellText(row, headerMap, MaterialCodeAliases);
            var materialName = ReadCellText(row, headerMap, MaterialNameAliases);
            if (string.IsNullOrWhiteSpace(materialCode) && string.IsNullOrWhiteSpace(materialName))
            {
                continue;
            }

            processedDataRows++;
            if (processedDataRows > MaxInventoryWorkbookDataRows)
            {
                throw new InvalidOperationException($"Excel 数据行超过单次导入上限（{MaxInventoryWorkbookDataRows} 行）。");
            }

            if (string.IsNullOrWhiteSpace(materialCode))
            {
                errors.Add(new InventoryImportRowError
                {
                    RowNumber = rowNumber,
                    Message = "缺少物料编码。"
                });
                continue;
            }

            if (string.IsNullOrWhiteSpace(materialName))
            {
                errors.Add(new InventoryImportRowError
                {
                    RowNumber = rowNumber,
                    Message = "缺少材料名称。"
                });
                continue;
            }

            try
            {
                rows.Add(new InventoryWorkbookRow
                {
                    RowNumber = rowNumber,
                    MaterialCode = materialCode,
                    MaterialName = materialName,
                    Category = ReadCellText(row, headerMap, CategoryAliases),
                    StockUnit = string.IsNullOrWhiteSpace(ReadCellText(row, headerMap, StockUnitAliases)) ? "件" : ReadCellText(row, headerMap, StockUnitAliases),
                    CurrentStockQty = ReadCellDecimal(row, headerMap, CurrentStockAliases),
                    SafeStockQty = ReadCellDecimal(row, headerMap, SafeStockAliases),
                    Sold7dQty = ReadCellDecimal(row, headerMap, Sold7dAliases),
                    Sold30dQty = ReadCellDecimal(row, headerMap, Sold30dAliases),
                    UnitCost = ReadCellDecimal(row, headerMap, UnitCostAliases),
                    BraceletSalePrice = ReadCellDecimal(row, headerMap, BraceletSalePriceAliases),
                    BraceletCostPrice = ReadCellDecimal(row, headerMap, BraceletCostPriceAliases),
                    SupplierName = ReadCellText(row, headerMap, SupplierAliases),
                    Remark = ReadCellText(row, headerMap, RemarkAliases),
                    Enabled = ReadCellBool(row, headerMap, EnabledAliases, true),
                    LastRestockedAt = ReadCellNullableDateTime(row, headerMap, LastRestockedAliases),
                    SourceUpdatedAt = ReadCellNullableDateTime(row, headerMap, SourceUpdatedAliases)
                });
            }
            catch (InvalidOperationException ex)
            {
                errors.Add(new InventoryImportRowError
                {
                    RowNumber = rowNumber,
                    Message = ex.Message
                });
            }
        }

        return rows;
    }

    private static void WriteWorkbook(string workbookPath, IReadOnlyList<InventoryWorkbookRow> rows)
    {
        var safeWorkbookPath = EnsureWorkbookWriteTargetIsSafe(workbookPath);
        var directory = Path.GetDirectoryName(safeWorkbookPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            EnsureWorkbookDirectoryPathIsSafe(directory, "Excel 导出目录");
            Directory.CreateDirectory(directory);
            EnsureWorkbookDirectoryPathIsSafe(directory, "Excel 导出目录");
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.AddWorksheet("Inventory");
        for (var column = 0; column < ExportHeaders.Length; column++)
        {
            worksheet.Cell(1, column + 1).Value = ExportHeaders[column];
            worksheet.Cell(1, column + 1).Style.Font.Bold = true;
        }

        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            var excelRow = index + 2;
            worksheet.Cell(excelRow, 1).Value = SafeExcelText(row.MaterialCode);
            worksheet.Cell(excelRow, 2).Value = SafeExcelText(row.MaterialName);
            worksheet.Cell(excelRow, 3).Value = SafeExcelText(row.Category);
            worksheet.Cell(excelRow, 4).Value = SafeExcelText(row.StockUnit);
            worksheet.Cell(excelRow, 5).Value = row.CurrentStockQty;
            worksheet.Cell(excelRow, 6).Value = row.SafeStockQty;
            worksheet.Cell(excelRow, 7).Value = row.Sold7dQty;
            worksheet.Cell(excelRow, 8).Value = row.Sold30dQty;
            worksheet.Cell(excelRow, 9).Value = row.UnitCost;
            worksheet.Cell(excelRow, 10).Value = row.BraceletSalePrice;
            worksheet.Cell(excelRow, 11).Value = row.BraceletCostPrice;
            worksheet.Cell(excelRow, 12).Value = SafeExcelText(row.SupplierName);
            worksheet.Cell(excelRow, 13).Value = SafeExcelText(row.Remark);
            worksheet.Cell(excelRow, 14).Value = SafeExcelText(row.Enabled ? "是" : "否");
            worksheet.Cell(excelRow, 15).Value = SafeExcelText(row.LastRestockedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
            worksheet.Cell(excelRow, 16).Value = SafeExcelText(row.SourceUpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? string.Empty);
        }

        worksheet.Columns().AdjustToContents();
        SaveWorkbookAtomically(workbook, safeWorkbookPath);
    }

    private static string SafeExcelText(string? value)
    {
        var text = value ?? string.Empty;
        var firstContent = text.FirstOrDefault(static ch => !char.IsWhiteSpace(ch));
        return firstContent is '=' or '+' or '-' or '@' ? "'" + text : text;
    }

    private static void SaveWorkbookAtomically(XLWorkbook workbook, string workbookPath)
    {
        var directory = Path.GetDirectoryName(workbookPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        EnsureWorkbookDirectoryPathIsSafe(directory, "Excel 导出目录");
        var tempPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(workbookPath)}.{Guid.NewGuid():N}.tmp.xlsx");

        try
        {
            if (LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                throw new InvalidOperationException("Excel 临时导出文件不能是链接文件。");
            }

            workbook.SaveAs(tempPath);
            LocalDataFileSecurity.HardenFile(tempPath);

            if (LocalDataFileSecurity.IsReparsePoint(workbookPath))
            {
                throw new InvalidOperationException("Excel 导出文件不能是链接文件。");
            }

            File.Move(tempPath, workbookPath, overwrite: true);

            if (LocalDataFileSecurity.IsReparsePoint(workbookPath))
            {
                throw new InvalidOperationException("Excel 导出文件不能是链接文件。");
            }

            LocalDataFileSecurity.HardenFile(workbookPath);
        }
        catch
        {
            DeleteTemporaryWorkbookFile(tempPath);
            throw;
        }
    }

    private static void DeleteTemporaryWorkbookFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath) && !LocalDataFileSecurity.IsReparsePoint(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string CreateWorkbookBackup(string workbookPath)
    {
        if (string.IsNullOrWhiteSpace(workbookPath) || !File.Exists(workbookPath))
        {
            return string.Empty;
        }

        var sourceFile = GetSafeExistingWorkbookFileInfo(workbookPath);
        var safeWorkbookPath = sourceFile.FullName;
        var directory = sourceFile.DirectoryName ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(safeWorkbookPath);
        var extension = Path.GetExtension(safeWorkbookPath);
        var backupPath = Path.Combine(directory, $"{fileName}.{DateTime.Now.ToString(WorkbookBackupTimestampFormat, CultureInfo.InvariantCulture)}.bak{extension}");
        if (File.Exists(backupPath) || LocalDataFileSecurity.IsReparsePoint(backupPath))
        {
            throw new InvalidOperationException("Excel 备份文件已存在或是链接文件。");
        }

        EnsureWorkbookDirectoryPathIsSafe(directory, "Excel 备份目录");
        if (LocalDataFileSecurity.IsReparsePoint(backupPath))
        {
            throw new InvalidOperationException("Excel 备份文件不能是链接文件。");
        }

        File.Copy(safeWorkbookPath, backupPath, overwrite: false);
        LocalDataFileSecurity.HardenFile(backupPath);
        PruneWorkbookBackups(directory, fileName, extension, backupPath);
        return backupPath;
    }

    private static void PruneWorkbookBackups(string directory, string fileName, string extension, string currentBackupPath)
    {
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(extension))
        {
            return;
        }

        try
        {
            var directoryInfo = new DirectoryInfo(directory);
            if (!directoryInfo.Exists)
            {
                return;
            }

            var fullDirectory = directoryInfo.FullName;
            var currentFullPath = Path.GetFullPath(currentBackupPath);
            var prefix = fileName + ".";
            var suffix = ".bak" + extension;

            var backups = directoryInfo
                .EnumerateFiles($"{fileName}.*.bak{extension}", SearchOption.TopDirectoryOnly)
                .Select(file => TryCreateWorkbookBackupCandidate(file, fullDirectory, prefix, suffix))
                .Where(static candidate => candidate is not null)
                .Select(static candidate => candidate!.Value)
                .OrderByDescending(static candidate => candidate.CreatedAt)
                .ThenByDescending(static candidate => candidate.FullPath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var expired in backups.Skip(WorkbookBackupRetentionCount))
            {
                if (string.Equals(expired.FullPath, currentFullPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (LocalDataFileSecurity.IsReparsePoint(expired.FullPath))
                {
                    continue;
                }

                File.Delete(expired.FullPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (SystemException)
        {
        }
    }

    private static WorkbookBackupCandidate? TryCreateWorkbookBackupCandidate(
        FileInfo file,
        string expectedDirectory,
        string prefix,
        string suffix)
    {
        var fullPath = file.FullName;
        if (!string.Equals(file.DirectoryName, expectedDirectory, StringComparison.OrdinalIgnoreCase)
            || !file.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || !file.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return null;
        }

        var timestamp = file.Name.Substring(prefix.Length, file.Name.Length - prefix.Length - suffix.Length);
        if (!DateTime.TryParseExact(
                timestamp,
                WorkbookBackupTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var createdAt))
        {
            return null;
        }

        return new WorkbookBackupCandidate(Path.GetFullPath(fullPath), createdAt);
    }

    private readonly record struct WorkbookBackupCandidate(string FullPath, DateTime CreatedAt);

    private static Dictionary<string, int> BuildHeaderMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var normalized = NormalizeHeader(NormalizeWorkbookCellText(
                cell.GetString(),
                "Excel 表头",
                MaxInventoryWorkbookHeaderCharacters));
            if (!string.IsNullOrWhiteSpace(normalized) && !map.ContainsKey(normalized))
            {
                map[normalized] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    private static void EnsureRequiredHeader(Dictionary<string, int> headerMap, IEnumerable<string> aliases, string displayName)
    {
        if (ResolveColumnIndex(headerMap, aliases) > 0)
        {
            return;
        }

        throw new InvalidOperationException($"Excel 缺少必填列：{displayName}。");
    }

    private static int ResolveColumnIndex(Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        foreach (var alias in aliases)
        {
            if (headerMap.TryGetValue(NormalizeHeader(alias), out var columnIndex))
            {
                return columnIndex;
            }
        }

        return 0;
    }

    private static string ReadCellText(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        var columnIndex = ResolveColumnIndex(headerMap, aliases);
        if (columnIndex <= 0)
        {
            return string.Empty;
        }

        return NormalizeWorkbookCellText(
            row.Cell(columnIndex).GetFormattedString(),
            "Excel 单元格",
            MaxInventoryWorkbookCellCharacters);
    }

    private static decimal ReadCellDecimal(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        var text = ReadCellText(row, headerMap, aliases);
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var invariant))
        {
            return invariant;
        }

        if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var current))
        {
            return current;
        }

        throw new InvalidOperationException("无法解析数字。");
    }

    private static bool ReadCellBool(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases, bool defaultValue)
    {
        var text = ReadCellText(row, headerMap, aliases);
        if (string.IsNullOrWhiteSpace(text))
        {
            return defaultValue;
        }

        var normalized = text.Trim().ToLowerInvariant();
        if (normalized is "1" or "true" or "yes" or "y" or "是" or "启用" or "正常")
        {
            return true;
        }

        if (normalized is "0" or "false" or "no" or "n" or "否" or "停用" or "禁用" or "disabled")
        {
            return false;
        }

        return defaultValue;
    }

    private static DateTime? ReadCellNullableDateTime(IXLRow row, Dictionary<string, int> headerMap, IEnumerable<string> aliases)
    {
        var text = ReadCellText(row, headerMap, aliases);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
        {
            return parsed;
        }

        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("无法解析日期时间。");
    }

    private static string NormalizeHeader(string value)
    {
        return string.Concat((value ?? string.Empty)
            .Trim()
            .Where(static ch => !char.IsWhiteSpace(ch) && ch is not '_' and not '-' and not '/' and not '(' and not ')' and not '（' and not '）'))
            .ToLowerInvariant();
    }

    private static string NormalizeWorkbookCellText(string? value, string displayName, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length > maxCharacters)
        {
            throw new InvalidOperationException($"{displayName}超过客户端处理上限（{maxCharacters} 字符）。");
        }

        if (normalized.Any(static ch => char.IsControl(ch) && ch is not '\r' and not '\n' and not '\t'))
        {
            throw new InvalidOperationException($"{displayName}包含不允许的控制字符。");
        }

        return normalized;
    }

    private static bool RowsEqual(InventoryWorkbookRow left, InventoryWorkbookRow right)
    {
        return string.Equals(left.MaterialCode, right.MaterialCode, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeText(left.MaterialName), NormalizeText(right.MaterialName), StringComparison.Ordinal)
            && string.Equals(NormalizeText(left.Category), NormalizeText(right.Category), StringComparison.Ordinal)
            && string.Equals(NormalizeText(left.StockUnit), NormalizeText(right.StockUnit), StringComparison.Ordinal)
            && DecimalEquals(left.CurrentStockQty, right.CurrentStockQty)
            && DecimalEquals(left.SafeStockQty, right.SafeStockQty)
            && DecimalEquals(left.Sold7dQty, right.Sold7dQty)
            && DecimalEquals(left.Sold30dQty, right.Sold30dQty)
            && DecimalEquals(left.UnitCost, right.UnitCost)
            && DecimalEquals(left.BraceletSalePrice, right.BraceletSalePrice)
            && DecimalEquals(left.BraceletCostPrice, right.BraceletCostPrice)
            && string.Equals(NormalizeText(left.SupplierName), NormalizeText(right.SupplierName), StringComparison.Ordinal)
            && string.Equals(NormalizeText(left.Remark), NormalizeText(right.Remark), StringComparison.Ordinal)
            && left.Enabled == right.Enabled
            && NullableDateTimeEquals(left.LastRestockedAt, right.LastRestockedAt)
            && NullableDateTimeEquals(left.SourceUpdatedAt, right.SourceUpdatedAt);
    }

    private static bool DecimalEquals(decimal left, decimal right)
    {
        return decimal.Round(left, 4) == decimal.Round(right, 4);
    }

    private static bool NullableDateTimeEquals(DateTime? left, DateTime? right)
    {
        if (!left.HasValue && !right.HasValue)
        {
            return true;
        }

        if (!left.HasValue || !right.HasValue)
        {
            return false;
        }

        return left.Value.ToUniversalTime().Ticks == right.Value.ToUniversalTime().Ticks;
    }

    private static string NormalizeText(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string ComputeFileHash(string path)
    {
        using var stream = OpenSafeWorkbookReadStream(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }

    private static FileStream OpenSafeWorkbookReadStream(string workbookPath)
    {
        var fileInfo = GetSafeExistingWorkbookFileInfo(workbookPath);
        var stream = new FileStream(
            fileInfo.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        try
        {
            if (stream.Length > MaxInventoryWorkbookBytes)
            {
                throw new InvalidOperationException($"Excel 文件超过导入上限（{MaxInventoryWorkbookBytes / 1024 / 1024}MB）。");
            }

            if (LocalDataFileSecurity.IsReparsePoint(fileInfo.FullName))
            {
                throw new InvalidOperationException("Excel 文件不能是链接文件。");
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void EnsureWorkbookPackageIsSafe(Stream workbookStream)
    {
        if (!workbookStream.CanSeek)
        {
            throw new InvalidOperationException("Excel 文件无法进行安全预检。");
        }

        var originalPosition = workbookStream.Position;
        try
        {
            workbookStream.Position = 0;
            using var archive = new ZipArchive(workbookStream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count == 0)
            {
                throw new InvalidOperationException("Excel 文件结构无效或已损坏。");
            }

            if (archive.Entries.Count > MaxInventoryWorkbookZipEntries)
            {
                throw new InvalidOperationException("Excel 文件内部结构过于复杂，已拒绝导入。");
            }

            var hasContentTypes = false;
            var hasWorkbook = false;
            var worksheetCount = 0;
            long totalExpandedBytes = 0;

            foreach (var entry in archive.Entries)
            {
                var normalizedName = NormalizeWorkbookPackageEntryName(entry.FullName);
                if (string.IsNullOrWhiteSpace(normalizedName))
                {
                    continue;
                }

                if (normalizedName == "[content_types].xml")
                {
                    hasContentTypes = true;
                }
                else if (normalizedName == "xl/workbook.xml")
                {
                    hasWorkbook = true;
                }

                if (entry.Length > MaxInventoryWorkbookEntryBytes)
                {
                    throw new InvalidOperationException("Excel 文件内部条目超过导入处理上限。");
                }

                totalExpandedBytes += entry.Length;
                if (totalExpandedBytes > MaxInventoryWorkbookExpandedBytes)
                {
                    throw new InvalidOperationException("Excel 文件解压后超过导入处理上限。");
                }

                EnsureWorkbookEntryCompressionIsSafe(entry);

                if (normalizedName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                    && normalizedName.EndsWith(".xml", StringComparison.Ordinal))
                {
                    worksheetCount++;
                    if (worksheetCount > MaxInventoryWorkbookWorksheetCount)
                    {
                        throw new InvalidOperationException("Excel 工作表数量超过导入处理上限。");
                    }

                    if (entry.Length > MaxInventoryWorkbookWorksheetXmlBytes)
                    {
                        throw new InvalidOperationException("Excel 工作表内容超过导入处理上限。");
                    }

                    EnsureWorksheetRowCountIsSafe(entry);
                    continue;
                }

                if (normalizedName == "xl/sharedstrings.xml" && entry.Length > MaxInventoryWorkbookSharedStringsBytes)
                {
                    throw new InvalidOperationException("Excel 共享字符串内容超过导入处理上限。");
                }

                if (normalizedName == "xl/styles.xml" && entry.Length > MaxInventoryWorkbookStylesBytes)
                {
                    throw new InvalidOperationException("Excel 样式内容超过导入处理上限。");
                }
            }

            if (!hasContentTypes || !hasWorkbook || worksheetCount == 0)
            {
                throw new InvalidOperationException("Excel 文件结构无效或已损坏。");
            }
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidOperationException("Excel 文件结构无效或已损坏。", ex);
        }
        finally
        {
            workbookStream.Position = originalPosition;
        }
    }

    private static string NormalizeWorkbookPackageEntryName(string entryName)
    {
        var normalized = (entryName ?? string.Empty).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Excel 文件内部路径无效。");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment == "." || segment == ".."))
        {
            throw new InvalidOperationException("Excel 文件内部路径无效。");
        }

        return normalized.ToLowerInvariant();
    }

    private static void EnsureWorkbookEntryCompressionIsSafe(ZipArchiveEntry entry)
    {
        if (entry.CompressedLength <= 0 || entry.Length <= 1024 * 1024)
        {
            return;
        }

        var compressionRatio = entry.Length / (double)entry.CompressedLength;
        if (compressionRatio > MaxInventoryWorkbookCompressionRatio)
        {
            throw new InvalidOperationException("Excel 文件压缩结构异常，已拒绝导入。");
        }
    }

    private static void EnsureWorksheetRowCountIsSafe(ZipArchiveEntry worksheetEntry)
    {
        using var worksheetStream = worksheetEntry.Open();
        using var reader = XmlReader.Create(
            worksheetStream,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                XmlResolver = null
            });

        var rowCount = 0;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || !string.Equals(reader.LocalName, "row", StringComparison.Ordinal))
            {
                continue;
            }

            rowCount++;
            if (rowCount > MaxInventoryWorkbookDataRows + 1)
            {
                throw new InvalidOperationException($"Excel 数据行超过单次导入上限（{MaxInventoryWorkbookDataRows} 行）。");
            }
        }
    }

    private static FileInfo GetSafeExistingWorkbookFileInfo(string workbookPath)
    {
        EnsureWorkbookExtensionIsSafe(workbookPath);
        var fullPath = Path.GetFullPath(workbookPath);
        EnsureWorkbookDirectoryPathIsSafe(Path.GetDirectoryName(fullPath), "Excel 文件目录");
        if (LocalDataFileSecurity.IsReparsePoint(fullPath))
        {
            throw new InvalidOperationException("Excel 文件不能是链接文件。");
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("未找到要导入的 Excel 文件。", fullPath);
        }

        return new FileInfo(fullPath);
    }

    private static string EnsureWorkbookWriteTargetIsSafe(string workbookPath)
    {
        EnsureWorkbookExtensionIsSafe(workbookPath);
        var fullPath = Path.GetFullPath(workbookPath);
        EnsureWorkbookDirectoryPathIsSafe(Path.GetDirectoryName(fullPath), "Excel 导出目录");
        if (LocalDataFileSecurity.IsReparsePoint(fullPath))
        {
            throw new InvalidOperationException("Excel 导出文件不能是链接文件。");
        }

        return fullPath;
    }

    private static void EnsureWorkbookDirectoryPathIsSafe(string? directoryPath, string description)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            return;
        }

        var current = new DirectoryInfo(Path.GetFullPath(directoryPath));
        while (current is not null)
        {
            if (current.Exists && LocalDataFileSecurity.IsReparsePoint(current.FullName))
            {
                throw new InvalidOperationException($"{description}不能位于链接目录。");
            }

            current = current.Parent;
        }
    }

    private static void EnsureWorkbookExtensionIsSafe(string workbookPath)
    {
        if (!string.Equals(Path.GetExtension(workbookPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("库存 Excel 仅支持 .xlsx 工作簿，不支持宏工作簿或旧版 Excel 格式。");
        }
    }
}
