using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Windows;

[assembly: ExtensionApplication(typeof(CIC.BIM.Addin.CAD.CICCadApp))]
[assembly: CommandClass(typeof(CIC.BIM.Addin.CAD.CICCadApp))]

namespace CIC.BIM.Addin.CAD
{
    public class CICCadApp : IExtensionApplication
    {
        public void Initialize()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage("\n[CIC] CIC Tools for AutoCAD loaded! ✓");
            Autodesk.AutoCAD.ApplicationServices.Application.Idle += OnIdle;
        }

        public void Terminate() { }

        private void OnIdle(object sender, System.EventArgs e)
        {
            Autodesk.AutoCAD.ApplicationServices.Application.Idle -= OnIdle;
            CreateRibbon();
        }

        // ══════════ COMMAND (gõ lệnh) ══════════

        [CommandMethod("CIC_EXPORTBLOCKS")]
        public void CmdExportBlocks()
        {
            RunExport();
        }

        // ══════════ RIBBON ══════════

        private void CreateRibbon()
        {
            try
            {
                var ribbon = ComponentManager.Ribbon;
                if (ribbon == null) return;

                foreach (var t in ribbon.Tabs)
                    if (t.Id == "CIC_TOOLS_TAB") return;

                var tab = new RibbonTab { Title = "CIC Tools", Id = "CIC_TOOLS_TAB" };
                var panel = new RibbonPanelSource { Title = "Block Export" };
                var panelItem = new RibbonPanel { Source = panel };

                var btnExport = new RibbonButton
                {
                    Text = "Export\nBlocks",
                    ShowText = true,
                    Size = RibbonItemSize.Large,
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    ToolTip = new RibbonToolTip
                    {
                        Title = "Export Dynamic Blocks → JSON",
                        Content = "Quét tất cả blocks (bao gồm dynamic block visibility states) và xuất file JSON để Revit đọc.",
                    }
                };
                // Gọi trực tiếp (không qua command line)
                btnExport.CommandHandler = new DirectCommandHandler();

                panel.Items.Add(btnExport);
                tab.Panels.Add(panelItem);
                ribbon.Tabs.Add(tab);

                var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage("\n[CIC] Tab 'CIC Tools' đã sẵn sàng trên Ribbon.");
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show($"CIC Ribbon Error: {ex.Message}", "CIC Tools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ══════════ EXPORT LOGIC ══════════

        public static void RunExport()
        {
            var doc = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                System.Windows.MessageBox.Show("Chưa mở bản vẽ nào!", "CIC Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var db = doc.Database;
            var ed = doc.Editor;

            ed.WriteMessage("\n[CIC] Đang quét Dynamic Blocks...");

            var blockMap = new Dictionary<string, BlockExportInfo>();
            int errorCount = 0;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in btr)
                {
                    var ent = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (ent == null) continue;

                    try
                    {
                        ProcessBlockReference(ent, tr, blockMap, 0, 0, 0, 1, 1);
                    }
                    catch (System.Exception ex)
                    {
                        errorCount++;
                        ed.WriteMessage($"\n[CIC] Lỗi: {ex.Message}");
                    }
                }

                tr.Commit();
            }

            if (blockMap.Count == 0)
            {
                System.Windows.MessageBox.Show(
                    "Không tìm thấy block nào trong bản vẽ.",
                    "CIC Export Blocks", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Export JSON
            var output = new BlockExportData
            {
                Source = Path.GetFileName(db.Filename),
                Exported = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
                Blocks = new List<BlockExportInfo>(blockMap.Values)
            };

            var jsonPath = Path.ChangeExtension(db.Filename, "_blocks.json");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(output, options);
            File.WriteAllText(jsonPath, json);

            int totalInstances = output.Blocks.Sum(b => b.Positions.Count);
            int dynamicCount = output.Blocks.Count(b => b.IsDynamic);

            // Thông báo kết quả
            var msg = $"✅ Export thành công!\n\n" +
                      $"📊 {blockMap.Count} blocks ({dynamicCount} dynamic)\n" +
                      $"📍 {totalInstances} instances\n\n" +
                      $"📁 {jsonPath}";

            ed.WriteMessage($"\n[CIC] ✅ Xuất {blockMap.Count} blocks, {totalInstances} instances → {jsonPath}");

            System.Windows.MessageBox.Show(msg, "CIC Export Blocks", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ══════════ BLOCK PROCESSING ══════════

        private static void ProcessBlockReference(BlockReference blkRef, Transaction tr,
            Dictionary<string, BlockExportInfo> blockMap,
            double parentX, double parentY, double parentRotation,
            double parentScaleX, double parentScaleY,
            int depth = 0)
        {
            if (depth > 10) return;

            string blockName;
            string parentName = "";
            bool isDynamic = false;

            try
            {
                if (blkRef.IsDynamicBlock)
                {
                    isDynamic = true;
                    var dynBtr = (BlockTableRecord)tr.GetObject(
                        blkRef.DynamicBlockTableRecord, OpenMode.ForRead);
                    parentName = dynBtr.Name;
                    blockName = GetEffectiveName(blkRef, parentName);
                }
                else
                {
                    var btr = (BlockTableRecord)tr.GetObject(blkRef.BlockTableRecord, OpenMode.ForRead);
                    blockName = btr.Name;
                }
            }
            catch
            {
                blockName = blkRef.Name;
            }

            if (string.IsNullOrEmpty(blockName)) return;
            if (blockName.StartsWith("*") && !blockName.StartsWith("*U", StringComparison.OrdinalIgnoreCase)) return;
            if (blockName.Contains("$")) return;

            var x = parentX + blkRef.Position.X * parentScaleX;
            var y = parentY + blkRef.Position.Y * parentScaleY;
            var rotation = parentRotation + blkRef.Rotation;
            var layerName = blkRef.Layer ?? "";

            if (!blockMap.ContainsKey(blockName))
            {
                blockMap[blockName] = new BlockExportInfo
                {
                    Name = blockName,
                    Parent = isDynamic ? parentName : null,
                    Layer = layerName,
                    IsDynamic = isDynamic,
                    Positions = new List<PositionInfo>()
                };
            }

            blockMap[blockName].Positions.Add(new PositionInfo
            {
                X = Math.Round(x, 2),
                Y = Math.Round(y, 2),
                Rotation = Math.Round(rotation * 180.0 / Math.PI, 2)
            });

            // Recurse — chỉ khi KHÔNG phải dynamic block
            // Dynamic block chứa nested geometry cho tất cả visibility states
            // → recurse sẽ đếm duplicate
            if (!isDynamic)
            {
                try
                {
                    var btrId = blkRef.BlockTableRecord;
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    foreach (ObjectId childId in btr)
                    {
                        var child = tr.GetObject(childId, OpenMode.ForRead) as BlockReference;
                        if (child != null)
                        {
                            ProcessBlockReference(child, tr, blockMap,
                                x, y, rotation,
                                parentScaleX * blkRef.ScaleFactors.X,
                                parentScaleY * blkRef.ScaleFactors.Y,
                                depth + 1);
                        }
                    }
                }
                catch { }
            }
        }

        private static string GetEffectiveName(BlockReference blkRef, string parentName)
        {
            try
            {
                var props = blkRef.DynamicBlockReferencePropertyCollection;
                if (props != null)
                {
                    foreach (DynamicBlockReferenceProperty prop in props)
                    {
                        if (prop.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0
                            || prop.PropertyName.Equals("Trạng thái hiển thị", StringComparison.OrdinalIgnoreCase))
                        {
                            var val = prop.Value?.ToString();
                            if (!string.IsNullOrEmpty(val))
                                return val;
                        }
                    }
                }
            }
            catch { }

            return parentName;
        }
    }

    // ══════════ RIBBON COMMAND HANDLER ══════════

    public class DirectCommandHandler : System.Windows.Input.ICommand
    {
#pragma warning disable 67
        public event System.EventHandler? CanExecuteChanged;
#pragma warning restore 67

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            try
            {
                CICCadApp.RunExport();
            }
            catch (System.Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Lỗi: {ex.Message}\n\n{ex.StackTrace}",
                    "CIC Tools Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // ══════════ JSON MODELS ══════════

    public class BlockExportData
    {
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("exported")] public string Exported { get; set; } = "";
        [JsonPropertyName("blocks")] public List<BlockExportInfo> Blocks { get; set; } = new();
    }

    public class BlockExportInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("parent")] public string? Parent { get; set; }
        [JsonPropertyName("layer")] public string Layer { get; set; } = "";
        [JsonPropertyName("isDynamic")] public bool IsDynamic { get; set; }
        [JsonPropertyName("positions")] public List<PositionInfo> Positions { get; set; } = new();
    }

    public class PositionInfo
    {
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("rotation")] public double Rotation { get; set; }
    }
}
