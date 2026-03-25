using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CIC.BIM.Addin.Tools.Services;

// ══════════════ MODELS ══════════════

public class CadBlockInfo
{
    public string BlockName { get; set; } = "";
    public string LayerName { get; set; } = "";
    public int Count { get; set; }
    public List<XYZ> Positions { get; set; } = new();
    public List<double> Rotations { get; set; } = new();
}

public enum BlockPlacementMode
{
    PlaceOnLevel,
    PlaceOnFace
}

public class BlockCadConfig
{
    public ElementId CadLinkId { get; set; } = ElementId.InvalidElementId;
    public ElementId? RevitLinkInstanceId { get; set; }
    public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
    public double Elevation { get; set; }
    public bool IncludeRotation { get; set; } = true;
    public double RotationOffset { get; set; } = 0; // degrees
    public BlockPlacementMode PlacementMode { get; set; } = BlockPlacementMode.PlaceOnLevel;
    public List<BlockMapping> Mappings { get; set; } = new();
    public string? DwgFilePath { get; set; }
}

public class BlockMapping
{
    public string BlockName { get; set; } = "";
    public ElementId FamilySymbolId { get; set; } = ElementId.InvalidElementId;
}

public class BlockCadResult
{
    public int TotalPlaced { get; set; }
    public Dictionary<string, int> CountByBlock { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public record FamilyTypeItem(ElementId Id, string FamilyName, string TypeName)
{
    public string DisplayName => $"{FamilyName} : {TypeName}";
    public override string ToString() => DisplayName;
}

// ══════════════ SERVICE ══════════════

public static class BlockCadService
{
    private const double MmToFeet = 1.0 / 304.8;

    // ══════════ SCAN CAD LINKS ══════════

    public static List<CadLinkInfo> ScanCadLinks(Document doc)
    {
        return CadAutoDrawService.ScanCadLinks(doc);
    }

    // ══════════ GET DWG FILE PATH ══════════

    /// <summary>
    /// Lấy đường dẫn file DWG từ ImportInstance.
    /// </summary>
    public static string? GetDwgFilePath(Document doc, ElementId importId, ElementId? revitLinkInstId = null)
    {
        try
        {
            Document targetDoc = doc;
            ImportInstance? import = null;

            if (revitLinkInstId != null)
            {
                var linkInst = doc.GetElement(revitLinkInstId) as RevitLinkInstance;
                var linkDoc = linkInst?.GetLinkDocument();
                if (linkDoc != null)
                {
                    targetDoc = linkDoc;
                    import = linkDoc.GetElement(importId) as ImportInstance;
                }
            }
            else
            {
                import = doc.GetElement(importId) as ImportInstance;
            }

            if (import == null) return null;

            var typeId = import.GetTypeId();
            if (typeId == ElementId.InvalidElementId) return null;

            var cadType = targetDoc.GetElement(typeId);
            if (cadType == null) return null;

            // Thử lấy file path từ ExternalFileReference (cho linked CAD)
            try
            {
                var extRef = cadType.GetExternalFileReference();
                if (extRef != null)
                {
                    var modelPath = extRef.GetAbsolutePath();
                    if (modelPath != null)
                    {
                        var path = ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath);
                        if (File.Exists(path))
                            return path;
                    }
                }
            }
            catch { }

            // Fallback: tìm file DWG cùng thư mục với file RVT
            var rvtPath = doc.PathName;
            if (!string.IsNullOrEmpty(rvtPath))
            {
                var dir = Path.GetDirectoryName(rvtPath);
                if (dir != null)
                {
                    var rawName = cadType.Name;
                    
                    // Xử lý các suffix như .dwg (2) hoặc .dwg {q1}
                    // Strip anything after .dwg
                    int dwgIndex = rawName.IndexOf(".dwg", StringComparison.OrdinalIgnoreCase);
                    if (dwgIndex != -1)
                    {
                        rawName = rawName.Substring(0, dwgIndex + 4);
                    }
                    else
                    {
                        rawName += ".dwg";
                    }

                    var dwgPath = Path.Combine(dir, rawName);
                    if (File.Exists(dwgPath))
                        return dwgPath;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BlockCad] GetDwgFilePath error: {ex.Message}");
        }

        return null;
    }

    // ══════════ LOAD FROM CAD EXPORT JSON ══════════

    /// <summary>
    /// Đọc file JSON từ CIC CAD Plugin (CIC_EXPORTBLOCKS).
    /// JSON chứa dynamic block visibility states + positions chính xác.
    /// File JSON: cùng thư mục DWG, tên = ten_dwg_blocks.json
    /// </summary>
    public static List<CadBlockInfo>? TryLoadBlockJson(string? dwgFilePath)
    {
        if (string.IsNullOrEmpty(dwgFilePath)) return null;

        var jsonPath = Path.ChangeExtension(dwgFilePath, "_blocks.json");
        if (!File.Exists(jsonPath))
        {
            Debug.WriteLine($"[BlockCad] No JSON export: {jsonPath}");
            return null;
        }

        try
        {
            var json = File.ReadAllText(jsonPath);

            // Parse each block object using regex (dependency-free .NET 4.8 approach)
            var blockPattern = new System.Text.RegularExpressions.Regex(
                @"\{[^{}]*""name""\s*:\s*""([^""]*?)""[^{}]*""layer""\s*:\s*""([^""]*?)""[^{}]*""positions""\s*:\s*\[(.*?)\]\s*\}",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            var posPattern = new System.Text.RegularExpressions.Regex(
                @"\{[^{}]*""x""\s*:\s*([-\d.]+)[^{}]*""y""\s*:\s*([-\d.]+)[^{}]*""rotation""\s*:\s*([-\d.]+)[^{}]*\}");

            var result = new List<CadBlockInfo>();
            var blockMatches = blockPattern.Matches(json);
            foreach (System.Text.RegularExpressions.Match bm in blockMatches)
            {
                var name = bm.Groups[1].Value;
                if (string.IsNullOrEmpty(name)) continue;
                if (name.Contains("$") || name.StartsWith("*")) continue;

                var layer = bm.Groups[2].Value;
                var positionsJson = bm.Groups[3].Value;

                var info = new CadBlockInfo
                {
                    BlockName = name,
                    LayerName = layer,
                };

                var posMatches = posPattern.Matches(positionsJson);
                foreach (System.Text.RegularExpressions.Match pm in posMatches)
                {
                    var x = double.Parse(pm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var y = double.Parse(pm.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
                    var rot = double.Parse(pm.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

                    // Convert mm → feet (Revit internal units)
                    info.Positions.Add(new XYZ(x / 304.8, y / 304.8, 0));
                    info.Rotations.Add(rot * Math.PI / 180.0);
                    info.Count++;
                }

                result.Add(info);
            }

            Debug.WriteLine($"[BlockCad] Loaded {result.Count} blocks from JSON");
            return result.Where(b => b.Count > 0)
                .OrderBy(b => b.BlockName).ToList();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BlockCad] JSON parse error: {ex.Message}");
            return null;
        }
    }

    // ══════════ SCAN BLOCKS FROM DWG FILE ══════════

    /// <summary>
    /// Parse DWG file trực tiếp bằng ACadSharp để lấy block references.
    /// Đây là cách duy nhất để lấy ĐÚNG tên block definition từ DWG.
    /// </summary>
    public static List<CadBlockInfo> ScanBlocksFromDwg(string dwgFilePath)
    {
        var blockDict = new Dictionary<string, CadBlockInfo>();

        CadDocument? cadDoc = null;
        string? tempCopy = null;
        try
        {
            // ACadSharp mở file với exclusive lock — Revit cũng lock file DWG
            // → Copy sang temp trước rồi đọc bản copy
            tempCopy = Path.Combine(Path.GetTempPath(), $"cic_blockcad_{Guid.NewGuid():N}.dwg");
            File.Copy(dwgFilePath, tempCopy, true);
            cadDoc = DwgReader.Read(tempCopy);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BlockCad] DwgReader error: {ex.Message}");
            return new();
        }
        finally
        {
            try { if (tempCopy != null && File.Exists(tempCopy)) File.Delete(tempCopy); }
            catch { }
        }

        if (cadDoc == null) return new();

        try
        {
            // Duyệt tất cả entities trong Model Space
            ScanInserts(cadDoc.ModelSpace.Entities, blockDict, 0, 0, 1, 1);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BlockCad] ModelSpace scan error: {ex.Message}");
        }

        // 1. Lọc bỏ xref blocks (tên chứa '$' là xref notation trong AutoCAD)
        var xrefKeys = blockDict.Keys.Where(k => k.Contains("$")).ToList();
        foreach (var key in xrefKeys) blockDict.Remove(key);

        // 2. Lọc bỏ *U anonymous blocks (chỉ giữ blocks có tên có nghĩa)
        var anonKeys = blockDict.Keys.Where(k => k.StartsWith("*")).ToList();
        foreach (var key in anonKeys) blockDict.Remove(key);

        return blockDict.Values
            .Where(b => b.Count > 0)
            .OrderBy(b => b.BlockName)
            .ToList();
    }

    /// <summary>
    /// Duyệt entities để tìm INSERT (block references).
    /// </summary>
    private static void ScanInserts(IEnumerable<ACadSharp.Entities.Entity> entities,
        Dictionary<string, CadBlockInfo> blockDict,
        double parentX, double parentY,
        double parentScaleX, double parentScaleY,
        int depth = 0)
    {
        if (depth > 10) return;

        foreach (var entity in entities)
        {
            if (entity is ACadSharp.Entities.Insert insert)
            {
                try
                {
                    var blockName = insert.Block?.Name ?? "";
                    if (string.IsNullOrEmpty(blockName)) continue;

                    // Bỏ qua system/anonymous blocks, giữ lại *U
                    if (blockName.StartsWith("*") && !blockName.StartsWith("*U", StringComparison.OrdinalIgnoreCase)) continue;

                    // Xử lý tên block có dấu ngoặc nhọn
                    if (blockName.Contains("{") && blockName.Contains("}"))
                    {
                        int start = blockName.LastIndexOf("{") + 1;
                        int end = blockName.LastIndexOf("}");
                        if (end > start) blockName = blockName.Substring(start, end - start);
                    }

                    var layerName = insert.Layer?.Name ?? "";
                    var x = parentX + insert.InsertPoint.X * parentScaleX;
                    var y = parentY + insert.InsertPoint.Y * parentScaleY;
                    var rotation = insert.Rotation * Math.PI / 180.0;

                    if (!blockDict.ContainsKey(blockName))
                    {
                        blockDict[blockName] = new CadBlockInfo
                        {
                            BlockName = blockName,
                            LayerName = layerName
                        };
                    }

                    blockDict[blockName].Positions.Add(new XYZ(x * MmToFeet, y * MmToFeet, 0));
                    blockDict[blockName].Rotations.Add(rotation);
                    blockDict[blockName].Count++;

                    // Recurse vào nested blocks
                    if (insert.Block?.Entities != null)
                    {
                        ScanInserts(insert.Block.Entities, blockDict,
                            x, y,
                            parentScaleX * insert.XScale,
                            parentScaleY * insert.YScale,
                            depth + 1);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BlockCad] Insert scan error: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Xử lý dynamic block: nhóm các block chia sẻ cùng tập vị trí
    /// VÀ có tên giống nhau (common prefix ≥ 3 chars) → visibility states.
    /// Gộp thành 1 block đại diện.
    /// </summary>
    private static void MergeDynamicBlockVariants(Dictionary<string, CadBlockInfo> blockDict)
    {
        if (blockDict.Count < 2) return;

        var blocks = blockDict.Values.ToList();
        var toRemove = new HashSet<string>();
        var processed = new HashSet<string>();

        for (int i = 0; i < blocks.Count; i++)
        {
            if (processed.Contains(blocks[i].BlockName)) continue;

            var siblings = new List<CadBlockInfo> { blocks[i] };

            for (int j = i + 1; j < blocks.Count; j++)
            {
                if (processed.Contains(blocks[j].BlockName)) continue;
                if (blocks[j].Count != blocks[i].Count) continue;
                if (blocks[j].Count < 2) continue; // Bỏ qua blocks chỉ có 1 instance

                // Kiểm tra tên có prefix chung ≥ 3 ký tự
                var prefix = GetCommonPrefix(blocks[i].BlockName, blocks[j].BlockName);
                if (prefix.Length < 3) continue;

                // Kiểm tra positions giống nhau (tolerance 0.01 feet ≈ 3mm)
                bool samePositions = true;
                var pos1 = blocks[i].Positions;
                var pos2 = blocks[j].Positions;
                for (int k = 0; k < pos1.Count && k < pos2.Count; k++)
                {
                    if (pos1[k].DistanceTo(pos2[k]) > 0.01)
                    {
                        samePositions = false;
                        break;
                    }
                }

                if (samePositions)
                    siblings.Add(blocks[j]);
            }

            if (siblings.Count > 1)
            {
                // Tìm common prefix cho toàn nhóm
                var commonPrefix = FindCommonPrefix(siblings.Select(s => s.BlockName).ToList());
                commonPrefix = commonPrefix.TrimEnd(' ', '-', '_', '.');
                if (commonPrefix.Length < 3) commonPrefix = siblings[0].BlockName;

                // Đánh dấu tất cả siblings là đã xử lý
                foreach (var sib in siblings)
                {
                    processed.Add(sib.BlockName);
                    toRemove.Add(sib.BlockName);
                }

                // Tạo block đại diện
                toRemove.Remove(commonPrefix);
                blockDict[commonPrefix] = new CadBlockInfo
                {
                    BlockName = commonPrefix,
                    LayerName = siblings[0].LayerName,
                    Count = siblings[0].Count,
                    Positions = new List<XYZ>(siblings[0].Positions),
                    Rotations = new List<double>(siblings[0].Rotations)
                };
            }
        }

        foreach (var name in toRemove)
            blockDict.Remove(name);
    }

    private static string GetCommonPrefix(string a, string b)
    {
        int len = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < len && a[i] == b[i]) i++;
        return a.Substring(0, i);
    }

    private static string FindCommonPrefix(List<string> names)
    {
        if (names.Count == 0) return "";
        var prefix = names[0];
        foreach (var name in names.Skip(1))
        {
            while (!name.StartsWith(prefix) && prefix.Length > 0)
                prefix = prefix.Substring(0, prefix.Length - 1);
        }
        return prefix;
    }


    // ══════════ FALLBACK: SCAN FROM REVIT GEOMETRY ══════════

    /// <summary>
    /// Scanner chính: dùng Revit geometry API.
    /// Revit phân biệt được dynamic block visibility states → mỗi loại có vị trí riêng.
    /// </summary>
    public static List<CadBlockInfo> ScanBlocksFromRevit(Document doc, ElementId cadLinkId)
    {
        var import = doc.GetElement(cadLinkId) as ImportInstance;
        if (import == null) return new();

        var geoElem = import.get_Geometry(new Options
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = true
        });
        if (geoElem == null) return new();

        // Lấy tên DWG file để strip prefix (VD: "25102_TCB_CIC_XD_Lighting_03.dwg.")
        var dwgPrefix = "";
        try
        {
            var cat = import.Category;
            if (cat != null) dwgPrefix = cat.Name + ".";
        }
        catch { }

        var blockDict = new Dictionary<string, CadBlockInfo>();

        try
        {
            foreach (var geoObj in geoElem)
            {
                if (geoObj is GeometryInstance dwgInstance)
                {
                    var dwgTransform = dwgInstance.Transform;
                    try
                    {
                        var symbolGeo = dwgInstance.GetSymbolGeometry();
                        if (symbolGeo != null)
                            ScanGeoRecursive(symbolGeo, doc, blockDict, dwgTransform, 0);
                    }
                    catch { }

                    try
                    {
                        var instGeo = dwgInstance.GetInstanceGeometry();
                        if (instGeo != null)
                            ScanPoints(instGeo, doc, blockDict);
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[BlockCad] Revit geo error: {ex.Message}");
        }

        // Post-processing: clean block names
        var cleanDict = new Dictionary<string, CadBlockInfo>();
        foreach (var kv in blockDict)
        {
            var name = kv.Value.BlockName;

            // Strip DWG file prefix (VD: "25102_TCB_CIC_XD_Lighting_03.dwg.Cong tac 1" → "Cong tac 1")
            if (!string.IsNullOrEmpty(dwgPrefix) && name.StartsWith(dwgPrefix))
                name = name.Substring(dwgPrefix.Length);

            // Lọc bỏ xref subcategories (chứa $)
            if (name.Contains("$")) continue;

            // Lọc bỏ anonymous blocks
            if (name.StartsWith("*")) continue;

            // Lọc bỏ blocks không có tên
            if (string.IsNullOrWhiteSpace(name) || name.StartsWith("Block_")) continue;

            kv.Value.BlockName = name;

            // Merge nếu tên trùng sau khi strip prefix
            if (cleanDict.ContainsKey(name))
            {
                cleanDict[name].Positions.AddRange(kv.Value.Positions);
                cleanDict[name].Rotations.AddRange(kv.Value.Rotations);
                cleanDict[name].Count += kv.Value.Count;
            }
            else
            {
                cleanDict[name] = kv.Value;
            }
        }

        return cleanDict.Values.Where(b => b.Count > 0)
            .OrderBy(b => b.BlockName).ToList();
    }

    private static void ScanGeoRecursive(GeometryElement geoElem, Document doc,
        Dictionary<string, CadBlockInfo> blockDict, Transform parentTransform, int depth)
    {
        if (depth > 10) return;
        foreach (var geoObj in geoElem)
        {
            if (geoObj is GeometryInstance inst)
            {
                try
                {
                    var t = parentTransform.Multiply(inst.Transform);
                    var name = GetStyleName(inst, doc);
                    var sym = inst.GetSymbolGeometry();
                    if (string.IsNullOrEmpty(name) && sym != null)
                        name = GetPrimaryStyleName(sym, doc);
                    if (string.IsNullOrEmpty(name)) name = $"Block_{depth}";

                    if (!blockDict.ContainsKey(name))
                        blockDict[name] = new CadBlockInfo { BlockName = name, LayerName = name };

                    blockDict[name].Positions.Add(t.Origin);
                    blockDict[name].Rotations.Add(Math.Atan2(t.BasisX.Y, t.BasisX.X));
                    blockDict[name].Count++;

                    if (sym != null) ScanGeoRecursive(sym, doc, blockDict, t, depth + 1);
                }
                catch { }
            }
        }
    }

    private static void ScanPoints(GeometryElement geoElem, Document doc,
        Dictionary<string, CadBlockInfo> blockDict)
    {
        foreach (var obj in geoElem)
        {
            if (obj is Autodesk.Revit.DB.Point pt)
            {
                try
                {
                    var name = GetStyleName(pt, doc);
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!blockDict.ContainsKey(name))
                        blockDict[name] = new CadBlockInfo { BlockName = name, LayerName = name };
                    blockDict[name].Positions.Add(pt.Coord);
                    blockDict[name].Rotations.Add(0);
                    blockDict[name].Count++;
                }
                catch { }
            }
        }
    }

    private static string GetStyleName(GeometryObject obj, Document doc)
    {
        try
        {
            var id = obj.GraphicsStyleId;
            if (id == ElementId.InvalidElementId) return "";
            return (doc.GetElement(id) as GraphicsStyle)?.GraphicsStyleCategory?.Name ?? "";
        }
        catch { return ""; }
    }

    private static string GetPrimaryStyleName(GeometryElement geo, Document doc)
    {
        var counts = new Dictionary<string, int>();
        foreach (var o in geo)
        {
            var n = GetStyleName(o, doc);
            if (!string.IsNullOrEmpty(n))
            {
                if (!counts.ContainsKey(n)) counts[n] = 0;
                counts[n]++;
            }
        }
        return counts.Count > 0 ? counts.OrderByDescending(k => k.Value).First().Key : "";
    }

    // ══════════ GET AVAILABLE FAMILIES ══════════

    public static List<FamilyTypeItem> GetMepFamilyTypes(Document doc)
    {
        var cats = new[]
        {
            BuiltInCategory.OST_LightingFixtures, BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_ElectricalEquipment, BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_DataDevices, BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_LightingDevices, BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_SecurityDevices, BuiltInCategory.OST_TelephoneDevices,
            BuiltInCategory.OST_MechanicalEquipment, BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_DuctAccessory, BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_GenericModel
        };

        var result = new List<FamilyTypeItem>();
        foreach (var cat in cats)
        {
            try
            {
                result.AddRange(new FilteredElementCollector(doc)
                    .OfCategory(cat).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
                    .Select(s => new FamilyTypeItem(s.Id, s.FamilyName, s.Name)));
            }
            catch { }
        }
        return result.OrderBy(f => f.FamilyName).ThenBy(f => f.TypeName).ToList();
    }

    // ══════════ EXECUTE: ĐẶT THIẾT BỊ ══════════

    public static BlockCadResult Execute(Document doc, BlockCadConfig config)
    {
        var result = new BlockCadResult();

        // 1. Lấy ImportInstance (có thể từ current doc hoặc Revit Link)
        ImportInstance? import = null;
        Transform linkTransform = Transform.Identity;

        if (config.RevitLinkInstanceId != null)
        {
            var linkInst = doc.GetElement(config.RevitLinkInstanceId) as RevitLinkInstance;
            var linkDoc = linkInst?.GetLinkDocument();
            if (linkDoc != null)
            {
                import = linkDoc.GetElement(config.CadLinkId) as ImportInstance;
                linkTransform = linkInst.GetTotalTransform();
            }
        }
        else
        {
            import = doc.GetElement(config.CadLinkId) as ImportInstance;
        }

        if (import == null) { result.Errors.Add("Không tìm thấy CAD link."); return result; }

        var level = doc.GetElement(config.LevelId) as Level;
        if (level == null) { result.Errors.Add("Level không hợp lệ."); return result; }

        if (config.Mappings.Count == 0) { result.Errors.Add("Chưa cấu hình mapping."); return result; }

        // Scan blocks: 1) JSON export (chính xác), 2) DWG parse, 3) Revit geometry
        List<CadBlockInfo> blocks;
        var jsonBlocks = TryLoadBlockJson(config.DwgFilePath);
        if (jsonBlocks != null && jsonBlocks.Count > 0)
            blocks = jsonBlocks;
        else if (!string.IsNullOrEmpty(config.DwgFilePath) && File.Exists(config.DwgFilePath))
            blocks = ScanBlocksFromDwg(config.DwgFilePath);
        else
            blocks = ScanBlocksFromRevit(import.Document, config.CadLinkId);

        var blockLookup = blocks.ToDictionary(b => b.BlockName, b => b);

        // Lấy transform của ImportInstance trong chính document chứa nó
        Transform importTransform = Transform.Identity;
        try
        {
            var geoElem = import.get_Geometry(new Options());
            if (geoElem != null)
            {
                foreach (var obj in geoElem)
                {
                    if (obj is GeometryInstance gi)
                    {
                        importTransform = gi.Transform;
                        break;
                    }
                }
            }
        }
        catch { }

        // Kết hợp transform: LinkDoc * CadLink
        var finalTransform = linkTransform.Multiply(importTransform);

        var offsetFeet = config.Elevation * MmToFeet;
        var view = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>().FirstOrDefault(v => !v.IsTemplate);

        foreach (var mapping in config.Mappings)
        {
            if (!blockLookup.TryGetValue(mapping.BlockName, out var blockInfo))
            {
                result.Errors.Add($"Block '{mapping.BlockName}' không tìm thấy.");
                continue;
            }

            var symbol = doc.GetElement(mapping.FamilySymbolId) as FamilySymbol;
            if (symbol == null) { result.Errors.Add($"Family not found: '{mapping.BlockName}'."); continue; }

            if (!symbol.IsActive) symbol.Activate();

            var placedCount = 0;
            for (int i = 0; i < blockInfo.Positions.Count; i++)
            {
                try
                {
                    var dwgPos = blockInfo.Positions[i];

                    // Transform tọa độ từ DWG space → Revit model space
                    var revitPos = finalTransform.OfPoint(dwgPos);
                    var placementPoint = new XYZ(revitPos.X, revitPos.Y, level.Elevation + offsetFeet);

                    FamilyInstance? instance = null;

                    if (config.PlacementMode == BlockPlacementMode.PlaceOnFace)
                    {
                        var faceRef = FindNearestFace(doc, placementPoint, 5.0, out var host); // Search within 5 feet
                        if (faceRef != null && host != null)
                        {
                            instance = doc.Create.NewFamilyInstance(faceRef, placementPoint, XYZ.BasisX, symbol);
                        }
                    }

                    // Fallback to level-based placement
                    if (instance == null)
                    {
                        instance = doc.Create.NewFamilyInstance(
                            placementPoint, symbol, level, StructuralType.NonStructural);
                    }

                    if (instance != null)
                    {
                        // Tính tổng góc xoay
                        double totalRotation = 0;

                        // 1. Xoay theo CAD (nếu bật)
                        if (config.IncludeRotation && i < blockInfo.Rotations.Count)
                        {
                            totalRotation += blockInfo.Rotations[i];
                        }

                        // 2. Cộng thêm góc xoay offset (degrees → radians)
                        if (Math.Abs(config.RotationOffset) > 0.001)
                        {
                            totalRotation += config.RotationOffset * Math.PI / 180.0;
                        }

                        // Áp dụng xoay
                        if (Math.Abs(totalRotation) > 0.001)
                        {
                            var axis = Autodesk.Revit.DB.Line.CreateBound(placementPoint, placementPoint + XYZ.BasisZ);
                            ElementTransformUtils.RotateElement(doc, instance.Id, axis, totalRotation);
                        }

                        if (config.PlacementMode == BlockPlacementMode.PlaceOnLevel && Math.Abs(offsetFeet) > 0.001)
                        {
                            instance.get_Parameter(BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM)?.Set(offsetFeet);
                        }

                        placedCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"'{mapping.BlockName}' #{i + 1}: {ex.Message}");
                }
            }

            result.CountByBlock[mapping.BlockName] = placedCount;
            result.TotalPlaced += placedCount;
        }

        return result;
    }

    private static Reference? FindNearestFace(Document doc, XYZ point, double radiusFeet, out Element? host)
    {
        host = null;
        try
        {
            // Các đối tượng ưu tiên làm host
            var categories = new List<BuiltInCategory>
            {
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Ceilings,
                BuiltInCategory.OST_Roofs
            };

            var filter = new ElementMulticategoryFilter(categories);
            var intersector = new ReferenceIntersector(filter, FindReferenceTarget.Face, (View3D)doc.ActiveView);

            // Bắn tia theo 6 hướng để tìm vật phẩm gần nhất
            var directions = new[] { XYZ.BasisX, -XYZ.BasisX, XYZ.BasisY, -XYZ.BasisY, XYZ.BasisZ, -XYZ.BasisZ };
            ReferenceWithContext? closest = null;
            double minDist = double.MaxValue;

            foreach (var dir in directions)
            {
                var result = intersector.FindNearest(point, dir);
                if (result != null && result.Proximity < radiusFeet && result.Proximity < minDist)
                {
                    minDist = result.Proximity;
                    closest = result;
                }
            }

            if (closest != null)
            {
                var reference = closest.GetReference();
                host = doc.GetElement(reference.ElementId);
                return reference;
            }
        }
        catch { }
        return null;
    }
}
