using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Service tự động nối (Join/UnJoin/SwitchJoin) cấu kiện trong Revit.
/// </summary>
public class AutoJointService
{
    #region Data Models

    /// <summary>Quy tắc ưu tiên nối: Category cắt → Category bị cắt.</summary>
    public class JoinRule
    {
        public BuiltInCategory CuttingCategory { get; set; }
        public BuiltInCategory CutCategory { get; set; }
        public string CuttingName { get; set; } = "";
        public string CutName { get; set; } = "";
    }

    /// <summary>Phạm vi áp dụng.</summary>
    public enum JoinScope
    {
        CurrentView,    // View hiện tại
        EntireProject,  // Toàn bộ dự án
        SelectedElements, // Đối tượng đã chọn
        PickRegion      // Quét chọn vùng
    }

    /// <summary>Kết quả xử lý.</summary>
    public class JoinResult
    {
        public int Joined { get; set; }
        public int Unjoined { get; set; }
        public int Switched { get; set; }
        public int AlreadyJoined { get; set; }
        public int BeamEndsConnected { get; set; }
        public int Failed { get; set; }
        public int TotalPairs { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>Category hỗ trợ Auto Joint.</summary>
    public static readonly (BuiltInCategory Cat, string Name)[] SupportedCategories = new[]
    {
        (BuiltInCategory.OST_StructuralColumns,    "Cột"),
        (BuiltInCategory.OST_StructuralFraming,    "Dầm"),
        (BuiltInCategory.OST_Walls,                "Tường"),
        (BuiltInCategory.OST_Floors,               "Sàn"),
        (BuiltInCategory.OST_StructuralFoundation, "Móng"),
        (BuiltInCategory.OST_Ceilings,             "Trần"),
        (BuiltInCategory.OST_Roofs,                "Mái"),
    };

    /// <summary>Quy tắc mặc định (Cột cắt Dầm, Cột cắt Tường, Dầm cắt Sàn...)</summary>
    public static List<JoinRule> DefaultRules => new()
    {
        // Dầm↔Dầm xử lý riêng bằng StructuralFramingUtils (end join)
        new JoinRule { CuttingCategory = BuiltInCategory.OST_StructuralColumns,    CutCategory = BuiltInCategory.OST_StructuralFraming,    CuttingName = "Cột", CutName = "Dầm" },
        new JoinRule { CuttingCategory = BuiltInCategory.OST_StructuralColumns,    CutCategory = BuiltInCategory.OST_Walls,                CuttingName = "Cột", CutName = "Tường" },
        new JoinRule { CuttingCategory = BuiltInCategory.OST_StructuralColumns,    CutCategory = BuiltInCategory.OST_Floors,               CuttingName = "Cột", CutName = "Sàn" },
        new JoinRule { CuttingCategory = BuiltInCategory.OST_StructuralFraming,    CutCategory = BuiltInCategory.OST_Walls,                CuttingName = "Dầm", CutName = "Tường" },
        new JoinRule { CuttingCategory = BuiltInCategory.OST_StructuralFraming,    CutCategory = BuiltInCategory.OST_Floors,               CuttingName = "Dầm", CutName = "Sàn" },
        new JoinRule { CuttingCategory = BuiltInCategory.OST_Walls,                CutCategory = BuiltInCategory.OST_Floors,               CuttingName = "Tường", CutName = "Sàn" },
        new JoinRule { CuttingCategory = BuiltInCategory.OST_StructuralFoundation, CutCategory = BuiltInCategory.OST_StructuralColumns,    CuttingName = "Móng", CutName = "Cột" },
    };

    #endregion

    #region Core — Join / Switch Join

    /// <summary>
    /// Nối tất cả cặp cấu kiện giao nhau theo quy tắc + đổi thứ tự ưu tiên.
    /// Bước 1: Xử lý kết nối đầu dầm (beam end joins) bằng StructuralFramingUtils.
    /// Bước 2: JoinGeometry cho các cặp khác category (cột-dầm, dầm-sàn...).
    /// </summary>
    public static JoinResult JoinAndSwitch(Document doc, List<JoinRule> rules,
        List<Element> elements, Action<int, int>? progressCallback = null)
    {
        var result = new JoinResult();

        // Nhóm elements theo category
        var byCategory = elements
            .Where(e => e.Category != null)
            .GroupBy(e => (BuiltInCategory)e.Category.Id.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Lọc rules: tách riêng beam-beam
        var geometryRules = rules.Where(r => r.CuttingCategory != r.CutCategory).ToList();
        bool hasBeams = byCategory.ContainsKey(BuiltInCategory.OST_StructuralFraming);

        // Tính tổng số cặp
        int totalWork = 0;
        foreach (var rule in geometryRules)
        {
            if (byCategory.ContainsKey(rule.CuttingCategory) && byCategory.ContainsKey(rule.CutCategory))
                totalWork += byCategory[rule.CuttingCategory].Count * byCategory[rule.CutCategory].Count;
        }
        if (hasBeams)
        {
            int beamCount = byCategory[BuiltInCategory.OST_StructuralFraming].Count;
            totalWork += beamCount;
        }
        result.TotalPairs = totalWork;

        using var tx = new Transaction(doc, "CIC — Tự động nối cấu kiện");
        tx.Start();

        int processed = 0;

        // ═══ BƯỚC 1: LUÔN kết nối đầu dầm khi có dầm ═══
        if (hasBeams)
        {
            var beams = byCategory[BuiltInCategory.OST_StructuralFraming];
            foreach (var beam in beams)
            {
                processed++;
                if (processed % 20 == 0)
                    progressCallback?.Invoke(processed, totalWork);

                if (beam is not FamilyInstance fi) continue;

                try
                {
                    // Cho phép join ở cả 2 đầu dầm (start=0, end=1)
                    StructuralFramingUtils.AllowJoinAtEnd(fi, 0);
                    StructuralFramingUtils.AllowJoinAtEnd(fi, 1);
                    result.BeamEndsConnected++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    if (result.Errors.Count < 10)
                        result.Errors.Add($"Beam end join: {ex.Message}");
                }
            }

            // Sau khi cho phép join đầu, thử JoinGeometry cho các cặp dầm giao nhau
            var processedPairs = new HashSet<(long, long)>();
            for (int i = 0; i < beams.Count; i++)
            {
                var beamA = beams[i];
                var bbA = beamA.get_BoundingBox(null);
                if (bbA == null) continue;

                for (int j = i + 1; j < beams.Count; j++)
                {
                    var beamB = beams[j];
                    var bbB = beamB.get_BoundingBox(null);
                    if (bbB == null || !BoundingBoxesIntersect(bbA, bbB))
                        continue;

                    try
                    {
                        if (!JoinGeometryUtils.AreElementsJoined(doc, beamA, beamB))
                        {
                            JoinGeometryUtils.JoinGeometry(doc, beamA, beamB);
                            result.Joined++;
                        }
                        else
                        {
                            result.AlreadyJoined++;
                        }
                    }
                    catch { /* Không phải tất cả cặp dầm đều join được */ }
                }
            }
        }

        // ═══ BƯỚC 2: JoinGeometry cho các cặp khác category ═══
        // Force rejoin: unjoin → rejoin → set cutting order (để Revit tính lại geometry)
        foreach (var rule in geometryRules)
        {
            if (!byCategory.ContainsKey(rule.CuttingCategory) ||
                !byCategory.ContainsKey(rule.CutCategory))
                continue;

            var cuttingElements = byCategory[rule.CuttingCategory];
            var cutElements = byCategory[rule.CutCategory];

            foreach (var cutting in cuttingElements)
            {
                var cuttingBB = cutting.get_BoundingBox(null);
                if (cuttingBB == null) continue;

                foreach (var cut in cutElements)
                {
                    processed++;
                    if (processed % 50 == 0)
                        progressCallback?.Invoke(processed, totalWork);

                    if (cutting.Id == cut.Id) continue;

                    // BoundingBox pre-filter
                    var cutBB = cut.get_BoundingBox(null);
                    if (cutBB == null || !BoundingBoxesIntersect(cuttingBB, cutBB))
                        continue;

                    try
                    {
                        bool alreadyJoined = JoinGeometryUtils.AreElementsJoined(doc, cutting, cut);

                        if (alreadyJoined)
                        {
                            // Đã join → LUÔN ép đúng cutting order
                            // SwitchJoinOrder toggle thứ tự. Gọi 1 lần = đổi chiều.
                            try
                            {
                                JoinGeometryUtils.SwitchJoinOrder(doc, cutting, cut);
                                result.Switched++;
                            }
                            catch
                            {
                                result.AlreadyJoined++;
                            }
                        }
                        else
                        {
                            // Chưa join → join mới
                            JoinGeometryUtils.JoinGeometry(doc, cutting, cut);
                            result.Joined++;

                            if (!JoinGeometryUtils.IsCuttingElementInJoin(doc, cutting, cut))
                            {
                                JoinGeometryUtils.SwitchJoinOrder(doc, cutting, cut);
                                result.Switched++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        if (result.Errors.Count < 10)
                            result.Errors.Add($"{rule.CuttingName}↔{rule.CutName}: {ex.Message}");
                    }
                }
            }
        }

        tx.Commit();
        progressCallback?.Invoke(totalWork, totalWork);
        return result;
    }

    #endregion

    #region Core — UnJoin

    /// <summary>Bỏ nối tất cả cặp cấu kiện theo quy tắc.</summary>
    public static JoinResult UnjoinAll(Document doc, List<JoinRule> rules,
        List<Element> elements, Action<int, int>? progressCallback = null)
    {
        var result = new JoinResult();

        var byCategory = elements
            .Where(e => e.Category != null)
            .GroupBy(e => (BuiltInCategory)e.Category.Id.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        int totalWork = 0;
        foreach (var rule in rules)
        {
            if (byCategory.ContainsKey(rule.CuttingCategory) && byCategory.ContainsKey(rule.CutCategory))
            {
                int countA = byCategory[rule.CuttingCategory].Count;
                int countB = byCategory[rule.CutCategory].Count;
                if (rule.CuttingCategory == rule.CutCategory)
                    totalWork += countA * (countA - 1) / 2;
                else
                    totalWork += countA * countB;
            }
        }
        result.TotalPairs = totalWork;

        var processedPairs = new HashSet<(long, long)>();

        using var tx = new Transaction(doc, "CIC — Bỏ nối cấu kiện");
        tx.Start();

        int processed = 0;

        foreach (var rule in rules)
        {
            if (!byCategory.ContainsKey(rule.CuttingCategory) ||
                !byCategory.ContainsKey(rule.CutCategory))
                continue;

            var cuttingElements = byCategory[rule.CuttingCategory];
            var cutElements = byCategory[rule.CutCategory];
            bool isSameCategory = rule.CuttingCategory == rule.CutCategory;

            foreach (var cutting in cuttingElements)
            {
                var cuttingBB = cutting.get_BoundingBox(null);
                if (cuttingBB == null) continue;

                foreach (var cut in cutElements)
                {
                    processed++;
                    if (processed % 50 == 0)
                        progressCallback?.Invoke(processed, totalWork);

                    if (cutting.Id == cut.Id) continue;

                    // Cùng category → bỏ qua cặp đã xử lý
                    if (isSameCategory)
                    {
                        long idA = cutting.Id.Value;
                        long idB = cut.Id.Value;
                        var pairKey = idA < idB ? (idA, idB) : (idB, idA);
                        if (!processedPairs.Add(pairKey))
                            continue;
                    }

                    var cutBB = cut.get_BoundingBox(null);
                    if (cutBB == null || !BoundingBoxesIntersect(cuttingBB, cutBB))
                        continue;

                    try
                    {
                        if (JoinGeometryUtils.AreElementsJoined(doc, cutting, cut))
                        {
                            JoinGeometryUtils.UnjoinGeometry(doc, cutting, cut);
                            result.Unjoined++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Failed++;
                        if (result.Errors.Count < 10)
                            result.Errors.Add($"UnJoin {rule.CuttingName}↔{rule.CutName}: {ex.Message}");
                    }
                }
            }
        }

        tx.Commit();
        progressCallback?.Invoke(totalWork, totalWork);
        return result;
    }

    #endregion

    #region Collect Elements by Scope

    /// <summary>Thu thập cấu kiện theo phạm vi.</summary>
    public static List<Element> CollectByScope(Document doc, UIDocument uiDoc,
        JoinScope scope, IEnumerable<BuiltInCategory> categories)
    {
        var catList = categories.ToList();
        var elements = new List<Element>();

        switch (scope)
        {
            case JoinScope.CurrentView:
                foreach (var cat in catList)
                {
                    elements.AddRange(new FilteredElementCollector(doc, doc.ActiveView.Id)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToList());
                }
                break;

            case JoinScope.EntireProject:
                foreach (var cat in catList)
                {
                    elements.AddRange(new FilteredElementCollector(doc)
                        .OfCategory(cat)
                        .WhereElementIsNotElementType()
                        .ToList());
                }
                break;

            case JoinScope.SelectedElements:
                var selectedIds = uiDoc.Selection.GetElementIds();
                foreach (var id in selectedIds)
                {
                    var elem = doc.GetElement(id);
                    if (elem?.Category != null &&
                        catList.Contains((BuiltInCategory)elem.Category.Id.Value))
                        elements.Add(elem);
                }
                break;

            case JoinScope.PickRegion:
                try
                {
                    var refs = uiDoc.Selection.PickObjects(
                        ObjectType.Element,
                        "Quét chọn các cấu kiện cần nối");
                    foreach (var r in refs)
                    {
                        var elem = doc.GetElement(r.ElementId);
                        if (elem?.Category != null &&
                            catList.Contains((BuiltInCategory)elem.Category.Id.Value))
                            elements.Add(elem);
                    }
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException) { }
                break;
        }

        return elements;
    }

    #endregion

    #region Helpers

    /// <summary>Kiểm tra 2 BoundingBox có giao nhau không (3D).</summary>
    private static bool BoundingBoxesIntersect(BoundingBoxXYZ a, BoundingBoxXYZ b)
    {
        // Tolerance rộng (~15mm ~ 0.05 feet) để bắt các cặp ở góc/đầu dầm
        const double tol = 0.05;
        return a.Min.X - tol <= b.Max.X && a.Max.X + tol >= b.Min.X &&
               a.Min.Y - tol <= b.Max.Y && a.Max.Y + tol >= b.Min.Y &&
               a.Min.Z - tol <= b.Max.Z && a.Max.Z + tol >= b.Min.Z;
    }

    /// <summary>Lấy chiều dài element (dùng cho heuristic cùng category: dài hơn = cutting).</summary>
    private static double GetElementLength(Element elem)
    {
        // Thử lấy từ parameter Length
        var lengthParam = elem.get_Parameter(BuiltInParameter.INSTANCE_LENGTH_PARAM)
                       ?? elem.get_Parameter(BuiltInParameter.STRUCTURAL_FRAME_CUT_LENGTH)
                       ?? elem.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
        if (lengthParam != null && lengthParam.HasValue)
            return lengthParam.AsDouble();

        // Fallback: tính từ BoundingBox diagonal trên mặt phẳng XY
        var bb = elem.get_BoundingBox(null);
        if (bb != null)
        {
            double dx = bb.Max.X - bb.Min.X;
            double dy = bb.Max.Y - bb.Min.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        return 0;
    }

    #endregion
}
