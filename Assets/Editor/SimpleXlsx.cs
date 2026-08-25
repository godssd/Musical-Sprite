using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

/// <summary>
/// 极简 xlsx 读取器（零依赖，仅用 System.IO.Compression + System.Xml.Linq）。
/// 读取指定工作表为「行 → 列字符串列表」。
/// 支持：共享字符串（sharedStrings.xml，含富文本 &lt;r&gt;&lt;t&gt;）、数值单元格、inlineStr。
/// 足以解析 Characters.xlsx 这类简单表格；复杂样式/合并单元格不在范围内。
/// </summary>
public static class SimpleXlsx
{
    public static List<List<string>> ReadSheet(string xlsxPath, int sheetIndex = 0)
    {
        if (!File.Exists(xlsxPath))
            throw new FileNotFoundException("xlsx not found: " + xlsxPath);

        using var zip = ZipFile.OpenRead(xlsxPath);

        // 1) 共享字符串
        var shared = new List<string>();
        var ssEntry = zip.GetEntry("xl/sharedStrings.xml");
        if (ssEntry != null)
        {
            using var s = ssEntry.Open();
            var doc = XDocument.Load(s);
            foreach (var si in doc.Descendants().Where(e => e.Name.LocalName == "si"))
            {
                string txt = "";
                foreach (var t in si.Elements().Where(e => e.Name.LocalName == "t"))
                    txt += t.Value;
                shared.Add(txt);
            }
        }

        // 2) 工作簿：sheet 顺序 + r:id
        var sheetRids = new List<string>();
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry != null)
        {
            using var s = wbEntry.Open();
            var doc = XDocument.Load(s);
            XNamespace ns = doc.Root.GetDefaultNamespace();
            XNamespace rns = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            foreach (var sh in doc.Descendants(ns + "sheet"))
            {
                var rid = sh.Attribute(rns + "id")?.Value;
                if (rid != null) sheetRids.Add(rid);
            }
        }

        // 3) 关系：rId → Target
        var relMap = new Dictionary<string, string>();
        var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry != null)
        {
            using var s = relsEntry.Open();
            var rdoc = XDocument.Load(s);
            foreach (var rel in rdoc.Descendants().Where(e => e.Name.LocalName == "Relationship"))
            {
                var id = rel.Attribute("Id")?.Value;
                var target = rel.Attribute("Target")?.Value;
                if (id != null && target != null) relMap[id] = target;
            }
        }

        // 4) 解析目标路径
        var sheetPaths = new List<string>();
        foreach (var rid in sheetRids)
            if (relMap.TryGetValue(rid, out var tgt))
            {
                string p = tgt.StartsWith("xl/") ? tgt : "xl/" + tgt.TrimStart('/');
                sheetPaths.Add(p);
            }

        if (sheetIndex < 0 || sheetIndex >= sheetPaths.Count)
            return new List<List<string>>();

        var sheetEntry = zip.GetEntry(sheetPaths[sheetIndex]);
        if (sheetEntry == null)
            return new List<List<string>>();

        // 5) 读取行/单元格
        using var ss = sheetEntry.Open();
        var sdoc = XDocument.Load(ss);
        XNamespace sns = sdoc.Root.GetDefaultNamespace();
        var rows = new List<List<string>>();
        foreach (var row in sdoc.Descendants(sns + "row"))
        {
            var cells = new Dictionary<int, string>();
            int maxCol = -1;
            foreach (var c in row.Elements(sns + "c"))
            {
                string rAttr = c.Attribute("r")?.Value ?? "";
                int col = ColumnIndex(rAttr);
                string type = c.Attribute("t")?.Value;
                string val = "";
                if (type == "s")
                {
                    var v = c.Element(sns + "v")?.Value;
                    if (v != null && int.TryParse(v, out int idx) && idx >= 0 && idx < shared.Count)
                        val = shared[idx];
                }
                else if (type == "inlineStr")
                {
                    val = c.Element(sns + "is")?.Element(sns + "t")?.Value ?? "";
                }
                else
                {
                    val = c.Element(sns + "v")?.Value ?? "";
                }
                cells[col] = val;
                if (col > maxCol) maxCol = col;
            }
            var list = new List<string>();
            for (int i = 0; i <= maxCol; i++)
                list.Add(cells.TryGetValue(i, out var v) ? v : "");
            rows.Add(list);
        }
        return rows;
    }

    private static int ColumnIndex(string cellRef)
    {
        int col = 0;
        foreach (char ch in cellRef)
        {
            if (char.IsLetter(ch))
                col = col * 26 + (ch - 'A' + 1);
        }
        return col - 1; // 0-based
    }
}
