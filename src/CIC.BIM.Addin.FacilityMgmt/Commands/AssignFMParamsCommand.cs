using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.FacilityMgmt.Services;

namespace CIC.BIM.Addin.FacilityMgmt.Commands;

/// <summary>
/// Command: Gán tham số FM
/// Creates shared parameter file and binds 8 FM parameters to MEP categories.
/// </summary>
[Transaction(TransactionMode.Manual)]
public class AssignFMParamsCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        var doc = commandData.Application.ActiveUIDocument.Document;
        var app = commandData.Application.Application;
        var log = new System.Text.StringBuilder();

        try
        {
            // Step 1: Create shared param file with correct format
            log.AppendLine("▶ Bước 1: Tạo Shared Parameter file...");
            string paramFile;
            try
            {
                paramFile = ParameterService.EnsureSharedParamFile(app);
                log.AppendLine($"  ✓ File: {paramFile}");
            }
            catch (Exception ex)
            {
                log.AppendLine($"  ✗ Lỗi: {ex.Message}");
                TaskDialog.Show("CIC Tool - Debug", log.ToString());
                return Result.Failed;
            }

            // Step 2: Open the shared parameter file
            log.AppendLine("▶ Bước 2: Mở Shared Parameter file...");
            DefinitionFile? defFile;
            try
            {
                defFile = app.OpenSharedParameterFile();
                if (defFile == null)
                {
                    log.AppendLine("  ✗ OpenSharedParameterFile() trả về null");
                    // Show file content for debugging
                    try
                    {
                        var content = System.IO.File.ReadAllText(paramFile);
                        log.AppendLine($"  File content:\n{content}");
                    }
                    catch { }
                    TaskDialog.Show("CIC Tool - Debug", log.ToString());
                    return Result.Failed;
                }
                log.AppendLine($"  ✓ OK");
            }
            catch (Exception ex)
            {
                log.AppendLine($"  ✗ Lỗi: {ex.GetType().Name}: {ex.Message}");
                // Show file content for debugging
                try
                {
                    var content = System.IO.File.ReadAllText(paramFile);
                    log.AppendLine($"  File content ({content.Length} chars):\n{content.Substring(0, Math.Min(500, content.Length))}");
                }
                catch { }
                TaskDialog.Show("CIC Tool - Debug", log.ToString());
                return Result.Failed;
            }

            // Step 3: Get group
            log.AppendLine($"▶ Bước 3: Đọc group '{FMParameters.GroupName}'...");
            var group = defFile.Groups.get_Item(FMParameters.GroupName);
            if (group == null)
            {
                log.AppendLine($"  ✗ Group không tìm thấy trong file!");
                log.AppendLine($"  Các groups có: ");
                foreach (DefinitionGroup g in defFile.Groups)
                {
                    log.AppendLine($"    - {g.Name}");
                }
                TaskDialog.Show("CIC Tool - Debug", log.ToString());
                return Result.Failed;
            }
            log.AppendLine($"  ✓ Group có {group.Definitions.Size} definitions");

            // Step 4: Build category set
            log.AppendLine("▶ Bước 4: Categories...");
            var categories = doc.Settings.Categories;
            var catSet = app.Create.NewCategorySet();
            var catNames = new List<string>();

            foreach (var builtInCat in FMParameters.TargetCategories)
            {
                try
                {
                    var cat = categories.get_Item(builtInCat);
                    if (cat != null && cat.AllowsBoundParameters)
                    {
                        catSet.Insert(cat);
                        catNames.Add(cat.Name);
                    }
                }
                catch { }
            }
            log.AppendLine($"  ✓ {catSet.Size} categories");

            if (catSet.Size == 0)
            {
                log.AppendLine("  ✗ Không tìm thấy category MEP nào!");
                TaskDialog.Show("CIC Tool - Debug", log.ToString());
                return Result.Failed;
            }

            // Step 5: Bind parameters from file definitions
            log.AppendLine("▶ Bước 5: Bind parameters...");

            using var tx = new Transaction(doc, "CIC - Gán tham số Vận hành");
            tx.Start();

            int boundCount = 0;
            var bindingMap = doc.ParameterBindings;

            foreach (var paramDef in FMParameters.All)
            {
                try
                {
                    // Find definition in group
                    Definition? def = null;
                    foreach (Definition d in group.Definitions)
                    {
                        if (d.Name == paramDef.Name)
                        {
                            def = d;
                            break;
                        }
                    }

                    if (def == null)
                    {
                        log.AppendLine($"  ✗ Không tìm thấy: {paramDef.Name}");
                        continue;
                    }

                    // Check if already bound
                    var existing = bindingMap.get_Item(def);
                    if (existing != null)
                    {
                        log.AppendLine($"  ○ Đã bound: {paramDef.Name}");
                        continue;
                    }

                    // Bind new
                    var binding = app.Create.NewInstanceBinding(catSet);
                    if (bindingMap.Insert(def, binding, FMParameters.ParameterGroup))
                    {
                        boundCount++;
                        log.AppendLine($"  ✓ Bound: {paramDef.Name}");
                    }
                    else
                    {
                        log.AppendLine($"  ✗ Bind thất bại: {paramDef.Name}");
                    }
                }
                catch (Exception ex)
                {
                    log.AppendLine($"  ✗ {paramDef.Name}: {ex.Message}");
                }
            }

            tx.Commit();

            // Restore user's original shared param file
            ParameterService.RestoreOriginalParamFile(app);

            // Result
            log.AppendLine($"\n═══ KẾT QUẢ ═══");
            log.AppendLine($"✅ Gán mới: {boundCount} tham số");
            log.AppendLine($"📋 Tổng: {FMParameters.All.Length} tham số FM");
            log.AppendLine($"📁 Categories: {string.Join(", ", catNames)}");
            log.AppendLine($"\n💡 Chạy 'Điền dữ liệu Vận hành' để auto-fill.");

            TaskDialog.Show("CIC Tool - Gán tham số", log.ToString());
            return Result.Succeeded;
        }
        catch (Exception ex)
        {
            log.AppendLine($"\n❌ LỖI: {ex.GetType().Name}: {ex.Message}");
            message = ex.Message;
            TaskDialog.Show("CIC Tool - Lỗi", log.ToString());
            return Result.Failed;
        }
    }
}
