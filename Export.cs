using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeChatCleaner {

 /// Writes the selected rows of the file table into a spreadsheet.
 public static class TableExport {
  public static void WriteCsv(string path, List<string> header, List<object[]> rows) {
   using (var sw = new StreamWriter(path, false, new UTF8Encoding(true))) {
    sw.WriteLine(Join(header.ToArray()));
    foreach (var r in rows) sw.WriteLine(Join(r));
   }
  }

  static string Join(object[] cells) {
   var parts = new List<string>();
   foreach (var v in cells) {
    string t = Convert.ToString(v);
    if (t == null) t = "";
    t = t.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
    parts.Add("\"" + t + "\"");
   }
   return string.Join(",", parts.ToArray());
  }

  /// Writes a real .xlsx workbook (spreadsheetml) with the values inline.
  public static void WriteXlsx(string path, List<string> header, List<object[]> rows) {
   using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
   using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create)) {
    Entry(zip, "[Content_Types].xml",
     "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
     "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
     "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
     "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
     "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
     "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
     "</Types>");
    Entry(zip, "_rels/.rels",
     "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
     "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
     "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
     "</Relationships>");
    Entry(zip, "xl/workbook.xml",
     "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
     "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
     "<sheets><sheet name=\"" + Xml("文件明细") + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
    Entry(zip, "xl/_rels/workbook.xml.rels",
     "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
     "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
     "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
     "</Relationships>");

    var sb = new StringBuilder();
    sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
    sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
    int rowNum = 1;
    sb.Append("<row r=\"" + rowNum + "\">");
    for (int c = 0; c < header.Count; c++)
     sb.Append("<c r=\"" + CellRef(c, rowNum) + "\" t=\"inlineStr\"><is><t>" + Xml(header[c]) + "</t></is></c>");
    sb.Append("</row>");
    foreach (var r in rows) {
     rowNum++;
     sb.Append("<row r=\"" + rowNum + "\">");
     for (int c = 0; c < r.Length; c++) {
      if (r[c] is long || r[c] is int)
       sb.Append("<c r=\"" + CellRef(c, rowNum) + "\"><v>" + Convert.ToString(r[c]) + "</v></c>");
      else
       sb.Append("<c r=\"" + CellRef(c, rowNum) + "\" t=\"inlineStr\"><is><t>" + Xml(Convert.ToString(r[c])) + "</t></is></c>");
     }
     sb.Append("</row>");
    }
    sb.Append("</sheetData></worksheet>");
    Entry(zip, "xl/worksheets/sheet1.xml", sb.ToString());
   }
  }

  static void Entry(System.IO.Compression.ZipArchive zip, string name, string content) {
   var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Optimal);
   using (var stream = entry.Open())
   using (var writer = new StreamWriter(stream, new UTF8Encoding(false))) writer.Write(content);
  }

  static string CellRef(int col, int row) {
   string name = "";
   int c = col + 1;
   while (c > 0) { int m = (c - 1) % 26; name = (char)('A' + m) + name; c = (c - 1) / 26; }
   return name + row;
  }

  static string Xml(string s) {
   if (s == null) return "";
   return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
  }
 }
}
