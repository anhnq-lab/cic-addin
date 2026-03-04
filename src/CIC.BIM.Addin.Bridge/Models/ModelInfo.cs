using System.Collections.Generic;

namespace CIC.BIM.Addin.Bridge.Models;

public class ModelInfo
{
    public string Title { get; set; } = string.Empty;
    public string PathName { get; set; } = string.Empty;
    public List<string> Levels { get; set; } = new();
    public List<string> Phases { get; set; } = new();
    public int TotalElements { get; set; }
    public string ActiveViewName { get; set; } = string.Empty;
    public string Units { get; set; } = string.Empty;
}

public class ModelSummary
{
    public string Title { get; set; } = string.Empty;
    public List<CategoryCount> Categories { get; set; } = new();
    public int TotalElements { get; set; }
}

public class CategoryCount
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class ElementData
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public List<ParameterData> Parameters { get; set; } = new();
}

public class ParameterData
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string StorageType { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public bool IsShared { get; set; }
    public string GroupName { get; set; } = string.Empty;
}

public class LevelInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Elevation { get; set; }
}

public class RoomData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
    public double Area { get; set; }
    public double Perimeter { get; set; }
}

public class ViewData
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string ViewType { get; set; } = string.Empty;
    public string LevelName { get; set; } = string.Empty;
}

public class WarningData
{
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public List<int> ElementIds { get; set; } = new();
}

public class QtoResultData
{
    public string LevelName { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string FamilyAndType { get; set; } = string.Empty;
    public string SizeTag { get; set; } = string.Empty;
    public double VolumeM3 { get; set; }
    public double AreaM2 { get; set; }
    public double LengthM { get; set; }
    public int Count { get; set; }
}

public class ParamCheckResult
{
    public int ElementId { get; set; }
    public string ElementName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public List<string> MissingParams { get; set; } = new();
    public List<string> EmptyParams { get; set; } = new();
}

public class NamingCheckResult
{
    public int ElementId { get; set; }
    public string ElementName { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool MatchesPattern { get; set; }
}

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public int Count { get; set; }
}
