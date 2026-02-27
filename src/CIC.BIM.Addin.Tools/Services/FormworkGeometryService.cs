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
    /// Dùng Mesh triangulation + offset theo normal → tạo thin solid từ triangles.
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

        // Tạo TessellatedShapeBuilder cho tất cả formwork panels
        var builder = new TessellatedShapeBuilder();
        builder.OpenConnectedFaceSet(false);

        int facesAdded = 0;

        foreach (var face in formworkFaces)
        {
            try
            {
                // Triangulate face
                var mesh = face.Triangulate(0.5); // Level of detail
                if (mesh == null || mesh.NumTriangles == 0) continue;

                // Lấy normal
                var bb = face.GetBoundingBox();
                var midUV = new UV(
                    (bb.Min.U + bb.Max.U) / 2,
                    (bb.Min.V + bb.Max.V) / 2);
                var normal = face.ComputeNormal(midUV).Normalize();
                var offset = normal.Multiply(thickness);

                // Tạo thin prism cho mỗi triangle
                for (int t = 0; t < mesh.NumTriangles; t++)
                {
                    var tri = mesh.get_Triangle(t);
                    var p0 = tri.get_Vertex(0);
                    var p1 = tri.get_Vertex(1);
                    var p2 = tri.get_Vertex(2);

                    // Offset vertices
                    var p0o = p0.Add(offset);
                    var p1o = p1.Add(offset);
                    var p2o = p2.Add(offset);

                    // Mặt ngoài (original face side)
                    builder.AddFace(new TessellatedFace(
                        new List<XYZ> { p0, p1, p2 }, materialId));

                    // Mặt trong (offset side)
                    builder.AddFace(new TessellatedFace(
                        new List<XYZ> { p2o, p1o, p0o }, materialId));

                    // 3 mặt bên
                    builder.AddFace(new TessellatedFace(
                        new List<XYZ> { p0, p0o, p1o, p1 }, materialId));
                    builder.AddFace(new TessellatedFace(
                        new List<XYZ> { p1, p1o, p2o, p2 }, materialId));
                    builder.AddFace(new TessellatedFace(
                        new List<XYZ> { p2, p2o, p0o, p0 }, materialId));
                }

                result.TotalAreaSqM += face.Area * SqFeetToSqM;
                facesAdded++;
            }
            catch (Exception ex)
            {
                if (result.Errors.Count < 20)
                    result.Errors.Add($"  Face: {ex.Message}");
            }
        }

        builder.CloseConnectedFaceSet();

        if (facesAdded == 0) return;

        builder.Target = TessellatedShapeBuilderTarget.Mesh;
        builder.Fallback = TessellatedShapeBuilderFallback.Salvage;
        builder.Build();

        var builderResult = builder.GetBuildResult();
        var geoObjects = builderResult.GetGeometricalObjects();

        if (geoObjects == null || geoObjects.Count == 0)
        {
            result.Errors.Add($"[{elem.Name}] TessellatedShapeBuilder returned empty");
            return;
        }

        // Tạo DirectShape
        var ds = DirectShape.CreateElement(
            doc, new ElementId(BuiltInCategory.OST_GenericModel));

        ds.SetShape(geoObjects.ToList());
        ds.Name = $"{DirectShapePrefix}{elem.Id.Value}";

        // Override graphics → màu nâu
        var view = doc.ActiveView;
        if (view != null)
        {
            var ogs = new OverrideGraphicSettings();
            ogs.SetSurfaceForegroundPatternColor(new Color(139, 90, 43));
            ogs.SetSurfaceBackgroundPatternColor(new Color(139, 90, 43));
            ogs.SetProjectionLineColor(new Color(101, 67, 33));
            if (solidFillId != ElementId.InvalidElementId)
            {
                ogs.SetSurfaceForegroundPatternId(solidFillId);
                ogs.SetSurfaceBackgroundPatternId(solidFillId);
            }
            ogs.SetSurfaceTransparency(10);
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
