using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;

namespace CIC.BIM.Addin.Tools.Services;

/// <summary>
/// Tự động sửa lỗi Room Boundary:
/// 1. Xóa Room Separation Lines trùng
/// 2. Xóa Rooms lỗi (Area=0)
/// 3. Xóa tường trùng (cùng type + chồng nhau)
/// 4. Unjoin tường khác type chồng nhau
/// </summary>
public class AutoFixRoomBoundaryService
{
    public class FixResult
    {
        public int SepLinesDeleted { get; set; }
        public int RedundantRoomsDeleted { get; set; }
        public int WallsDeleted { get; set; }
        public int OverlappingWallPairs { get; set; }
        public List<string> Messages { get; set; } = new();
        public List<int> ProblematicWallIds { get; set; } = new();
    }

    public FixResult Execute(Document doc, ElementId levelId)
    {
        var result = new FixResult();
        var level = doc.GetElement(levelId) as Level;
        if (level == null) { result.Messages.Add("❌ Level not found."); return result; }

        result.Messages.Add($"🔧 Fixing Room Boundary: {level.Name}");

        FixSepLines(doc, level, result);
        FixBadRooms(doc, level, result);
        FixOverlappingWalls(doc, level, result);

        // Cleanup rooms again after wall fix
        int extra = QuickCleanRooms(doc, level);
        if (extra > 0)
        {
            result.RedundantRoomsDeleted += extra;
            result.Messages.Add($"  🗑️ Extra cleanup: {extra} rooms.");
        }

        result.Messages.Add($"✅ Done: -{result.SepLinesDeleted} sep lines, -{result.WallsDeleted} walls, " +
            $"-{result.RedundantRoomsDeleted} rooms. {result.OverlappingWallPairs} diff-type pairs remain.");
        return result;
    }

    private void FixSepLines(Document doc, Level level, FixResult result)
    {
        using var tx = new Transaction(doc, "Fix sep lines");
        tx.Start();
        try
        {
            var seps = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_RoomSeparationLines)
                .WhereElementIsNotElementType().ToElements()
                .Where(e => e is ModelCurve && MatchLevel(doc, e, level)).ToList();

            if (seps.Count == 0) { tx.RollBack(); return; }

            // Group by signature to find duplicates
            var groups = new Dictionary<string, List<Element>>();
            foreach (var el in seps)
            {
                var loc = el.Location as LocationCurve;
                if (loc?.Curve == null) continue;
                var c = loc.Curve;
                var mid = c.Evaluate(0.5, true);
                var dir = (c.GetEndPoint(1) - c.GetEndPoint(0)).Normalize();
                if (dir.X < -0.001 || (Math.Abs(dir.X) < 0.001 && dir.Y < -0.001)) dir = dir.Negate();
                string key = $"{R(mid.X)},{R(mid.Y)},{R(dir.X)},{R(dir.Y)},{R(c.Length)}";
                if (!groups.ContainsKey(key)) groups[key] = new List<Element>();
                groups[key].Add(el);
            }

            var toDelete = new HashSet<ElementId>();
            foreach (var g in groups.Values)
                for (int i = 1; i < g.Count; i++) toDelete.Add(g[i].Id);

            // Sep lines overlapping walls
            var walls = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall)).WhereElementIsNotElementType()
                .Cast<Wall>().Where(w => w.LevelId == level.Id).ToList();

            foreach (var sep in seps)
            {
                if (toDelete.Contains(sep.Id)) continue;
                var sLoc = sep.Location as LocationCurve;
                if (sLoc?.Curve == null) continue;
                var sMid = sLoc.Curve.Evaluate(0.5, true);
                var sDir = (sLoc.Curve.GetEndPoint(1) - sLoc.Curve.GetEndPoint(0)).Normalize();

                foreach (var w in walls)
                {
                    var wLoc = w.Location as LocationCurve;
                    if (wLoc?.Curve == null) continue;
                    var wDir = (wLoc.Curve.GetEndPoint(1) - wLoc.Curve.GetEndPoint(0)).Normalize();
                    if (Math.Abs(sDir.DotProduct(wDir)) < 0.99) continue;
                    var wMid = wLoc.Curve.Evaluate(0.5, true);
                    double dist = new XYZ(sMid.X - wMid.X, sMid.Y - wMid.Y, 0).GetLength();
                    if (dist < w.Width + 0.05 && sLoc.Curve.Length <= wLoc.Curve.Length * 1.1)
                    { toDelete.Add(sep.Id); break; }
                }
            }

            foreach (var id in toDelete)
                try { doc.Delete(id); result.SepLinesDeleted++; } catch { }

            result.Messages.Add(result.SepLinesDeleted > 0
                ? $"  🗑️ Deleted {result.SepLinesDeleted} duplicate sep lines."
                : "  ✅ No duplicate sep lines.");
            tx.Commit();
        }
        catch (Exception ex)
        {
            result.Messages.Add($"  ⚠️ Sep lines error: {ex.Message}");
            if (tx.HasStarted()) tx.RollBack();
        }
    }

    private void FixBadRooms(Document doc, Level level, FixResult result)
    {
        using var tx = new Transaction(doc, "Fix bad rooms");
        tx.Start();
        try
        {
            int del = 0;
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .Cast<Room>().Where(r => r.LevelId == level.Id && r.Area <= 0).ToList();
            foreach (var r in rooms) try { doc.Delete(r.Id); del++; } catch { }
            result.RedundantRoomsDeleted = del;
            result.Messages.Add(del > 0 ? $"  🗑️ Deleted {del} bad rooms." : "  ✅ No bad rooms.");
            tx.Commit();
        }
        catch (Exception ex)
        {
            result.Messages.Add($"  ⚠️ Rooms error: {ex.Message}");
            if (tx.HasStarted()) tx.RollBack();
        }
    }

    private int QuickCleanRooms(Document doc, Level level)
    {
        using var tx = new Transaction(doc, "Quick clean rooms");
        tx.Start();
        int del = 0;
        try
        {
            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .Cast<Room>().Where(r => r.LevelId == level.Id && r.Area <= 0).ToList();
            foreach (var r in rooms) try { doc.Delete(r.Id); del++; } catch { }
            tx.Commit();
        }
        catch { if (tx.HasStarted()) tx.RollBack(); }
        return del;
    }

    /// <summary>
    /// Fix overlapping walls:
    /// - Same type: delete shorter wall (duplicate)
    /// - Different type: unjoin geometry
    /// </summary>
    private void FixOverlappingWalls(Document doc, Level level, FixResult result)
    {
        // Collect walls safely — skip any that throw
        var walls = new List<Wall>();
        try
        {
            var collector = new FilteredElementCollector(doc)
                .OfClass(typeof(Wall)).WhereElementIsNotElementType()
                .Cast<Wall>().ToList();

            foreach (var w in collector)
            {
                try
                {
                    if (!w.IsValidObject) continue;
                    if (w.LevelId != level.Id) continue;
                    // Skip curtain walls — they don't have Width
                    if (w.WallType.Kind == WallKind.Curtain) continue;
                    // Test accessing Location to catch invalid refs early
                    var loc = w.Location as LocationCurve;
                    if (loc?.Curve == null) continue;
                    walls.Add(w);
                }
                catch { /* skip invalid wall */ }
            }
        }
        catch (Exception ex)
        {
            result.Messages.Add($"  ⚠️ Error collecting walls: {ex.Message}");
            return;
        }

        result.Messages.Add($"  ℹ️ Found {walls.Count} valid walls on level.");

        // Find overlapping pairs — all access wrapped in try-catch
        var pairs = new List<(Wall a, Wall b, double overlap)>();
        for (int i = 0; i < walls.Count; i++)
        {
            try
            {
                var wA = walls[i];
                if (!wA.IsValidObject) continue;
                var lA = wA.Location as LocationCurve;
                if (lA?.Curve == null) continue;
                var sA = lA.Curve.GetEndPoint(0);
                var eA = lA.Curve.GetEndPoint(1);
                var dA = (eA - sA).Normalize();
                var mA = lA.Curve.Evaluate(0.5, true);
                double widthA = wA.Width;

                for (int j = i + 1; j < walls.Count; j++)
                {
                    try
                    {
                        var wB = walls[j];
                        if (!wB.IsValidObject) continue;
                        var lB = wB.Location as LocationCurve;
                        if (lB?.Curve == null) continue;
                        var sB = lB.Curve.GetEndPoint(0);
                        var eB = lB.Curve.GetEndPoint(1);
                        var dB = (eB - sB).Normalize();

                        if (Math.Abs(dA.DotProduct(dB)) < 0.99) continue;

                        var mB = lB.Curve.Evaluate(0.5, true);
                        var perp = new XYZ(-dA.Y, dA.X, 0);
                        double perpDist = Math.Abs((mB - mA).DotProduct(perp));
                        if (perpDist > (widthA + wB.Width) / 2.0) continue;

                        double pSB = (sB - sA).DotProduct(dA);
                        double pEB = (eB - sA).DotProduct(dA);
                        double lenA = lA.Curve.Length;
                        double oS = Math.Max(0, Math.Min(pSB, pEB));
                        double oE = Math.Min(lenA, Math.Max(pSB, pEB));
                        if (oE - oS > 0.01)
                            pairs.Add((wA, wB, oE - oS));
                    }
                    catch { /* skip this pair */ }
                }
            }
            catch { /* skip this wall */ }
        }

        if (pairs.Count == 0)
        {
            result.Messages.Add("  ✅ No overlapping walls found.");
            return;
        }

        result.Messages.Add($"  ℹ️ Found {pairs.Count} overlapping wall pairs.");

        var deleted = new HashSet<ElementId>();
        int sameFixed = 0, diffFixed = 0;
        var diffIds = new HashSet<int>();

        using var tx = new Transaction(doc, "Fix overlapping walls");
        tx.Start();
        try
        {
            pairs.Sort((a, b) => b.overlap.CompareTo(a.overlap));

            foreach (var (wA, wB, _) in pairs)
            {
                try
                {
                    if (!wA.IsValidObject || !wB.IsValidObject) continue;
                    if (deleted.Contains(wA.Id) || deleted.Contains(wB.Id)) continue;

                    if (wA.WallType.Id == wB.WallType.Id)
                    {
                        var lA = (wA.Location as LocationCurve)?.Curve?.Length ?? 0;
                        var lB = (wB.Location as LocationCurve)?.Curve?.Length ?? 0;
                        var toDelete = lA >= lB ? wB : wA;
                        try { doc.Delete(toDelete.Id); deleted.Add(toDelete.Id); result.WallsDeleted++; sameFixed++; }
                        catch { }
                    }
                    else
                    {
                        try
                        {
                            if (JoinGeometryUtils.AreElementsJoined(doc, wA, wB))
                                JoinGeometryUtils.UnjoinGeometry(doc, wA, wB);
                        }
                        catch { }
                        diffFixed++;
                        diffIds.Add(wA.Id.IntegerValue);
                        diffIds.Add(wB.Id.IntegerValue);
                    }
                }
                catch { /* skip this pair */ }
            }
            tx.Commit();
        }
        catch (Exception ex)
        {
            result.Messages.Add($"  ⚠️ Wall fix transaction error: {ex.Message}");
            if (tx.HasStarted()) tx.RollBack();
        }

        result.OverlappingWallPairs = diffFixed;
        result.ProblematicWallIds = diffIds.ToList();

        if (sameFixed > 0) result.Messages.Add($"  🗑️ Deleted {sameFixed} duplicate walls (same type).");
        if (diffFixed > 0) result.Messages.Add($"  ⚠️ Unjoined {diffFixed} diff-type wall pairs.");
    }

    private static bool MatchLevel(Document doc, Element e, Level level)
    {
        if (e.LevelId != ElementId.InvalidElementId)
            return (doc.GetElement(e.LevelId) as Level)?.Id == level.Id;
        var loc = e.Location as LocationCurve;
        if (loc?.Curve != null)
            return Math.Abs(loc.Curve.GetEndPoint(0).Z - level.Elevation) < 0.1;
        return false;
    }

    private static double R(double v) => Math.Round(v, 3);
}
