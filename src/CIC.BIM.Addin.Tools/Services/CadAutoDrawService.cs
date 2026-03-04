using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;

namespace CIC.BIM.Addin.Tools.Services;

// ══════════════ ENUMS & MODELS ══════════════

public enum RevitObjectType
{
    Ignore,     // Bỏ qua layer này
    Wall,
    Column,
    Beam,
    Floor,
    Pipe,
    Duct,
    CableTray,
    FamilyInstance
}

/// <summary>
/// Thông tin 1 CAD link trong model.
/// </summary>
public record CadLinkInfo(ElementId Id, string FileName);

/// <summary>
/// Thông tin 1 layer trong file CAD.
/// </summary>
public class CadLayerInfo
{
    public string LayerName { get; set; } = "";
    public int LineCount { get; set; }
    public int BlockCount { get; set; }
    public bool IsSelected { get; set; }
    public RevitObjectType ObjectType { get; set; } = RevitObjectType.Ignore;
    public string RevitTypeName { get; set; } = "";
    public double Size { get; set; } // diameter for pipe, height for wall, etc.
}

/// <summary>
/// Cấu hình chạy Auto-Draw.
/// </summary>
public class CadAutoDrawConfig
{
    public ElementId CadLinkId { get; set; } = ElementId.InvalidElementId;
    public ElementId LevelId { get; set; } = ElementId.InvalidElementId;
    public double DefaultHeight { get; set; } = 3000; // mm
    public double Offset { get; set; } // mm
    public List<CadLayerMapping> Mappings { get; set; } = new();
}

public class CadLayerMapping
{
    public string LayerName { get; set; } = "";
    public RevitObjectType ObjectType { get; set; }
    public string RevitTypeName { get; set; } = "";
    public double Size { get; set; }
}

/// <summary>
/// Kết quả sau khi chạy Auto-Draw.
/// </summary>
public class CadAutoDrawResult
{
    public int TotalCreated { get; set; }
    public Dictionary<RevitObjectType, int> CountByType { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

// ══════════════ SERVICE ══════════════

public static class CadAutoDrawService
{
    private const double MmToFeet = 1.0 / 304.8;

    /// <summary>
    /// Tìm tất cả CAD link instances trong model.
    /// </summary>
    public static List<CadLinkInfo> ScanCadLinks(Document doc)
    {
        var result = new List<CadLinkInfo>();

        var collector = new FilteredElementCollector(doc)
            .OfClass(typeof(ImportInstance));

        foreach (var elem in collector)
        {
            if (elem is not ImportInstance import) continue;

            // Lấy tên file CAD
            var name = "(Unknown)";
            if (import.IsLinked)
            {
                var cadType = doc.GetElement(import.GetTypeId());
                if (cadType != null)
                    name = cadType.Name;
            }
            else
            {
                // Imported (not link)
                var cadType = doc.GetElement(import.GetTypeId());
                name = cadType?.Name ?? "(Imported)";
            }

            result.Add(new CadLinkInfo(elem.Id, name));
        }

        return result;
    }

    /// <summary>
    /// Scan tất cả layers từ 1 CAD link, đếm số line và block trên mỗi layer.
    /// </summary>
    public static List<CadLayerInfo> ScanLayers(Document doc, ElementId cadLinkId)
    {
        var layerDict = new Dictionary<string, CadLayerInfo>();
        var import = doc.GetElement(cadLinkId) as ImportInstance;
        if (import == null) return new();

        var geoElem = import.get_Geometry(new Options
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = true
        });

        if (geoElem == null) return new();

        TraverseGeometry(geoElem, layerDict, doc);

        return layerDict.Values
            .OrderByDescending(l => l.LineCount + l.BlockCount)
            .ThenBy(l => l.LayerName)
            .ToList();
    }

    /// <summary>
    /// Đệ quy duyệt GeometryElement để trích xuất thông tin layer.
    /// </summary>
    private static void TraverseGeometry(GeometryElement geoElem,
        Dictionary<string, CadLayerInfo> layerDict, Document doc)
    {
        foreach (var geoObj in geoElem)
        {
            switch (geoObj)
            {
                case GeometryInstance geoInst:
                    // CAD link thường wrap trong GeometryInstance
                    var instGeo = geoInst.GetInstanceGeometry();
                    if (instGeo != null)
                        TraverseGeometry(instGeo, layerDict, doc);
                    break;

                case Line:
                case Arc:
                case NurbSpline:
                case HermiteSpline:
                case Ellipse:
                    ProcessCurve(geoObj as Curve, layerDict, doc);
                    break;

                case PolyLine polyLine:
                    ProcessPolyLine(polyLine, layerDict, doc);
                    break;

                case Point:
                    // Points thường là block insertion points — đếm block
                    var ptLayer = GetLayerName(geoObj, doc);
                    if (!string.IsNullOrEmpty(ptLayer))
                    {
                        EnsureLayer(layerDict, ptLayer);
                        layerDict[ptLayer].BlockCount++;
                    }
                    break;
            }
        }
    }

    private static void ProcessCurve(Curve? curve, Dictionary<string, CadLayerInfo> layerDict, Document doc)
    {
        if (curve == null) return;
        var layerName = GetLayerName(curve, doc);
        if (string.IsNullOrEmpty(layerName)) return;
        EnsureLayer(layerDict, layerName);
        layerDict[layerName].LineCount++;
    }

    private static void ProcessPolyLine(PolyLine polyLine, Dictionary<string, CadLayerInfo> layerDict, Document doc)
    {
        var layerName = GetLayerName(polyLine, doc);
        if (string.IsNullOrEmpty(layerName)) return;
        EnsureLayer(layerDict, layerName);
        // PolyLine đếm như 1 đối tượng
        layerDict[layerName].LineCount++;
    }

    /// <summary>
    /// Lấy tên layer (GraphicsStyle) từ geometry object.
    /// </summary>
    private static string GetLayerName(GeometryObject geoObj, Document doc)
    {
        var styleId = geoObj.GraphicsStyleId;
        if (styleId == ElementId.InvalidElementId) return "";

        var style = doc.GetElement(styleId) as GraphicsStyle;
        return style?.GraphicsStyleCategory?.Name ?? "";
    }

    private static void EnsureLayer(Dictionary<string, CadLayerInfo> dict, string name)
    {
        if (!dict.ContainsKey(name))
            dict[name] = new CadLayerInfo { LayerName = name };
    }

    /// <summary>
    /// Lấy danh sách tên type có sẵn trong model theo loại đối tượng.
    /// </summary>
    public static List<string> GetAvailableTypes(Document doc, RevitObjectType objectType)
    {
        return objectType switch
        {
            RevitObjectType.Wall => GetTypeNames<WallType>(doc),
            RevitObjectType.Column => GetFamilySymbolNames(doc, BuiltInCategory.OST_StructuralColumns),
            RevitObjectType.Beam => GetFamilySymbolNames(doc, BuiltInCategory.OST_StructuralFraming),
            RevitObjectType.Floor => GetTypeNames<FloorType>(doc),
            RevitObjectType.Pipe => GetTypeNames<PipeType>(doc),
            RevitObjectType.Duct => GetTypeNames<DuctType>(doc),
            RevitObjectType.CableTray => GetTypeNames<CableTrayType>(doc),
            RevitObjectType.FamilyInstance => GetAllFamilyNames(doc),
            _ => new()
        };
    }

    private static List<string> GetTypeNames<T>(Document doc) where T : ElementType
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(T))
            .Cast<T>()
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();
    }

    private static List<string> GetFamilySymbolNames(Document doc, BuiltInCategory cat)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(cat)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Select(s => $"{s.FamilyName} : {s.Name}")
            .OrderBy(n => n)
            .ToList();
    }

    private static List<string> GetAllFamilyNames(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Select(s => $"{s.FamilyName} : {s.Name}")
            .Distinct()
            .OrderBy(n => n)
            .ToList();
    }

    // ══════════════ EXECUTE: TẠO ĐỐI TƯỢNG REVIT ══════════════

    /// <summary>
    /// Chạy tạo đối tượng Revit từ CAD. Phải wrap trong Transaction.
    /// </summary>
    public static CadAutoDrawResult Execute(Document doc, CadAutoDrawConfig config)
    {
        var result = new CadAutoDrawResult();
        var import = doc.GetElement(config.CadLinkId) as ImportInstance;
        if (import == null)
        {
            result.Errors.Add("Không tìm thấy CAD link.");
            return result;
        }

        // Build lookup: layer → mapping
        var mappingLookup = config.Mappings
            .Where(m => m.ObjectType != RevitObjectType.Ignore)
            .ToDictionary(m => m.LayerName, m => m);

        if (mappingLookup.Count == 0)
        {
            result.Errors.Add("Chưa cấu hình mapping nào.");
            return result;
        }

        // Get geometry
        var geoElem = import.get_Geometry(new Options
        {
            ComputeReferences = true,
            IncludeNonVisibleObjects = true
        });
        if (geoElem == null)
        {
            result.Errors.Add("Không đọc được geometry từ CAD link.");
            return result;
        }

        // Get level
        var level = doc.GetElement(config.LevelId) as Level;
        if (level == null)
        {
            result.Errors.Add("Level không hợp lệ.");
            return result;
        }

        // Collect curves by layer
        var curvesByLayer = new Dictionary<string, List<Curve>>();
        var polylinesByLayer = new Dictionary<string, List<PolyLine>>();
        var pointsByLayer = new Dictionary<string, List<XYZ>>();
        CollectGeometryByLayer(geoElem, doc, curvesByLayer, polylinesByLayer, pointsByLayer);

        // Process each mapping
        foreach (var kvp in mappingLookup)
        {
            var layerName = kvp.Key;
            var mapping = kvp.Value;
            var count = 0;

            try
            {
                switch (mapping.ObjectType)
                {
                    case RevitObjectType.Wall:
                        count = CreateWalls(doc, curvesByLayer, polylinesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.Beam:
                        count = CreateBeams(doc, curvesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.Column:
                        count = CreateColumns(doc, pointsByLayer, curvesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.Floor:
                        count = CreateFloors(doc, curvesByLayer, polylinesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.Pipe:
                        count = CreatePipes(doc, curvesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.Duct:
                        count = CreateDucts(doc, curvesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.CableTray:
                        count = CreateCableTrays(doc, curvesByLayer, layerName, mapping, level, config);
                        break;
                    case RevitObjectType.FamilyInstance:
                        count = CreateFamilyInstances(doc, pointsByLayer, layerName, mapping, level, config);
                        break;
                }

                if (!result.CountByType.ContainsKey(mapping.ObjectType))
                    result.CountByType[mapping.ObjectType] = 0;
                result.CountByType[mapping.ObjectType] += count;
                result.TotalCreated += count;
            }
            catch (System.Exception ex)
            {
                result.Errors.Add($"Layer '{layerName}' ({mapping.ObjectType}): {ex.Message}");
            }
        }

        return result;
    }

    /// <summary>
    /// Thu thập geometry theo layer.
    /// </summary>
    private static void CollectGeometryByLayer(GeometryElement geoElem, Document doc,
        Dictionary<string, List<Curve>> curves,
        Dictionary<string, List<PolyLine>> polylines,
        Dictionary<string, List<XYZ>> points)
    {
        foreach (var geoObj in geoElem)
        {
            switch (geoObj)
            {
                case GeometryInstance geoInst:
                    var instGeo = geoInst.GetInstanceGeometry();
                    if (instGeo != null)
                        CollectGeometryByLayer(instGeo, doc, curves, polylines, points);
                    break;

                case Curve curve:
                {
                    var layer = GetLayerName(curve, doc);
                    if (string.IsNullOrEmpty(layer)) break;
                    if (!curves.ContainsKey(layer)) curves[layer] = new();
                    curves[layer].Add(curve);
                    break;
                }

                case PolyLine polyLine:
                {
                    var layer = GetLayerName(polyLine, doc);
                    if (string.IsNullOrEmpty(layer)) break;
                    if (!polylines.ContainsKey(layer)) polylines[layer] = new();
                    polylines[layer].Add(polyLine);
                    break;
                }

                case Point pt:
                {
                    var layer = GetLayerName(pt, doc);
                    if (string.IsNullOrEmpty(layer)) break;
                    if (!points.ContainsKey(layer)) points[layer] = new();
                    points[layer].Add(pt.Coord);
                    break;
                }
            }
        }
    }

    // ────── WALL ──────
    private static int CreateWalls(Document doc,
        Dictionary<string, List<Curve>> curves,
        Dictionary<string, List<PolyLine>> polylines,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var wallType = FindWallType(doc, mapping.RevitTypeName);
        if (wallType == null) return 0;

        var height = (mapping.Size > 0 ? mapping.Size : config.DefaultHeight) * MmToFeet;
        var offset = config.Offset * MmToFeet;
        var count = 0;

        // Từ curves
        if (curves.TryGetValue(layerName, out var layerCurves))
        {
            foreach (var curve in layerCurves)
            {
                if (curve is Line line && line.Length > 0.01)
                {
                    try
                    {
                        Wall.Create(doc, line, wallType.Id, level.Id, height, offset, false, false);
                        count++;
                    }
                    catch { /* skip invalid geometry */ }
                }
            }
        }

        // Từ polylines — convert thành lines
        if (polylines.TryGetValue(layerName, out var layerPolylines))
        {
            foreach (var polyLine in layerPolylines)
            {
                var coords = polyLine.GetCoordinates();
                for (int i = 0; i < coords.Count - 1; i++)
                {
                    try
                    {
                        var seg = Line.CreateBound(coords[i], coords[i + 1]);
                        if (seg.Length > 0.01)
                        {
                            Wall.Create(doc, seg, wallType.Id, level.Id, height, offset, false, false);
                            count++;
                        }
                    }
                    catch { }
                }
            }
        }

        return count;
    }

    // ────── BEAM ──────
    private static int CreateBeams(Document doc,
        Dictionary<string, List<Curve>> curves,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var symbol = FindFamilySymbol(doc, mapping.RevitTypeName, BuiltInCategory.OST_StructuralFraming);
        if (symbol == null) return 0;
        if (!symbol.IsActive) symbol.Activate();

        var count = 0;
        if (!curves.TryGetValue(layerName, out var layerCurves)) return 0;

        foreach (var curve in layerCurves)
        {
            if (curve is not Line line || line.Length < 0.01) continue;
            try
            {
                // Beam tạo bằng NewFamilyInstance với line + structural type
                var beam = doc.Create.NewFamilyInstance(line, symbol, level, StructuralType.Beam);
                if (beam != null)
                {
                    // Set offset nếu cần
                    if (config.Offset != 0)
                    {
                        var offsetParam = beam.get_Parameter(BuiltInParameter.STRUCTURAL_BEAM_END0_ELEVATION);
                        offsetParam?.Set(config.Offset * MmToFeet);
                    }
                    count++;
                }
            }
            catch { }
        }

        return count;
    }

    // ────── COLUMN ──────
    private static int CreateColumns(Document doc,
        Dictionary<string, List<XYZ>> points,
        Dictionary<string, List<Curve>> curves,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var symbol = FindFamilySymbol(doc, mapping.RevitTypeName, BuiltInCategory.OST_StructuralColumns);
        if (symbol == null) return 0;
        if (!symbol.IsActive) symbol.Activate();

        var height = (mapping.Size > 0 ? mapping.Size : config.DefaultHeight) * MmToFeet;
        var count = 0;

        // Từ points (block insertion points)
        if (points.TryGetValue(layerName, out var layerPoints))
        {
            foreach (var pt in layerPoints)
            {
                try
                {
                    var col = doc.Create.NewFamilyInstance(pt, symbol, level, StructuralType.Column);
                    if (col != null) count++;
                }
                catch { }
            }
        }

        // Từ curves — dùng midpoint của line ngắn (< 2m) làm vị trí cột
        if (curves.TryGetValue(layerName, out var layerCurves))
        {
            // Nhóm các line ngắn thành hình chữ nhật → lấy tâm
            var shortLines = layerCurves
                .Where(c => c is Line l && l.Length < 2.0 * 2) // < 2m in feet
                .ToList();

            if (shortLines.Count > 0)
            {
                // Lấy center từ midpoint trung bình
                var midpoints = shortLines.Select(c => c.Evaluate(0.5, true)).ToList();
                var grouped = GroupNearbyPoints(midpoints, 1.0); // group within 1 foot

                foreach (var center in grouped)
                {
                    try
                    {
                        var col = doc.Create.NewFamilyInstance(center, symbol, level, StructuralType.Column);
                        if (col != null) count++;
                    }
                    catch { }
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Gom nhóm các điểm gần nhau, trả về centroid mỗi nhóm.
    /// </summary>
    private static List<XYZ> GroupNearbyPoints(List<XYZ> points, double tolerance)
    {
        var used = new bool[points.Count];
        var result = new List<XYZ>();

        for (int i = 0; i < points.Count; i++)
        {
            if (used[i]) continue;
            var group = new List<XYZ> { points[i] };
            used[i] = true;

            for (int j = i + 1; j < points.Count; j++)
            {
                if (used[j]) continue;
                if (points[i].DistanceTo(points[j]) < tolerance)
                {
                    group.Add(points[j]);
                    used[j] = true;
                }
            }

            var cx = group.Average(p => p.X);
            var cy = group.Average(p => p.Y);
            var cz = group.Average(p => p.Z);
            result.Add(new XYZ(cx, cy, cz));
        }

        return result;
    }

    // ────── FLOOR ──────
    private static int CreateFloors(Document doc,
        Dictionary<string, List<Curve>> curves,
        Dictionary<string, List<PolyLine>> polylines,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var floorType = FindFloorType(doc, mapping.RevitTypeName);
        if (floorType == null) return 0;

        var count = 0;

        // Từ polylines khép kín → CurveLoop
        if (polylines.TryGetValue(layerName, out var layerPolylines))
        {
            foreach (var polyLine in layerPolylines)
            {
                var coords = polyLine.GetCoordinates();
                if (coords.Count < 3) continue;

                try
                {
                    var curveLoop = new CurveLoop();
                    for (int i = 0; i < coords.Count - 1; i++)
                    {
                        var seg = Line.CreateBound(coords[i], coords[i + 1]);
                        curveLoop.Append(seg);
                    }

                    // Đóng kín nếu chưa
                    if (!coords.First().IsAlmostEqualTo(coords.Last()))
                    {
                        curveLoop.Append(Line.CreateBound(coords.Last(), coords.First()));
                    }

                    var loops = new List<CurveLoop> { curveLoop };
                    var floor = Floor.Create(doc, loops, floorType.Id, level.Id);
                    if (floor != null)
                    {
                        if (config.Offset != 0)
                        {
                            var offsetParam = floor.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
                            offsetParam?.Set(config.Offset * MmToFeet);
                        }
                        count++;
                    }
                }
                catch { }
            }
        }

        return count;
    }

    // ────── PIPE ──────
    private static int CreatePipes(Document doc,
        Dictionary<string, List<Curve>> curves,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var pipeType = FindType<PipeType>(doc, mapping.RevitTypeName);
        if (pipeType == null) return 0;

        var systemType = new FilteredElementCollector(doc)
            .OfClass(typeof(PipingSystemType))
            .FirstOrDefault() as PipingSystemType;
        if (systemType == null) return 0;

        var diameter = (mapping.Size > 0 ? mapping.Size : 110) * MmToFeet; // default Ø110
        var elevation = config.Offset * MmToFeet;
        var count = 0;

        if (!curves.TryGetValue(layerName, out var layerCurves)) return 0;

        foreach (var curve in layerCurves)
        {
            if (curve is not Line line || line.Length < 0.01) continue;
            try
            {
                var pipe = Autodesk.Revit.DB.Plumbing.Pipe.Create(
                    doc, systemType.Id, pipeType.Id, level.Id,
                    line.GetEndPoint(0), line.GetEndPoint(1));

                if (pipe != null)
                {
                    var diaParam = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
                    diaParam?.Set(diameter);
                    count++;
                }
            }
            catch { }
        }

        return count;
    }

    // ────── DUCT ──────
    private static int CreateDucts(Document doc,
        Dictionary<string, List<Curve>> curves,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var ductType = FindType<DuctType>(doc, mapping.RevitTypeName);
        if (ductType == null) return 0;

        var systemType = new FilteredElementCollector(doc)
            .OfClass(typeof(MechanicalSystemType))
            .FirstOrDefault() as MechanicalSystemType;
        if (systemType == null) return 0;

        var size = (mapping.Size > 0 ? mapping.Size : 400) * MmToFeet; // default 400mm
        var count = 0;

        if (!curves.TryGetValue(layerName, out var layerCurves)) return 0;

        foreach (var curve in layerCurves)
        {
            if (curve is not Line line || line.Length < 0.01) continue;
            try
            {
                var duct = Autodesk.Revit.DB.Mechanical.Duct.Create(
                    doc, systemType.Id, ductType.Id, level.Id,
                    line.GetEndPoint(0), line.GetEndPoint(1));

                if (duct != null) count++;
            }
            catch { }
        }

        return count;
    }

    // ────── CABLE TRAY ──────
    private static int CreateCableTrays(Document doc,
        Dictionary<string, List<Curve>> curves,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var cableTrayType = FindType<CableTrayType>(doc, mapping.RevitTypeName);
        if (cableTrayType == null) return 0;

        var count = 0;
        if (!curves.TryGetValue(layerName, out var layerCurves)) return 0;

        foreach (var curve in layerCurves)
        {
            if (curve is not Line line || line.Length < 0.01) continue;
            try
            {
                var tray = Autodesk.Revit.DB.Electrical.CableTray.Create(
                    doc, cableTrayType.Id, line.GetEndPoint(0), line.GetEndPoint(1), level.Id);
                if (tray != null) count++;
            }
            catch { }
        }

        return count;
    }

    // ────── FAMILY INSTANCE ──────
    private static int CreateFamilyInstances(Document doc,
        Dictionary<string, List<XYZ>> points,
        string layerName, CadLayerMapping mapping, Level level, CadAutoDrawConfig config)
    {
        var symbol = FindFamilySymbolByFullName(doc, mapping.RevitTypeName);
        if (symbol == null) return 0;
        if (!symbol.IsActive) symbol.Activate();

        var count = 0;
        if (!points.TryGetValue(layerName, out var layerPoints)) return 0;

        foreach (var pt in layerPoints)
        {
            try
            {
                var inst = doc.Create.NewFamilyInstance(pt, symbol, level, StructuralType.NonStructural);
                if (inst != null) count++;
            }
            catch { }
        }

        return count;
    }

    // ════════════ HELPERS: Find types ════════════

    private static WallType? FindWallType(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .FirstOrDefault(t => t.Name == name);
    }

    private static FloorType? FindFloorType(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(t => t.Name == name);
    }

    private static T? FindType<T>(Document doc, string name) where T : ElementType
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(T))
            .Cast<T>()
            .FirstOrDefault(t => t.Name == name);
    }

    private static FamilySymbol? FindFamilySymbol(Document doc, string fullName, BuiltInCategory cat)
    {
        // fullName = "FamilyName : TypeName"
        var parts = fullName.Split(new[] { " : " }, System.StringSplitOptions.None);
        if (parts.Length == 2)
        {
            return new FilteredElementCollector(doc)
                .OfCategory(cat)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName == parts[0] && s.Name == parts[1]);
        }

        // Fallback: tìm theo tên type
        return new FilteredElementCollector(doc)
            .OfCategory(cat)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(s => s.Name == fullName);
    }

    private static FamilySymbol? FindFamilySymbolByFullName(Document doc, string fullName)
    {
        var parts = fullName.Split(new[] { " : " }, System.StringSplitOptions.None);
        if (parts.Length == 2)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .FirstOrDefault(s => s.FamilyName == parts[0] && s.Name == parts[1]);
        }

        return new FilteredElementCollector(doc)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .FirstOrDefault(s => s.Name == fullName);
    }
}
