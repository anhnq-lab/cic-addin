using Autodesk.Revit.DB;

namespace CIC.BIM.Addin.Tools.Views;

/// <summary>
/// Helper record cho Level ComboBox items.
/// </summary>
public record LevelItem(ElementId Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// Helper record cho CAD Link ComboBox items.
/// </summary>
public record CadLinkItem(ElementId Id, string FileName, ElementId? RevitLinkInstanceId = null)
{
    public override string ToString() => FileName;
}
