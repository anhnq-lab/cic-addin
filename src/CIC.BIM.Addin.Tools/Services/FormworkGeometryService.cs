using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tạo ván khuôn thật (DirectShape) từ các Face cấu kiện kết cấu.
/// Ván khuôn: tấm mỏng màu nâu, offset ra ngoài mặt cấu kiện.
/// Dùng TessellatedShapeBuilder (robust hơn CreateExtrusionGeometry).
/// </summary>
public static class FormworkGeometryService
{
    private const string MaterialName = "CIC_Ván khuôn";
    private const string DirectShapePrefix = "CIC_VK_";
    private const double DefaultThicknessMm = 18.0;
    private const double MmToFeet = 1.0 / 304.8;
    private const double SqFeetToSqM = 0.092903;
    private const double NormalTolerance = 0.8;

    #region Data Model

    public class FormworkCreationResult
    {
        public int Created { get; set; }
        public int FacesProcessed { get; set; }
        public int Failed { get; set; }
        public double TotalAreaSqM { get; set; }
        public List<ElementId> CreatedIds { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    #endregion

    #region Create Formwork

    /// <summary>
    /// Tạo DirectShape ván khuôn cho danh sách cấu kiện.
    /// </summary>
    public static FormworkCreationResult CreateFormwork(
        Document doc, IList<Element> elements,
        double thicknessMm = DefaultThicknessMm,
        Action<int, int>? progress = null)
    {
        var result = new FormworkCreationResult();
        var thickness = thicknessMm * MmToFeet;

        using var tx = new Transaction(doc, "CIC — Tạo ván khuôn");
        tx.Start();

        try
        {
            // Tạo/tìm material nâu
            var materialId = EnsureBrownMaterial(doc);
            var solidFillId = GetSolidFillPatternId(doc);
            int total = elements.Count;

            for (int i = 0; i < elements.Count; i++)
            {
                var elem = elements[i];
                progress?.Invoke(i + 1, total);

                try
                {
                    CreateFormworkForElement(doc, elem, thickness, materialId, solidFillId, result);
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    if (result.Errors.Count < 20)
                        result.Errors.Add($"[{elem.Name} ID:{elem.Id.Value}] {ex.Message}");
                }
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            if (tx.HasStarted() && !tx.HasEnded())
                tx.RollBack();
            result.Errors.Insert(0, $"CRITICAL: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Tạo DirectShape cho mỗi Face ván khuôn của 1 cấu kiện.
    /// Dùng CreateExtrusionGeometry cho face phẳng → solid sạch, không có đường tam giác.
    /// </summary>
    private static void CreateFormworkForElement(
        Document doc, Element elem, double thickness,
        ElementId materialId, ElementId solidFillId,
        FormworkCreationResult result)
    {
        var category = GetCategoryKey(elem);
        if (category == null) return;

        var solid = GetSolid(elem);
        if (solid == null || solid.Faces.Size == 0) return;

        // Thu thập tất cả formwork faces
        var formworkFaces = new List<Face>();
        foreach (Face face in solid.Faces)
        {
            if (ClassifyFace(face, category) == FaceRole.Formwork)
                formworkFaces.Add(face);
        }

        if (formworkFaces.Count == 0) return;

        // Tạo geometry cho mỗi face → list solids
        var geoList = new List<GeometryObject>();
        int facesAdded = 0;

        foreach (var face in formworkFaces)
        {
            try
            {
                // Chỉ xử lý PlanarFace — đảm bảo extrusion sạch
                if (face is PlanarFace planarFace)
                {
                    var curveLoops = planarFace.GetEdgesAsCurveLoops();
                    if (curveLoops == null || curveLoops.Count == 0) continue;

                    // Lấy normal hướng ra ngoài
                    var normal = planarFace.FaceNormal.Normalize();

                    // Tạo solid extrusion: đẩy theo hướng normal ra ngoài
                    var panelSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                        curveLoops, normal, thickness);

                    if (panelSolid != null && panelSolid.Volume > 0)
                    {
                        geoList.Add(panelSolid);
                        result.TotalAreaSqM += face.Area * SqFeetToSqM;
                        facesAdded++;
                    }
                }
                else
                {
                    // Non-planar face: dùng CurveLoop xấp xỉ từ edges
                    var curveLoops = face.GetEdgesAsCurveLoops();
                    if (curveLoops == null || curveLoops.Count == 0) continue;

                    // Dùng normal tại tâm face
                    var bb = face.GetBoundingBox();
                    var midUV = new UV(
                        (bb.Min.U + bb.Max.U) / 2,
                        (bb.Min.V + bb.Max.V) / 2);
                    var normal = face.ComputeNormal(midUV).Normalize();

                    try
                    {
                        var panelSolid = GeometryCreationUtilities.CreateExtrusionGeometry(
                            curveLoops, normal, thickness);

                        if (panelSolid != null && panelSolid.Volume > 0)
                        {
                            geoList.Add(panelSolid);
                            result.TotalAreaSqM += face.Area * SqFeetToSqM;
                            facesAdded++;
                        }
                    }
                    catch
                    {
                        // Non-planar extrusion failed, skip silently
                    }
                }
            }
            catch (Exception ex)
            {
                if (result.Errors.Count < 20)
                    result.Errors.Add($"  Face [{elem.Name}]: {ex.Message}");
            }
        }

        if (geoList.Count == 0) return;

        // Tạo DirectShape chứa tất cả panels
        var ds = DirectShape.CreateElement(
            doc, new ElementId(BuiltInCategory.OST_GenericModel));

        ds.SetShape(geoList);
        ds.Name = $"{DirectShapePrefix}{elem.Id.Value}";

        // Gán material trực tiếp lên DirectShape
        foreach (var geoObj in geoList)
        {
            if (geoObj is Solid s)
            {
                foreach (Face f in s.Faces)
                {
                    // Material đã set qua OverrideGraphicSettings
                }
            }
        }

        // Override graphics → màu nâu
        var view = doc.ActiveView;
        if (view != null)
        {
            var ogs = new OverrideGraphicSettings();
            // Surface — tô đặc màu nâu
            ogs.SetSurfaceForegroundPatternColor(new Color(139, 90, 43));
            ogs.SetSurfaceBackgroundPatternColor(new Color(139, 90, 43));
            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceBackgroundPatternId(solidFillId);
            }
            ogs.SetSurfaceTransparency(10);

            // Projection lines — tối thiểu
            ogs.SetProjectionLineColor(new Color(139, 90, 43));
            ogs.SetProjectionLineWeight(1);

            view.SetElementOverrides(ds.Id, ogs);
        }

        result.CreatedIds.Add(ds.Id);
        result.Created++;
        result.FacesProcessed += facesAdded;
    }

    #endregion

    #region Delete Formwork

    public static int DeleteAll(Document doc)
    {
        var toDelete = new FilteredElementCollector(doc)
            .OfClass(typeof(DirectShape))
            .WhereElementIsNotElementType()
            .Where(e => e.Name?.StartsWith(DirectShapePrefix) == true)
            .Select(e => e.Id)
            .ToList();

        if (toDelete.Count == 0) return 0;

        using var tx = new Transaction(doc, "CIC — Xóa ván khuôn");
        tx.Start();
        doc.Delete(toDelete);
        tx.Commit();

        return toDelete.Count;
    }

    public static int CountExisting(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(DirectShape))
            .WhereElementIsNotElementType()
            .Count(e => e.Name?.StartsWith(DirectShapePrefix) == true);
    }

    #endregion

    #region Material

    private static ElementId GetSolidFillPatternId(Document doc)
    {
        var solidFill = new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(fp => fp.GetFillPattern()?.IsSolidFill == true);

        return solidFill?.Id ?? ElementId.InvalidElementId;
    }

    private static ElementId EnsureBrownMaterial(Document doc)
    {
        var existing = new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .FirstOrDefault(m => m.Name == MaterialName);

        if (existing != null) return existing.Id;

        // Tạo material mới màu nâu gỗ
        var matId = Material.Create(doc, MaterialName);
        var mat = doc.GetElement(matId) as Material;
        if (mat != null)
        {
            mat.Color = new Color(139, 90, 43);       // Nâu gỗ
            mat.Transparency = 10;

            // Gán Solid Fill Pattern để material hiện đặc
            var solidFillId = GetSolidFillPatternId(doc);
            if (solidFillId != ElementId.InvalidElementId)
            {
                mat.SurfaceForegroundPatternId = solidFillId;
                mat.SurfaceForegroundPatternColor = new Color(139, 90, 43);
                mat.SurfaceBackgroundPatternId = solidFillId;
                mat.SurfaceBackgroundPatternColor = new Color(139, 90, 43);
                mat.CutForegroundPatternId = solidFillId;
                mat.CutForegroundPatternColor = new Color(139, 90, 43);
                mat.CutBackgroundPatternId = solidFillId;
                mat.CutBackgroundPatternColor = new Color(139, 90, 43);
            }
        }

        return matId;
    }

    #endregion

    #region Helpers

    private enum FaceRole { Formwork, Excluded }

    private static FaceRole ClassifyFace(Face face, string category)
    {
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
            "Dầm" => isTopFace ? FaceRole.Excluded : FaceRole.Formwork,
            "Cột" => isSideFace ? FaceRole.Formwork : FaceRole.Excluded,
            "Tường" => isSideFace ? FaceRole.Formwork : FaceRole.Excluded,
            "Sàn" => isBottomFace ? FaceRole.Formwork : FaceRole.Excluded,
            "Móng" => isTopFace ? FaceRole.Excluded : FaceRole.Formwork,
            _ => FaceRole.Excluded
        };
    }

    private static string? GetCategoryKey(Element elem)
    {
        var catId = elem.Category?.Id.Value;
        return catId switch
        {
            (long)BuiltInCategory.OST_StructuralFraming => "Dầm",
            (long)BuiltInCategory.OST_StructuralColumns => "Cột",
            (long)BuiltInCategory.OST_Walls => "Tường",
            (long)BuiltInCategory.OST_Floors => "Sàn",
            (long)BuiltInCategory.OST_StructuralFoundation => "Móng",
            _ => null
        };
    }

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

    #endregion
}
