using System.Collections.Generic;
using System.Data;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Service quản lý tham số đối tượng: đọc, ghi tự động, ghi thủ công.
/// Hỗ trợ đa bộ môn: Kết cấu, Kiến trúc, Cơ điện, Đường ống.
/// </summary>
public class ParamManagerService
{
    #region Constants — Tên tham số chuẩn CIC

    // ═══ Nhóm Vị trí ═══
    public const string P_Apartment = "Apartment";
    public const string P_BoqGroup = "BOQ Group";
    public const string P_Location = "Location";
    public const string P_ElementLocation = "Element Location";
    public const string P_Room = "Room";
    public const string P_RoomNumber = "Room Number";
    public const string P_Method = "Method";

    // ═══ Nhóm Phân loại ═══
    public const string P_BeTong = "Be Tong";
    public const string P_Lot = "Lot";
    public const string P_HangMuc = "HKL_HangMuc";
    public const string P_PhanLoai = "Phan Loai";

    // ═══ Nhóm Thống kê CIC ═══
    public const string P_CIC_HangMuc = "CIC_HangMuc";
    public const string P_CIC_KhoiLuong = "CIC_KhoiLuong";
    public const string P_CIC_DienTich = "CIC_DienTich";
    public const string P_CIC_TheTich = "CIC_TheTich";
    public const string P_CIC_FormworkArea = "CIC_FormworkArea";
    public const string P_CIC_PhanLoai = "CIC_PhanLoai";

    private const double FeetToMm = 304.8;
    private const double SqFeetToSqM = 0.092903;
    private const double CuFeetToCuM = 0.0283168;

    #endregion

    #region Discipline & Category Mapping

    /// <summary>Bộ môn hỗ trợ</summary>
    public enum Discipline
    {
        TatCa,      // Tất cả
        KetCau,     // Kết cấu
        KienTruc,   // Kiến trúc
        CoDien,     // Cơ điện (MEP)
        DuongOng    // Đường ống (Plumbing)
    }

    /// <summary>Thông tin một loại cấu kiện</summary>
    public class CategoryInfo
    {
        public BuiltInCategory Category { get; set; }
        public string DisplayName { get; set; } = "";
        public Discipline Discipline { get; set; }
        public bool StructuralOnly { get; set; }
    }

    /// <summary>Tất cả loại cấu kiện hỗ trợ</summary>
    public static readonly CategoryInfo[] AllCategories = new[]
    {
        // ═══ Kết cấu ═══
        new CategoryInfo { Category = BuiltInCategory.OST_StructuralFraming,    DisplayName = "Dầm",              Discipline = Discipline.KetCau },
        new CategoryInfo { Category = BuiltInCategory.OST_StructuralColumns,    DisplayName = "Cột",              Discipline = Discipline.KetCau },
        new CategoryInfo { Category = BuiltInCategory.OST_Walls,                DisplayName = "Tường KC",         Discipline = Discipline.KetCau, StructuralOnly = true },
        new CategoryInfo { Category = BuiltInCategory.OST_Floors,               DisplayName = "Sàn KC",           Discipline = Discipline.KetCau, StructuralOnly = true },
        new CategoryInfo { Category = BuiltInCategory.OST_StructuralFoundation, DisplayName = "Móng",             Discipline = Discipline.KetCau },

        // ═══ Kiến trúc ═══
        new CategoryInfo { Category = BuiltInCategory.OST_Walls,                DisplayName = "Tường KT",         Discipline = Discipline.KienTruc },
        new CategoryInfo { Category = BuiltInCategory.OST_Floors,               DisplayName = "Sàn KT",           Discipline = Discipline.KienTruc },
        new CategoryInfo { Category = BuiltInCategory.OST_Ceilings,             DisplayName = "Trần",             Discipline = Discipline.KienTruc },
        new CategoryInfo { Category = BuiltInCategory.OST_Doors,                DisplayName = "Cửa đi",           Discipline = Discipline.KienTruc },
        new CategoryInfo { Category = BuiltInCategory.OST_Windows,              DisplayName = "Cửa sổ",           Discipline = Discipline.KienTruc },
        new CategoryInfo { Category = BuiltInCategory.OST_Stairs,               DisplayName = "Cầu thang",        Discipline = Discipline.KienTruc },
        new CategoryInfo { Category = BuiltInCategory.OST_Railings,             DisplayName = "Lan can",           Discipline = Discipline.KienTruc },

        // ═══ Cơ điện (MEP) ═══
        new CategoryInfo { Category = BuiltInCategory.OST_DuctCurves,           DisplayName = "Ống gió",           Discipline = Discipline.CoDien },
        new CategoryInfo { Category = BuiltInCategory.OST_DuctFitting,          DisplayName = "Phụ kiện ống gió",  Discipline = Discipline.CoDien },
        new CategoryInfo { Category = BuiltInCategory.OST_MechanicalEquipment,  DisplayName = "Thiết bị cơ khí",   Discipline = Discipline.CoDien },
        new CategoryInfo { Category = BuiltInCategory.OST_CableTray,            DisplayName = "Máng cáp",          Discipline = Discipline.CoDien },
        new CategoryInfo { Category = BuiltInCategory.OST_LightingFixtures,     DisplayName = "Đèn",               Discipline = Discipline.CoDien },
        new CategoryInfo { Category = BuiltInCategory.OST_ElectricalFixtures,   DisplayName = "Ổ cắm",             Discipline = Discipline.CoDien },
        new CategoryInfo { Category = BuiltInCategory.OST_ElectricalEquipment,  DisplayName = "Tủ điện",            Discipline = Discipline.CoDien },

        // ═══ Đường ống (Plumbing) ═══
        new CategoryInfo { Category = BuiltInCategory.OST_PipeCurves,           DisplayName = "Ống nước",           Discipline = Discipline.DuongOng },
        new CategoryInfo { Category = BuiltInCategory.OST_PipeFitting,          DisplayName = "Phụ kiện ống nước",  Discipline = Discipline.DuongOng },
        new CategoryInfo { Category = BuiltInCategory.OST_PlumbingFixtures,     DisplayName = "Thiết bị vệ sinh",  Discipline = Discipline.DuongOng },
        new CategoryInfo { Category = BuiltInCategory.OST_Sprinklers,           DisplayName = "Sprinkler",          Discipline = Discipline.DuongOng },
    };

    /// <summary>Lấy danh mục cấu kiện theo bộ môn</summary>
    public static CategoryInfo[] GetCategoriesByDiscipline(Discipline discipline)
    {
        if (discipline == Discipline.TatCa)
            return AllCategories;
        return AllCategories.Where(c => c.Discipline == discipline).ToArray();
    }

    /// <summary>Tên hiển thị của bộ môn</summary>
    public static string GetDisciplineName(Discipline d) => d switch
    {
        Discipline.TatCa => "Tất cả",
        Discipline.KetCau => "Kết cấu",
        Discipline.KienTruc => "Kiến trúc",
        Discipline.CoDien => "Cơ điện",
        Discipline.DuongOng => "Đường ống",
        _ => "Khác"
    };

    #endregion

    #region Collect Elements

    /// <summary>Thu thập đối tượng theo danh sách categories đã chọn.</summary>
    public static List<Element> CollectElements(Document doc, IEnumerable<CategoryInfo> selectedCategories)
    {
        var elements = new List<Element>();
        var addedIds = new HashSet<long>();

        foreach (var catInfo in selectedCategories)
        {
            var collected = Collect(doc, catInfo.Category);

            foreach (var elem in collected)
            {
                // Lọc kết cấu / kiến trúc cho Tường và Sàn
                if (catInfo.StructuralOnly && !IsStructural(elem))
                    continue;
                if (catInfo.Discipline == Discipline.KienTruc &&
                    (catInfo.Category == BuiltInCategory.OST_Walls || catInfo.Category == BuiltInCategory.OST_Floors) &&
                    IsStructural(elem))
                    continue;

                // Tránh trùng lặp
                var id = GetElementIdValue(elem);
                if (addedIds.Add(id))
                    elements.Add(elem);
            }
        }

        return elements;
    }

    /// <summary>Thu thập cấu kiện theo categories (API cũ cho tương thích).</summary>
    public static List<Element> CollectElements(Document doc,
        bool beam, bool column, bool wall, bool floor, bool foundation)
    {
        var selected = new List<CategoryInfo>();
        var kcCats = GetCategoriesByDiscipline(Discipline.KetCau);

        if (beam) selected.Add(kcCats.First(c => c.DisplayName == "Dầm"));
        if (column) selected.Add(kcCats.First(c => c.DisplayName == "Cột"));
        if (wall) selected.Add(kcCats.First(c => c.DisplayName == "Tường KC"));
        if (floor) selected.Add(kcCats.First(c => c.DisplayName == "Sàn KC"));
        if (foundation) selected.Add(kcCats.First(c => c.DisplayName == "Móng"));

        return CollectElements(doc, selected);
    }

    private static List<Element> Collect(Document doc, BuiltInCategory cat)
    {
        return new FilteredElementCollector(doc)
            .OfCategory(cat)
            .WhereElementIsNotElementType()
            .ToList();
    }

    private static bool IsStructural(Element e)
    {
        var p = e.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)
             ?? e.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL);
        return p?.AsInteger() == 1;
    }

    #endregion

    #region Read Params → DataTable

    /// <summary>
    /// Đọc tất cả tham số từ đối tượng vào DataTable để hiển thị trong DataGrid.
    /// Mỗi hàng = 1 đối tượng, mỗi cột = 1 tham số.
    /// </summary>
    public static DataTable ReadParamsToTable(Document doc, List<Element> elements)
    {
        var dt = new DataTable("Parameters");

        // Cột cố định
        dt.Columns.Add("ElementId", typeof(long));
        dt.Columns.Add("Loại cấu kiện", typeof(string));
        dt.Columns.Add("Bộ môn", typeof(string));
        dt.Columns.Add("Type", typeof(string));
        dt.Columns.Add("Tầng", typeof(string));

        // Cột kích thước (chỉ đọc)
        dt.Columns.Add("Rộng (mm)", typeof(double));
        dt.Columns.Add("Cao (mm)", typeof(double));
        dt.Columns.Add("Dài (mm)", typeof(double));
        dt.Columns.Add("Diện tích (m²)", typeof(double));
        dt.Columns.Add("Thể tích (m³)", typeof(double));

        // Khám phá shared parameters
        var sharedParamNames = DiscoverSharedParams(elements);
        foreach (var pName in sharedParamNames)
        {
            if (!dt.Columns.Contains(pName))
                dt.Columns.Add(pName, typeof(string));
        }

        // Điền dữ liệu
        foreach (var elem in elements)
        {
            var row = dt.NewRow();
            row["ElementId"] = GetElementIdValue(elem);
            row["Loại cấu kiện"] = GetCategoryDisplayName(elem);
            row["Bộ môn"] = GetDisciplineForElement(elem);
            row["Type"] = GetTypeName(elem);
            row["Tầng"] = GetLevelName(doc, elem);

            // Kích thước
            ExtractDimensions(elem, row);

            // Shared params
            foreach (var pName in sharedParamNames)
            {
                var p = elem.LookupParameter(pName);
                if (p != null)
                    row[pName] = GetParamDisplayValue(p);
            }

            dt.Rows.Add(row);
        }

        return dt;
    }

    /// <summary>Tìm tất cả shared params trên elements.</summary>
    private static List<string> DiscoverSharedParams(List<Element> elements)
    {
        var paramNames = new HashSet<string>();
        // Lấy mẫu tối đa 10 elements để khám phá params
        foreach (var elem in elements.Take(10))
        {
            foreach (Parameter p in elem.Parameters)
            {
                if (p.IsShared || p.Definition is InternalDefinition id &&
                    id.BuiltInParameter == BuiltInParameter.INVALID)
                {
                    var name = p.Definition.Name;
                    if (!name.StartsWith("ELEM") && !name.StartsWith("HOST_"))
                        paramNames.Add(name);
                }
            }
        }

        // Sắp xếp: CIC_ trước, sau đó theo bảng chữ cái
        return paramNames
            .OrderByDescending(n => n.StartsWith("CIC_"))
            .ThenBy(n => n)
            .ToList();
    }

    #endregion

    #region Auto-Populate

    /// <summary>
    /// Tự động điền tham số cho tất cả đối tượng.
    /// </summary>
    public static AutoFillResult AutoPopulate(Document doc, List<Element> elements)
    {
        var result = new AutoFillResult();

        using var tx = new Transaction(doc, "CIC — Tự động điền tham số");
        tx.Start();

        foreach (var elem in elements)
        {
            try
            {
                // ═══ Phòng / Số phòng ═══
                TryAutoFill(elem, P_Room, () => GetRoomName(doc, elem), result);
                TryAutoFill(elem, P_RoomNumber, () => GetRoomNumber(doc, elem), result);

                // ═══ Vị trí ═══
                TryAutoFill(elem, P_Location, () => GetLocationText(doc, elem), result);
                TryAutoFill(elem, P_ElementLocation, () => GetElementLocation(doc, elem), result);

                // ═══ Phân loại ═══
                var catName = GetCategoryDisplayName(elem);
                TryAutoFill(elem, P_PhanLoai, () => catName, result);
                TryAutoFill(elem, P_CIC_PhanLoai, () => catName, result);

                // ═══ Bê tông (chỉ kết cấu) ═══
                TryAutoFill(elem, P_BeTong, () => GetBeTongGrade(elem), result);

                // ═══ Hạng mục ═══
                TryAutoFill(elem, P_CIC_HangMuc, () => $"{catName} - {GetTypeName(elem)}", result);

                // ═══ Diện tích (m²) ═══
                var area = GetAreaSqM(elem);
                TryAutoFillNumber(elem, P_CIC_DienTich, area, result);

                // ═══ Thể tích (m³) ═══
                var volume = GetVolumeCuM(elem);
                TryAutoFillNumber(elem, P_CIC_TheTich, volume, result);

                result.ElementsProcessed++;
            }
            catch (System.Exception ex)
            {
                result.Warnings.Add($"{elem.Name} (ID:{GetElementIdValue(elem)}): {ex.Message}");
            }
        }

        tx.Commit();
        return result;
    }

    private static void TryAutoFill(Element elem, string paramName,
        System.Func<string?> valueGetter, AutoFillResult result)
    {
        var param = elem.LookupParameter(paramName);
        if (param == null || param.IsReadOnly) return;

        // Chỉ ghi nếu param đang trống
        var currentVal = GetParamDisplayValue(param);
        if (!string.IsNullOrWhiteSpace(currentVal)) return;

        var newVal = valueGetter();
        if (string.IsNullOrWhiteSpace(newVal)) return;

        if (param.StorageType == StorageType.String)
        {
            param.Set(newVal);
            result.ValuesWritten++;
        }
    }

    private static void TryAutoFillNumber(Element elem, string paramName,
        double value, AutoFillResult result)
    {
        if (value <= 0) return;
        var param = elem.LookupParameter(paramName);
        if (param == null || param.IsReadOnly) return;

        if (param.StorageType == StorageType.Double)
        {
            param.Set(value);
            result.ValuesWritten++;
        }
        else if (param.StorageType == StorageType.String)
        {
            param.Set(value.ToString("N2"));
            result.ValuesWritten++;
        }
    }

    #endregion

    #region Write Params from DataTable

    /// <summary>
    /// Ghi thay đổi từ DataTable (sau khi người dùng chỉnh sửa) vào mô hình.
    /// </summary>
    public static WriteResult WriteFromTable(Document doc, DataTable table,
        List<string> editableColumns)
    {
        var result = new WriteResult();

        using var tx = new Transaction(doc, "CIC — Ghi tham số");
        tx.Start();

        foreach (DataRow row in table.Rows)
        {
            var elemId = new ElementId((long)row["ElementId"]);
            var elem = doc.GetElement(elemId);
            if (elem == null) continue;

            foreach (var colName in editableColumns)
            {
                if (!table.Columns.Contains(colName)) continue;

                var val = row[colName]?.ToString() ?? "";
                var param = elem.LookupParameter(colName);
                if (param == null || param.IsReadOnly) continue;

                try
                {
                    if (param.StorageType == StorageType.String)
                    {
                        param.Set(val);
                        result.ValuesWritten++;
                    }
                    else if (param.StorageType == StorageType.Double && double.TryParse(val, out var dVal))
                    {
                        param.Set(dVal);
                        result.ValuesWritten++;
                    }
                    else if (param.StorageType == StorageType.Integer && int.TryParse(val, out var iVal))
                    {
                        param.Set(iVal);
                        result.ValuesWritten++;
                    }
                }
                catch
                {
                    result.Errors++;
                }
            }

            result.ElementsUpdated++;
        }

        tx.Commit();
        return result;
    }

    /// <summary>Ghi hàng loạt 1 giá trị cho nhiều đối tượng.</summary>
    public static int BatchWrite(Document doc, IEnumerable<long> elementIds,
        string paramName, string value)
    {
        int count = 0;

        using var tx = new Transaction(doc, $"CIC — Ghi hàng loạt {paramName}");
        tx.Start();

        foreach (var id in elementIds)
        {
            var elem = doc.GetElement(new ElementId(id));
            var param = elem?.LookupParameter(paramName);
            if (param == null || param.IsReadOnly) continue;

            try
            {
                if (param.StorageType == StorageType.String)
                {
                    param.Set(value);
                    count++;
                }
                else if (param.StorageType == StorageType.Double && double.TryParse(value, out var d))
                {
                    param.Set(d);
                    count++;
                }
            }
            catch { }
        }

        tx.Commit();
        return count;
    }

    #endregion

    #region Private Helpers — Value Getters

    private static string? GetRoomName(Document doc, Element elem)
    {
        var room = GetRoomAtElement(doc, elem);
        return room?.Name;
    }

    private static string? GetRoomNumber(Document doc, Element elem)
    {
        var room = GetRoomAtElement(doc, elem);
        return room?.Number;
    }

    private static Room? GetRoomAtElement(Document doc, Element elem)
    {
        var bb = elem.get_BoundingBox(null);
        if (bb == null) return null;

        var center = new XYZ(
            (bb.Min.X + bb.Max.X) / 2,
            (bb.Min.Y + bb.Max.Y) / 2,
            (bb.Min.Z + bb.Max.Z) / 2);

        return doc.GetRoomAtPoint(center);
    }

    private static string GetLocationText(Document doc, Element elem)
    {
        var level = GetLevelName(doc, elem);
        var room = GetRoomName(doc, elem);
        if (!string.IsNullOrEmpty(room))
            return $"{level} - {room}";
        return level;
    }

    private static string GetElementLocation(Document doc, Element elem)
    {
        var bb = elem.get_BoundingBox(null);
        if (bb == null) return "";

        var cx = System.Math.Round((bb.Min.X + bb.Max.X) / 2 * FeetToMm, 0);
        var cy = System.Math.Round((bb.Min.Y + bb.Max.Y) / 2 * FeetToMm, 0);
        var level = GetLevelName(doc, elem);

        return $"{level} ({cx:N0}, {cy:N0})";
    }

    private static string? GetBeTongGrade(Element elem)
    {
        var matParam = elem.get_Parameter(BuiltInParameter.STRUCTURAL_MATERIAL_PARAM);
        if (matParam != null)
        {
            var matName = matParam.AsValueString();
            if (!string.IsNullOrEmpty(matName))
            {
                if (matName.IndexOf("C25", System.StringComparison.OrdinalIgnoreCase) >= 0) return "C25";
                if (matName.IndexOf("C30", System.StringComparison.OrdinalIgnoreCase) >= 0) return "C30";
                if (matName.IndexOf("C35", System.StringComparison.OrdinalIgnoreCase) >= 0) return "C35";
                if (matName.IndexOf("C40", System.StringComparison.OrdinalIgnoreCase) >= 0) return "C40";
                return matName;
            }
        }
        return null;
    }

    private static double GetAreaSqM(Element elem)
    {
        var p = elem.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
        if (p != null && p.HasValue)
            return p.AsDouble() * SqFeetToSqM;
        return 0;
    }

    private static double GetVolumeCuM(Element elem)
    {
        var p = elem.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED);
        if (p != null && p.HasValue)
            return p.AsDouble() * CuFeetToCuM;
        return 0;
    }

    /// <summary>Tên hiển thị tiếng Việt có dấu cho category.</summary>
    private static string GetCategoryDisplayName(Element elem)
    {
        var cat = elem.Category;
        if (cat == null) return "Khác";

        var catId = cat.Id;
        var match = AllCategories.FirstOrDefault(c =>
            c.Category == (BuiltInCategory)catId.Value);

        if (match != null)
        {
            // Phân biệt Tường/Sàn KC vs KT
            if (match.Category == BuiltInCategory.OST_Walls)
                return IsStructural(elem) ? "Tường KC" : "Tường KT";
            if (match.Category == BuiltInCategory.OST_Floors)
                return IsStructural(elem) ? "Sàn KC" : "Sàn KT";
            return match.DisplayName;
        }

        return cat.Name;
    }

    /// <summary>Xác định bộ môn của đối tượng.</summary>
    private static string GetDisciplineForElement(Element elem)
    {
        var cat = elem.Category;
        if (cat == null) return "Khác";

        var catId = cat.Id;

        // Tường/Sàn: phân biệt theo structural
        if ((BuiltInCategory)catId.Value == BuiltInCategory.OST_Walls ||
            (BuiltInCategory)catId.Value == BuiltInCategory.OST_Floors)
        {
            return IsStructural(elem) ? "Kết cấu" : "Kiến trúc";
        }

        var match = AllCategories.FirstOrDefault(c =>
            c.Category == (BuiltInCategory)catId.Value);

        return match != null ? GetDisciplineName(match.Discipline) : "Khác";
    }

    private static string GetTypeName(Element elem)
    {
        if (elem is FamilyInstance fi)
            return fi.Symbol?.FamilyName + " : " + fi.Name;
        return elem.Name;
    }

    private static string GetLevelName(Document doc, Element elem)
    {
        var bp = new[]
        {
            BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.WALL_BASE_CONSTRAINT
        };

        foreach (var bip in bp)
        {
            var p = elem.get_Parameter(bip);
            if (p != null)
            {
                var lvlId = p.AsElementId();
                if (lvlId != null && lvlId != ElementId.InvalidElementId)
                {
                    var lvl = doc.GetElement(lvlId) as Level;
                    if (lvl != null) return lvl.Name;
                }
            }
        }

        if (elem is Wall wall && wall.LevelId != ElementId.InvalidElementId)
            return (doc.GetElement(wall.LevelId) as Level)?.Name ?? "N/A";

        return "N/A";
    }

    private static string GetParamDisplayValue(Parameter p)
    {
        if (!p.HasValue) return "";
        return p.StorageType switch
        {
            StorageType.String => p.AsString() ?? "",
            StorageType.Double => p.AsValueString() ?? p.AsDouble().ToString("N2"),
            StorageType.Integer => p.AsInteger().ToString(),
            StorageType.ElementId => p.AsValueString() ?? "",
            _ => ""
        };
    }

    private static void ExtractDimensions(Element elem, DataRow row)
    {
        if (elem is FamilyInstance fi)
        {
            row["Rộng (mm)"] = GetBipValue(fi, BuiltInParameter.FAMILY_WIDTH_PARAM,
                BuiltInParameter.STRUCTURAL_SECTION_COMMON_WIDTH) * FeetToMm;
            row["Cao (mm)"] = GetBipValue(fi, BuiltInParameter.FAMILY_HEIGHT_PARAM,
                BuiltInParameter.STRUCTURAL_SECTION_COMMON_HEIGHT) * FeetToMm;
            row["Dài (mm)"] = GetBipValue(fi, BuiltInParameter.INSTANCE_LENGTH_PARAM,
                BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH) * FeetToMm;
        }
        else if (elem is Wall wall)
        {
            row["Rộng (mm)"] = wall.WallType.Width * FeetToMm;
            row["Cao (mm)"] = (wall.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble() ?? 0) * FeetToMm;
            row["Dài (mm)"] = (wall.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0) * FeetToMm;
        }

        row["Diện tích (m²)"] = GetAreaSqM(elem);
        row["Thể tích (m³)"] = GetVolumeCuM(elem);
    }

    private static double GetBipValue(FamilyInstance fi, params BuiltInParameter[] candidates)
    {
        foreach (var bip in candidates)
        {
            var p = fi.get_Parameter(bip) ?? fi.Symbol?.get_Parameter(bip);
            if (p != null && p.HasValue && p.AsDouble() > 0)
                return p.AsDouble();
        }
        return 0;
    }

    /// <summary>Lấy giá trị ElementId tương thích cả R2024 và R2025.</summary>
    private static long GetElementIdValue(Element elem)
    {
        // R2025 dùng .Value (long), R2024 dùng .IntegerValue (int)
        // .Value available in both — use try/catch for safety
        try
        {
            return elem.Id.Value;
        }
        catch
        {
            return elem.Id.IntegerValue;
        }
    }

    #endregion

    #region Result Models

    public class AutoFillResult
    {
        public int ElementsProcessed { get; set; }
        public int ValuesWritten { get; set; }
        public List<string> Warnings { get; set; } = new();
    }

    public class WriteResult
    {
        public int ElementsUpdated { get; set; }
        public int ValuesWritten { get; set; }
        public int Errors { get; set; }
    }

    #endregion
}
