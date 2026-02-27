using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;

namespace CIC.BIM.Addin.Tools.Services;

#region Data Models

/// <summary>Options for formwork calculation.</summary>
public class FormworkOptions
{
    public bool IncludeBeam { get; set; } = true;
    public bool IncludeColumn { get; set; } = true;
    public bool IncludeWall { get; set; } = true;
    public bool IncludeFloor { get; set; }
    public bool IncludeFoundation { get; set; }

    public bool AutoDeductIntersection { get; set; } = true;
    public bool GroupByLevel { get; set; } = true;
    public bool GroupByType { get; set; }
}

/// <summary>Formwork result for a single element.</summary>
public class FormworkItem
{
    public ElementId Id { get; set; } = ElementId.InvalidElementId;
    public string Category { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string LevelName { get; set; } = "";

    /// <summary>Dimensions in mm.</summary>
    public double WidthMm { get; set; }
    public double HeightMm { get; set; }
    public double LengthMm { get; set; }

    /// <summary>Areas in m².</summary>
    public double GrossArea { get; set; }
    public double DeductionArea { get; set; }
    public double NetArea => GrossArea - DeductionArea;
}

/// <summary>Aggregated result for all elements.</summary>
public class FormworkResult
{
    public List<FormworkItem> Items { get; set; } = new();
    public List<string> Warnings { get; set; } = new();

    public double TotalGrossArea => Items.Sum(i => i.GrossArea);
    public double TotalDeduction => Items.Sum(i => i.DeductionArea);
    public double TotalNetArea => Items.Sum(i => i.NetArea);
    public int ElementCount => Items.Count;
}

#endregion

/// <summary>
/// Core engine: tính diện tích ván khuôn bằng Solid Geometry + Face Normal.
/// Chính xác hơn công thức gần đúng (b×h×L) — hỗ trợ dầm cong, cấu kiện nghiêng.
/// </summary>
public class FormworkService
{
    private const double FeetToMm = 304.8;
    private const double SqFeetToSqM = 0.092903;
    private const double NormalTolerance = 0.8; // cos(~37°) — phân loại mặt ngang/đứng

    /// <summary>
    /// Tính diện tích ván khuôn cho danh sách cấu kiện.
    /// </summary>
    public FormworkResult Calculate(Document doc, IList<Element> elements, FormworkOptions options)
    {
        var result = new FormworkResult();

        foreach (var elem in elements)
        {
            try
            {
                var item = CalculateElement(doc, elem);
                if (item != null)
                    result.Items.Add(item);
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"{elem.Name} (ID:{elem.Id.IntegerValue}): {ex.Message}");
            }
        }

        // Trừ giao nhau
        if (options.AutoDeductIntersection)
        {
            DeductIntersections(doc, elements, result);
        }

        return result;
    }

    /// <summary>
    /// Thu thập tất cả cấu kiện kết cấu từ model theo options.
    /// </summary>
    public static List<Element> CollectElements(Document doc, FormworkOptions options)
    {
        var elements = new List<Element>();

        if (options.IncludeBeam)
            elements.AddRange(Collect(doc, BuiltInCategory.OST_StructuralFraming));

        if (options.IncludeColumn)
            elements.AddRange(Collect(doc, BuiltInCategory.OST_StructuralColumns));

        if (options.IncludeWall)
        {
            // Chỉ lấy tường kết cấu
            elements.AddRange(
                Collect(doc, BuiltInCategory.OST_Walls)
                    .Where(w => w is Wall wall &&
                                wall.WallType.Kind == WallKind.Basic &&
                                IsStructuralWall(wall)));
        }

        if (options.IncludeFloor)
        {
            elements.AddRange(
                Collect(doc, BuiltInCategory.OST_Floors)
                    .Where(f => IsStructuralFloor(f)));
        }

        if (options.IncludeFoundation)
        {
            elements.AddRange(Collect(doc, BuiltInCategory.OST_StructuralFoundation));
        }

        return elements;
    }

    #region Private — Element Calculation

    private FormworkItem? CalculateElement(Document doc, Element elem)
    {
        var category = GetCategoryKey(elem);
        if (category == null) return null;

        var solid = GetSolid(elem);
        if (solid == null || solid.Faces.Size == 0) return null;

        var item = new FormworkItem
        {
            Id = elem.Id,
            Category = category,
            TypeName = GetTypeName(elem),
            LevelName = GetLevelName(doc, elem)
        };

        // Lấy kích thước tham khảo
        ExtractDimensions(elem, item);

        // Tính diện tích ván khuôn từ Solid Faces
        double formworkArea = 0;

        foreach (Face face in solid.Faces)
        {
            var faceType = ClassifyFace(face, category);
            if (faceType == FaceRole.Formwork)
            {
                formworkArea += face.Area * SqFeetToSqM;
            }
        }

        item.GrossArea = Math.Round(formworkArea, 3);
        return item;
    }

    /// <summary>
    /// Phân loại Face dựa trên Face Normal + loại cấu kiện.
    /// </summary>
    private FaceRole ClassifyFace(Face face, string category)
    {
        // Lấy normal tại trọng tâm
        var bb = face.GetBoundingBox();
        var midUV = new UV(
            (bb.Min.U + bb.Max.U) / 2,
            (bb.Min.V + bb.Max.V) / 2);
        var normal = face.ComputeNormal(midUV);

        bool isTopFace = normal.Z > NormalTolerance;
        bool isBottomFace = normal.Z < -NormalTolerance;
        bool isSideFace = !isTopFace && !isBottomFace;

        return category switch
        {
            // Dầm: đáy + 2 bên (mặt trên tiếp xúc sàn → không tính)
            "Dầm" => isTopFace ? FaceRole.Excluded : FaceRole.Formwork,

            // Cột: chỉ mặt bên (trên/dưới tiếp xúc dầm/sàn)
            "Cột" => isSideFace ? FaceRole.Formwork : FaceRole.Excluded,

            // Tường: mặt bên lớn (trên/dưới tiếp xúc dầm/sàn)
            "Tường" => isSideFace ? FaceRole.Formwork : FaceRole.Excluded,

            // Sàn: chỉ mặt đáy
            "Sàn" => isBottomFace ? FaceRole.Formwork : FaceRole.Excluded,

            // Móng: đáy + bên
            "Móng" => isTopFace ? FaceRole.Excluded : FaceRole.Formwork,

            _ => FaceRole.Excluded
        };
    }

    private enum FaceRole { Formwork, Excluded }

    #endregion

    #region Private — Intersection Deduction

    /// <summary>
    /// Trừ diện tích giao nhau giữa các cấu kiện.
    /// Dùng BoundingBox intersection để ước lượng diện tích bị che.
    /// </summary>
    private void DeductIntersections(Document doc, IList<Element> elements, FormworkResult result)
    {
        // Tạo lookup nhanh: ElementId → FormworkItem
        var lookup = result.Items.ToDictionary(i => i.Id.IntegerValue);

        // Chỉ trừ giữa các cặp category được biết
        var deductPairs = new[]
        {
            ("Dầm", "Cột"),    // Beam–Column
            ("Dầm", "Sàn"),    // Beam–Slab  
            ("Cột", "Tường"),  // Column–Wall
            ("Cột", "Sàn"),    // Column–Slab
        };

        foreach (var item in result.Items)
        {
            var elem = doc.GetElement(item.Id);
            if (elem == null) continue;

            foreach (var pair in deductPairs)
            {
                string otherCategory;
                if (item.Category == pair.Item1)
                    otherCategory = pair.Item2;
                else if (item.Category == pair.Item2)
                    otherCategory = pair.Item1;
                else
                    continue;

                // Tìm các element thuộc category kia giao với element hiện tại
                var otherItems = result.Items
                    .Where(i => i.Category == otherCategory)
                    .ToList();

                foreach (var otherItem in otherItems)
                {
                    var otherElem = doc.GetElement(otherItem.Id);
                    if (otherElem == null) continue;

                    try
                    {
                        var deduction = CalculateIntersectionDeduction(
                            doc, elem, item, otherElem, otherItem);
                        if (deduction > 0)
                        {
                            item.DeductionArea += deduction;
                        }
                    }
                    catch
                    {
                        // Lỗi tính giao → bỏ qua
                    }
                }
            }

            // Giới hạn: deduction không vượt quá 50% gross
            item.DeductionArea = Math.Min(item.DeductionArea, item.GrossArea * 0.5);
            item.DeductionArea = Math.Round(item.DeductionArea, 3);
        }
    }

    /// <summary>
    /// Ước lượng diện tích bị trừ khi 2 cấu kiện giao nhau.
    /// Dùng BoundingBox intersection area.
    /// </summary>
    private double CalculateIntersectionDeduction(
        Document doc, Element elem1, FormworkItem item1,
        Element elem2, FormworkItem item2)
    {
        var bb1 = elem1.get_BoundingBox(null);
        var bb2 = elem2.get_BoundingBox(null);
        if (bb1 == null || bb2 == null) return 0;

        // Kiểm tra BoundingBox có giao nhau không
        double xOverlap = Math.Max(0,
            Math.Min(bb1.Max.X, bb2.Max.X) - Math.Max(bb1.Min.X, bb2.Min.X));
        double yOverlap = Math.Max(0,
            Math.Min(bb1.Max.Y, bb2.Max.Y) - Math.Max(bb1.Min.Y, bb2.Min.Y));
        double zOverlap = Math.Max(0,
            Math.Min(bb1.Max.Z, bb2.Max.Z) - Math.Max(bb1.Min.Z, bb2.Min.Z));

        if (xOverlap <= 0 || yOverlap <= 0 || zOverlap <= 0)
            return 0;

        // Xác nhận giao nhau thật bằng ElementIntersectsElementFilter
        var filter = new ElementIntersectsElementFilter(elem2);
        if (!filter.PassesFilter(elem1))
            return 0;

        // Ước lượng diện tích bị che:
        // Lấy mặt tiếp xúc dựa trên hướng giao
        double deduction = 0;

        if (item1.Category == "Dầm" && item2.Category == "Cột")
        {
            // Dầm giao cột: mặt bên dầm bị cột che ≈ 2 × (chiều rộng cột × chiều cao giao)
            deduction = 2 * (xOverlap * zOverlap + yOverlap * zOverlap) * SqFeetToSqM / 2;
        }
        else if (item1.Category == "Dầm" && item2.Category == "Sàn")
        {
            // Sàn phủ mặt trên dầm (đã loại trong ClassifyFace, nhưng trừ thêm ở bên)
            deduction = 0; // Đã xử lý trong ClassifyFace
        }
        else if (item1.Category == "Cột" && item2.Category == "Tường")
        {
            // Cột giao tường: mặt cột bị tường che
            deduction = 2 * Math.Min(xOverlap, yOverlap) * zOverlap * SqFeetToSqM;
        }
        else if (item1.Category == "Cột" && item2.Category == "Sàn")
        {
            // Cột xuyên sàn: mặt cột bị sàn che ≈ chu vi cột × chiều dày sàn
            double perimeter = 2 * (xOverlap + yOverlap);
            deduction = perimeter * zOverlap * SqFeetToSqM;
        }

        return Math.Round(deduction, 3);
    }

    #endregion

    #region Private — Helpers

    private static List<Element> Collect(Document doc, BuiltInCategory cat)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(cat)
            .WhereElementIsNotElementType()
            .ToList();
    }

    /// <summary>Lấy Solid lớn nhất từ element geometry.</summary>
    private static Solid? GetSolid(Element elem)
    {
        var geoOpts = new Options
        {
            ComputeReferences = false,
            DetailLevel = ViewDetailLevel.Fine
        };

        var geoElem = elem.get_Geometry(geoOpts);
        if (geoElem == null) return null;

        return GetLargestSolid(geoElem);
    }

    private static Solid? GetLargestSolid(GeometryElement geoElem)
    {
        Solid? largest = null;
        double maxVolume = 0;

        foreach (var geoObj in geoElem)
        {
            if (geoObj is Solid solid && solid.Volume > maxVolume)
            {
                maxVolume = solid.Volume;
                largest = solid;
            }
            else if (geoObj is GeometryInstance geoInst)
            {
                var instGeo = geoInst.GetInstanceGeometry();
                if (instGeo != null)
                {
                    var instSolid = GetLargestSolid(instGeo);
                    if (instSolid != null && instSolid.Volume > maxVolume)
                    {
                        maxVolume = instSolid.Volume;
                        largest = instSolid;
                    }
                }
            }
        }

        return largest;
    }

    private static string? GetCategoryKey(Element elem)
    {
        var catId = elem.Category?.Id.IntegerValue;
        return catId switch
        {
            (int)BuiltInCategory.OST_StructuralFraming => "Dầm",
            (int)BuiltInCategory.OST_StructuralColumns => "Cột",
            (int)BuiltInCategory.OST_Walls => "Tường",
            (int)BuiltInCategory.OST_Floors => "Sàn",
            (int)BuiltInCategory.OST_StructuralFoundation => "Móng",
            _ => null
        };
    }

    private static string GetTypeName(Element elem)
    {
        if (elem is FamilyInstance fi)
            return fi.Symbol?.FamilyName + " : " + fi.Name;
        return elem.Name;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        // Try Reference Level param first
        var levelParam = elem.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM)
                      ?? elem.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                      ?? elem.get_Parameter(BuiltInParameter.SCHEDULE_LEVEL_PARAM)
                      ?? elem.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT)
                      ?? elem.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);

        if (levelParam != null)
        {
            var levelId = levelParam.AsElementId();
            if (levelId != null && levelId != ElementId.InvalidElementId)
            {
                var level = doc.GetElement(levelId) as Level;
                if (level != null) return level.Name;
            }
        }

        // Fallback: try LevelId property
        if (elem is Wall wall && wall.LevelId != ElementId.InvalidElementId)
        {
            var lvl = doc.GetElement(wall.LevelId) as Level;
            if (lvl != null) return lvl.Name;
        }

        return "N/A";
    }

    /// <summary>Trích xuất kích thước tham khảo (mm) cho mỗi loại cấu kiện.</summary>
    private static void ExtractDimensions(Element elem, FormworkItem item)
    {
        if (elem is FamilyInstance fi)
        {
            // Beam / Column: lấy từ Symbol parameters
            item.WidthMm = GetParamValueMm(fi, BuiltInParameter.FAMILY_WIDTH_PARAM,
                BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH);
            item.HeightMm = GetParamValueMm(fi, BuiltInParameter.FAMILY_HEIGHT_PARAM,
                BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT);
            item.LengthMm = GetParamValueMm(fi, BuiltInParameter.INSTANCE_LENGTH_PARAM,
                BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH);
        }
        else if (elem is Wall wall)
        {
            item.LengthMm = (wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0) * FeetToMm;
            item.HeightMm = (wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0) * FeetToMm;
            item.WidthMm = wall.WallType.Width * FeetToMm;
        }
        else if (elem is Floor floor)
        {
            var area = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() ?? 0;
            item.LengthMm = Math.Sqrt(area) * FeetToMm; // Ước lượng
            item.WidthMm = item.LengthMm;
            // Chiều dày sàn
            var floorType = floor.Document.GetElement(floor.GetTypeId()) as FloorType;
            if (floorType != null)
            {
                var structure = floorType.GetCompoundStructure();
                if (structure != null)
                    item.HeightMm = structure.GetWidth() * FeetToMm;
            }
        }

        item.WidthMm = Math.Round(item.WidthMm, 0);
        item.HeightMm = Math.Round(item.HeightMm, 0);
        item.LengthMm = Math.Round(item.LengthMm, 0);
    }

    private static double GetParamValueMm(FamilyInstance fi, params BuiltInParameter[] candidates)
    {
        foreach (var bip in candidates)
        {
            var p = fi.get_Parameter(bip) ?? fi.Symbol?.get_Parameter(bip);
            if (p != null && p.HasValue)
            {
                var val = p.AsDouble();
                if (val > 0) return val * FeetToMm;
            }
        }
        return 0;
    }

    private static bool IsStructuralWall(Wall wall)
    {
        var structParam = wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT);
        return structParam?.AsInteger() == 1;
    }

    private static bool IsStructuralFloor(Element elem)
    {
        var structParam = elem.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
        return structParam?.AsInteger() == 1;
    }

    #endregion
}
