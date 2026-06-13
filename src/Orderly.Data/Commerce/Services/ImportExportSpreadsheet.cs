using System.Text;
using ClosedXML.Excel;

namespace Orderly.Data.Commerce.Services;

/// <summary>
/// A parsed tabular file: a header row plus the data rows beneath it. Each data row is aligned to
/// the header by position; a row shorter than the header is padded with empty cells on read.
/// </summary>
internal sealed record SpreadsheetData(IReadOnlyList<string> Header, IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// Raised when a file cannot be parsed as a valid CSV/XLSX document. The Import_Export_Service
/// translates it into a whole-file rejection with <c>ImportRejected</c> (Req 9.3).
/// </summary>
internal sealed class SpreadsheetFormatException : Exception
{
    public SpreadsheetFormatException(string message) : base(message) { }

    public SpreadsheetFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Minimal CSV/XLSX (de)serialization used only by the Import_Export_Service. CSV uses RFC 4180
/// style quoting; XLSX uses the ClosedXML package already referenced by this project. Reading a
/// malformed file throws <see cref="SpreadsheetFormatException"/> so the service can reject the whole
/// file before any data is committed (Req 9.3).
/// </summary>
internal static class ImportExportSpreadsheet
{
    private const string WorksheetName = "Data";

    // ----- Writing -----

    public static byte[] Write(ImportExportFormatKind kind, IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
        => kind == ImportExportFormatKind.Csv ? WriteCsv(header, rows) : WriteXlsx(header, rows);

    private static byte[] WriteCsv(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        builder.Append(string.Join(",", header.Select(EscapeCsvField)));
        builder.Append("\r\n");
        foreach (IReadOnlyList<string> row in rows)
        {
            builder.Append(string.Join(",", row.Select(EscapeCsvField)));
            builder.Append("\r\n");
        }

        // UTF-8 with BOM so spreadsheet apps open Chinese text correctly.
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(builder.ToString());
    }

    private static byte[] WriteXlsx(IReadOnlyList<string> header, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        using var workbook = new XLWorkbook();
        IXLWorksheet worksheet = workbook.Worksheets.Add(WorksheetName);

        for (int column = 0; column < header.Count; column++)
        {
            worksheet.Cell(1, column + 1).Value = header[column];
        }

        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            IReadOnlyList<string> row = rows[rowIndex];
            for (int column = 0; column < header.Count; column++)
            {
                string value = column < row.Count ? row[column] : string.Empty;
                // Persist every cell as text so values round-trip exactly (no numeric coercion).
                worksheet.Cell(rowIndex + 2, column + 1).SetValue(value);
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    // ----- Reading -----

    public static SpreadsheetData Read(ImportExportFormatKind kind, byte[] content)
        => kind == ImportExportFormatKind.Csv ? ReadCsv(content) : ReadXlsx(content);

    private static SpreadsheetData ReadCsv(byte[] content)
    {
        if (content is null || content.Length == 0)
        {
            throw new SpreadsheetFormatException("文件为空，无法作为 CSV 解析。");
        }

        string text;
        try
        {
            text = DecodeUtf8(content);
        }
        catch (Exception ex)
        {
            throw new SpreadsheetFormatException("文件不是有效的 UTF-8 文本，无法作为 CSV 解析。", ex);
        }

        List<List<string>> records = ParseCsv(text);
        if (records.Count == 0)
        {
            throw new SpreadsheetFormatException("CSV 文件缺少表头行。");
        }

        List<string> header = records[0].Select(cell => cell.Trim()).ToList();
        var rows = new List<IReadOnlyList<string>>(records.Count - 1);
        for (int i = 1; i < records.Count; i++)
        {
            rows.Add(records[i]);
        }

        return new SpreadsheetData(header, rows);
    }

    private static SpreadsheetData ReadXlsx(byte[] content)
    {
        if (content is null || content.Length == 0)
        {
            throw new SpreadsheetFormatException("文件为空，无法作为 XLSX 解析。");
        }

        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(content, writable: false));
        }
        catch (Exception ex)
        {
            throw new SpreadsheetFormatException("文件不是有效的 XLSX 工作簿。", ex);
        }

        using (workbook)
        {
            IXLWorksheet? worksheet = workbook.Worksheets.FirstOrDefault();
            if (worksheet is null)
            {
                throw new SpreadsheetFormatException("XLSX 工作簿没有任何工作表。");
            }

            IXLRow? headerRow = worksheet.FirstRowUsed();
            if (headerRow is null)
            {
                throw new SpreadsheetFormatException("XLSX 工作簿缺少表头行。");
            }

            int lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
            if (lastColumn == 0)
            {
                throw new SpreadsheetFormatException("XLSX 表头行为空。");
            }

            var header = new List<string>(lastColumn);
            for (int column = 1; column <= lastColumn; column++)
            {
                header.Add(headerRow.Cell(column).GetString().Trim());
            }

            var rows = new List<IReadOnlyList<string>>();
            int lastRow = worksheet.LastRowUsed()?.RowNumber() ?? headerRow.RowNumber();
            for (int rowNumber = headerRow.RowNumber() + 1; rowNumber <= lastRow; rowNumber++)
            {
                IXLRow row = worksheet.Row(rowNumber);
                if (row.IsEmpty())
                {
                    continue;
                }

                var cells = new List<string>(lastColumn);
                for (int column = 1; column <= lastColumn; column++)
                {
                    cells.Add(row.Cell(column).GetString());
                }

                rows.Add(cells);
            }

            return new SpreadsheetData(header, rows);
        }
    }

    // ----- CSV primitives -----

    private static string DecodeUtf8(byte[] content)
    {
        // Strip a UTF-8 BOM if present, then decode strictly so invalid byte sequences are rejected.
        int offset = content.Length >= 3 && content[0] == 0xEF && content[1] == 0xBB && content[2] == 0xBF ? 3 : 0;
        var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        return strict.GetString(content, offset, content.Length - offset);
    }

    private static string EscapeCsvField(string? value)
    {
        string field = value ?? string.Empty;
        bool mustQuote = field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r');
        if (!mustQuote)
        {
            return field;
        }

        return "\"" + field.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// Parses RFC 4180 style CSV text into records of fields, honoring quoted fields that may contain
    /// commas, quotes (escaped as <c>""</c>), and embedded newlines. A trailing empty line is ignored.
    /// </summary>
    private static List<List<string>> ParseCsv(string text)
    {
        var records = new List<List<string>>();
        var currentRecord = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool fieldStarted = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    fieldStarted = true;
                    break;
                case ',':
                    currentRecord.Add(field.ToString());
                    field.Clear();
                    fieldStarted = true;
                    break;
                case '\r':
                    // Treat '\r', '\n', and '\r\n' all as a single record terminator.
                    EndRecord(records, currentRecord, field, ref fieldStarted);
                    currentRecord = new List<string>();
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    break;
                case '\n':
                    EndRecord(records, currentRecord, field, ref fieldStarted);
                    currentRecord = new List<string>();
                    break;
                default:
                    field.Append(c);
                    fieldStarted = true;
                    break;
            }
        }

        // Flush the final field/record if the file did not end with a newline.
        if (fieldStarted || field.Length > 0 || currentRecord.Count > 0)
        {
            currentRecord.Add(field.ToString());
            records.Add(currentRecord);
        }

        return records;
    }

    private static void EndRecord(List<List<string>> records, List<string> currentRecord, StringBuilder field, ref bool fieldStarted)
    {
        currentRecord.Add(field.ToString());
        field.Clear();
        fieldStarted = false;
        records.Add(currentRecord);
    }
}

/// <summary>Internal mirror of the public format enum, kept independent of the Core assembly's enum ordering.</summary>
internal enum ImportExportFormatKind
{
    Csv,
    Xlsx,
}
