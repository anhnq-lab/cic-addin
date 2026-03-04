using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using CIC.BIM.Addin.Bridge.Models;
using CIC.BIM.Addin.Tools.Services;

namespace CIC.BIM.Addin.Bridge;

/// <summary>
/// Lightweight REST API server running inside Revit process.
/// Uses HttpListener to serve requests on localhost:52140.
/// All Revit API calls are routed through RevitApiHandler (ExternalEvent).
/// </summary>
public class RevitBridgeServer : IDisposable
{
    private const int Port = 52140;
    private const string Prefix = "http://localhost:52140/";

    private readonly RevitApiHandler _handler;
    private readonly ExternalEvent _externalEvent;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private Thread? _serverThread;

    public RevitBridgeServer(RevitApiHandler handler, ExternalEvent externalEvent)
    {
        _handler = handler;
        _externalEvent = externalEvent;
    }

    public void Start()
    {
        if (_listener != null) return;

        try
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add(Prefix);
            _listener.Start();

            _cts = new CancellationTokenSource();
            _serverThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "CIC.Bridge.Server"
            };
            _serverThread.Start();

            System.Diagnostics.Debug.WriteLine($"[CIC Bridge] Server started on {Prefix}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CIC Bridge] Failed to start: {ex.Message}");
            _listener = null;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();
        _listener = null;
        System.Diagnostics.Debug.WriteLine("[CIC Bridge] Server stopped.");
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }

    private void ListenLoop()
    {
        while (_listener != null && _listener.IsListening && !(_cts?.IsCancellationRequested ?? true))
        {
            try
            {
                var context = _listener.GetContext();
                // Handle each request on a thread pool thread
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
            catch (HttpListenerException)
            {
                // Expected when stopping
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CIC Bridge] Listen error: {ex.Message}");
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers for local development
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = 200;
            response.Close();
            return;
        }

        try
        {
            var path = request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
            var query = request.QueryString;

            object? result = path switch
            {
                "/health" => HandleHealth(),
                "/model/info" => HandleModelInfo().Result,
                "/model/summary" => HandleModelSummary().Result,
                "/elements" => HandleGetElements(query).Result,
                "/element" when query["id"] != null => HandleGetElement(query["id"]!).Result,
                "/parameters" => HandleGetParameters(query).Result,
                "/parameters/shared" => HandleGetSharedParams().Result,
                "/categories" => HandleGetCategories().Result,
                "/levels" => HandleGetLevels().Result,
                "/rooms" => HandleGetRooms(query).Result,
                "/views" => HandleGetViews(query).Result,
                "/qto" => HandleGetQto(query).Result,
                "/warnings" => HandleGetWarnings().Result,
                "/check/parameters" => HandleCheckParams(request).Result,
                "/check/naming" => HandleCheckNaming(request).Result,
                "/fix/room-boundary" => HandleFixRoomBoundary(request).Result,
                "/fix/rooms" => HandleCreateRooms(request).Result,
                "/fix/sep-lines" => HandleCreateSepLines(request).Result,
                _ => new ApiResponse<string> { Success = false, Error = $"Unknown endpoint: {path}" }
            };

            var json = SimpleJson.Serialize(result);
            var bytes = Encoding.UTF8.GetBytes(json);

            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        catch (Exception ex)
        {
            var error = SimpleJson.Serialize(new ApiResponse<string>
            {
                Success = false,
                Error = ex.InnerException?.Message ?? ex.Message
            });
            var bytes = Encoding.UTF8.GetBytes(error);
            response.StatusCode = 500;
            response.ContentType = "application/json; charset=utf-8";
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes, 0, bytes.Length);
        }
        finally
        {
            response.Close();
        }
    }

    // ═══════════════════════════════════════════════════════════════
    //  ENDPOINT HANDLERS
    // ═══════════════════════════════════════════════════════════════

    private object HandleHealth()
    {
        return new ApiResponse<object>
        {
            Success = true,
            Data = new { status = "ok", server = "CIC.BIM.Addin.Bridge", port = Port }
        };
    }

    private Task<object?> HandleModelInfo()
    {
        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<ModelInfo> { Success = false, Error = "No active document" };

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => l.Name)
                .ToList();

            var phases = doc.Phases.Cast<Phase>().Select(p => p.Name).ToList();

            var totalElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .GetElementCount();

            var activeView = doc.ActiveView?.Name ?? "N/A";

            return (object)new ApiResponse<ModelInfo>
            {
                Success = true,
                Data = new ModelInfo
                {
                    Title = doc.Title,
                    PathName = doc.PathName ?? "",
                    Levels = levels,
                    Phases = phases,
                    TotalElements = totalElements,
                    ActiveViewName = activeView,
                    Units = doc.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId().TypeId
                }
            };
        });
    }

    private Task<object?> HandleModelSummary()
    {
        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<ModelSummary> { Success = false, Error = "No active document" };

            var categoryCounts = new List<CategoryCount>();
            var allElements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements();

            var grouped = allElements
                .Where(e => e.Category != null)
                .GroupBy(e => e.Category.Name)
                .OrderByDescending(g => g.Count())
                .Take(50); // Top 50 categories

            foreach (var group in grouped)
            {
                categoryCounts.Add(new CategoryCount
                {
                    Name = group.Key,
                    Count = group.Count()
                });
            }

            return (object)new ApiResponse<ModelSummary>
            {
                Success = true,
                Data = new ModelSummary
                {
                    Title = doc.Title,
                    Categories = categoryCounts,
                    TotalElements = allElements.Count
                }
            };
        });
    }

    private Task<object?> HandleGetElements(System.Collections.Specialized.NameValueCollection query)
    {
        var category = query["category"] ?? "";
        var level = query["level"];
        var limitStr = query["limit"];
        int limit = 100;
        if (!string.IsNullOrEmpty(limitStr)) int.TryParse(limitStr, out limit);

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<ElementData>> { Success = false, Error = "No active document" };

            var collector = new FilteredElementCollector(doc);
            collector.WhereElementIsNotElementType();

            // Filter by category name
            if (!string.IsNullOrEmpty(category))
            {
                var bic = GetBuiltInCategory(category);
                if (bic != null)
                    collector.OfCategory(bic.Value);
            }

            var elements = collector.ToElements();

            // Filter by level name
            if (!string.IsNullOrEmpty(level))
            {
                elements = elements.Where(e =>
                {
                    if (e.LevelId != ElementId.InvalidElementId)
                    {
                        var lvl = doc.GetElement(e.LevelId) as Level;
                        return lvl?.Name?.Equals(level, StringComparison.OrdinalIgnoreCase) == true;
                    }
                    return false;
                }).ToList();
            }

            var result = elements.Take(limit).Select(e => ToElementData(doc, e, false)).ToList();

            return (object)new ApiResponse<List<ElementData>>
            {
                Success = true,
                Data = result,
                Count = result.Count
            };
        });
    }

    private Task<object?> HandleGetElement(string idStr)
    {
        if (!int.TryParse(idStr, out int id))
            return Task.FromResult<object?>(new ApiResponse<ElementData> { Success = false, Error = "Invalid element ID" });

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<ElementData> { Success = false, Error = "No active document" };

            var element = doc.GetElement(new ElementId(id));
            if (element == null)
                return new ApiResponse<ElementData> { Success = false, Error = $"Element {id} not found" };

            return (object)new ApiResponse<ElementData>
            {
                Success = true,
                Data = ToElementData(doc, element, true)
            };
        });
    }

    private Task<object?> HandleGetParameters(System.Collections.Specialized.NameValueCollection query)
    {
        var elementIdStr = query["elementId"];
        var category = query["category"];

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<ParameterData>> { Success = false, Error = "No active document" };

            var paramList = new List<ParameterData>();

            if (!string.IsNullOrEmpty(elementIdStr) && int.TryParse(elementIdStr, out int eid))
            {
                var element = doc.GetElement(new ElementId(eid));
                if (element != null)
                    paramList = GetElementParameters(element);
            }
            else if (!string.IsNullOrEmpty(category))
            {
                var bic = GetBuiltInCategory(category);
                if (bic != null)
                {
                    var first = new FilteredElementCollector(doc)
                        .OfCategory(bic.Value)
                        .WhereElementIsNotElementType()
                        .FirstElement();
                    if (first != null)
                        paramList = GetElementParameters(first);
                }
            }

            return (object)new ApiResponse<List<ParameterData>>
            {
                Success = true,
                Data = paramList,
                Count = paramList.Count
            };
        });
    }

    private Task<object?> HandleGetSharedParams()
    {
        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<string>> { Success = false, Error = "No active document" };

            var sharedParams = new HashSet<string>();
            var elements = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Take(500); // Sample 500 elements

            foreach (var el in elements)
            {
                foreach (Parameter p in el.Parameters)
                {
                    if (p.IsShared)
                        sharedParams.Add(p.Definition.Name);
                }
            }

            var sorted = sharedParams.OrderBy(s => s).ToList();
            return (object)new ApiResponse<List<string>>
            {
                Success = true,
                Data = sorted,
                Count = sorted.Count
            };
        });
    }

    private Task<object?> HandleGetCategories()
    {
        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<CategoryCount>> { Success = false, Error = "No active document" };

            var cats = new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .ToElements()
                .Where(e => e.Category != null)
                .GroupBy(e => e.Category.Name)
                .Select(g => new CategoryCount { Name = g.Key, Count = g.Count() })
                .OrderByDescending(c => c.Count)
                .ToList();

            return (object)new ApiResponse<List<CategoryCount>>
            {
                Success = true,
                Data = cats,
                Count = cats.Count
            };
        });
    }

    private Task<object?> HandleGetLevels()
    {
        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<LevelInfo>> { Success = false, Error = "No active document" };

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new LevelInfo
                {
                    Id = l.Id.IntegerValue,
                    Name = l.Name,
                    Elevation = Math.Round(UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters), 3)
                })
                .ToList();

            return (object)new ApiResponse<List<LevelInfo>>
            {
                Success = true,
                Data = levels,
                Count = levels.Count
            };
        });
    }

    private Task<object?> HandleGetRooms(System.Collections.Specialized.NameValueCollection query)
    {
        var level = query["level"];

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<RoomData>> { Success = false, Error = "No active document" };

            var rooms = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType()
                .Cast<Room>()
                .Where(r => r.Area > 0) // Only placed rooms
                .AsEnumerable();

            if (!string.IsNullOrEmpty(level))
            {
                rooms = rooms.Where(r =>
                    r.Level?.Name?.Equals(level, StringComparison.OrdinalIgnoreCase) == true);
            }

            var result = rooms.Select(r => new RoomData
            {
                Id = r.Id.IntegerValue,
                Name = r.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? "",
                Number = r.get_Parameter(BuiltInParameter.ROOM_NUMBER)?.AsString() ?? "",
                LevelName = r.Level?.Name ?? "",
                Area = Math.Round(UnitUtils.ConvertFromInternalUnits(r.Area, UnitTypeId.SquareMeters), 2),
                Perimeter = Math.Round(UnitUtils.ConvertFromInternalUnits(r.Perimeter, UnitTypeId.Meters), 2)
            }).ToList();

            return (object)new ApiResponse<List<RoomData>>
            {
                Success = true,
                Data = result,
                Count = result.Count
            };
        });
    }

    private Task<object?> HandleGetViews(System.Collections.Specialized.NameValueCollection query)
    {
        var typeFilter = query["type"]?.ToLowerInvariant();

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<ViewData>> { Success = false, Error = "No active document" };

            var views = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate)
                .AsEnumerable();

            if (!string.IsNullOrEmpty(typeFilter))
            {
                views = views.Where(v => v.ViewType.ToString().ToLowerInvariant().Contains(typeFilter));
            }

            var result = views.Select(v => new ViewData
            {
                Id = v.Id.IntegerValue,
                Name = v.Name,
                ViewType = v.ViewType.ToString(),
                LevelName = (v as ViewPlan)?.GenLevel?.Name ?? ""
            }).ToList();

            return (object)new ApiResponse<List<ViewData>>
            {
                Success = true,
                Data = result,
                Count = result.Count
            };
        });
    }

    private Task<object?> HandleGetQto(System.Collections.Specialized.NameValueCollection query)
    {
        var categoriesStr = query["categories"] ?? "Walls,Floors,Columns,StructuralFraming,Roofs";
        var groupByLevel = query["groupByLevel"]?.ToLowerInvariant() == "true";

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<QtoResultData>> { Success = false, Error = "No active document" };

            var categoryNames = categoriesStr.Split(',').Select(c => c.Trim()).ToList();
            var bics = categoryNames
                .Select(GetBuiltInCategory)
                .Where(b => b != null)
                .Select(b => b!.Value)
                .ToList();

            var svc = new SmartQTOService(doc);
            var qtoResults = svc.CalculateQTO(bics, false, null, groupByLevel);

            var result = qtoResults.Select(r => new QtoResultData
            {
                LevelName = r.LevelName,
                CategoryName = r.CategoryName,
                FamilyAndType = r.FamilyAndType,
                SizeTag = r.SizeTag,
                VolumeM3 = Math.Round(r.VolumeM3, 4),
                AreaM2 = Math.Round(r.AreaM2, 4),
                LengthM = Math.Round(r.LengthM, 4),
                Count = r.Count
            }).ToList();

            return (object)new ApiResponse<List<QtoResultData>>
            {
                Success = true,
                Data = result,
                Count = result.Count
            };
        });
    }

    private Task<object?> HandleGetWarnings()
    {
        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<WarningData>> { Success = false, Error = "No active document" };

            var warnings = doc.GetWarnings();
            var result = warnings.Select(w =>
            {
                var elementIds = w.GetFailingElements()
                    .Concat(w.GetAdditionalElements())
                    .Select(id => id.IntegerValue)
                    .ToList();

                return new WarningData
                {
                    Description = w.GetDescriptionText(),
                    Severity = w.GetSeverity().ToString(),
                    ElementIds = elementIds
                };
            }).ToList();

            return (object)new ApiResponse<List<WarningData>>
            {
                Success = true,
                Data = result,
                Count = result.Count
            };
        });
    }

    private Task<object?> HandleCheckParams(HttpListenerRequest request)
    {
        string body = "";
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            body = reader.ReadToEnd();
        }

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<ParamCheckResult>> { Success = false, Error = "No active document" };

            var parsed = SimpleJson.Deserialize(body) as Dictionary<string, object?>;
            var category = parsed?["category"]?.ToString() ?? "";
            var requiredParams = (parsed?["requiredParams"] as List<object?>)?
                .Select(p => p?.ToString() ?? "")
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList() ?? new List<string>();

            if (string.IsNullOrEmpty(category) || !requiredParams.Any())
            {
                return new ApiResponse<List<ParamCheckResult>>
                {
                    Success = false,
                    Error = "POST body must include 'category' and 'requiredParams' array"
                };
            }

            var bic = GetBuiltInCategory(category);
            if (bic == null)
                return new ApiResponse<List<ParamCheckResult>> { Success = false, Error = $"Unknown category: {category}" };

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic.Value)
                .WhereElementIsNotElementType()
                .ToElements();

            var results = new List<ParamCheckResult>();
            foreach (var el in elements)
            {
                var missing = new List<string>();
                var empty = new List<string>();

                foreach (var paramName in requiredParams)
                {
                    var param = el.LookupParameter(paramName);
                    if (param == null)
                        missing.Add(paramName);
                    else if (!param.HasValue || string.IsNullOrWhiteSpace(param.AsValueString() ?? param.AsString()))
                        empty.Add(paramName);
                }

                if (missing.Any() || empty.Any())
                {
                    results.Add(new ParamCheckResult
                    {
                        ElementId = el.Id.IntegerValue,
                        ElementName = el.Name,
                        Category = el.Category?.Name ?? "",
                        MissingParams = missing,
                        EmptyParams = empty
                    });
                }
            }

            return (object)new ApiResponse<List<ParamCheckResult>>
            {
                Success = true,
                Data = results,
                Count = results.Count
            };
        });
    }

    private Task<object?> HandleCheckNaming(HttpListenerRequest request)
    {
        string body = "";
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            body = reader.ReadToEnd();
        }

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<List<NamingCheckResult>> { Success = false, Error = "No active document" };

            var parsed = SimpleJson.Deserialize(body) as Dictionary<string, object?>;
            var category = parsed?["category"]?.ToString() ?? "";
            var pattern = parsed?["pattern"]?.ToString() ?? "";

            if (string.IsNullOrEmpty(category) || string.IsNullOrEmpty(pattern))
            {
                return new ApiResponse<List<NamingCheckResult>>
                {
                    Success = false,
                    Error = "POST body must include 'category' and 'pattern'"
                };
            }

            var bic = GetBuiltInCategory(category);
            if (bic == null)
                return new ApiResponse<List<NamingCheckResult>> { Success = false, Error = $"Unknown category: {category}" };

            var regex = new System.Text.RegularExpressions.Regex(pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            var elements = new FilteredElementCollector(doc)
                .OfCategory(bic.Value)
                .WhereElementIsNotElementType()
                .ToElements();

            var results = elements.Select(el =>
            {
                var typeName = "";
                var typeId = el.GetTypeId();
                if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType type)
                    typeName = type.Name;

                return new NamingCheckResult
                {
                    ElementId = el.Id.IntegerValue,
                    ElementName = el.Name,
                    TypeName = typeName,
                    Category = el.Category?.Name ?? "",
                    MatchesPattern = regex.IsMatch(typeName)
                };
            })
            .Where(r => !r.MatchesPattern) // Only return non-matching
            .ToList();

            return (object)new ApiResponse<List<NamingCheckResult>>
            {
                Success = true,
                Data = results,
                Count = results.Count
            };
        });
    }

    // ═══════════════════════════════════════════════════════════════
    //  HELPER METHODS
    // ═══════════════════════════════════════════════════════════════

    private static ElementData ToElementData(Document doc, Element element, bool includeParams)
    {
        var typeName = "";
        var familyName = "";
        var typeId = element.GetTypeId();
        if (typeId != ElementId.InvalidElementId && doc.GetElement(typeId) is ElementType type)
        {
            typeName = type.Name;
            familyName = type.FamilyName;
        }

        var levelName = "";
        if (element.LevelId != ElementId.InvalidElementId && doc.GetElement(element.LevelId) is Level lvl)
            levelName = lvl.Name;

        var data = new ElementData
        {
            Id = element.Id.IntegerValue,
            Category = element.Category?.Name ?? "",
            FamilyName = familyName,
            TypeName = typeName,
            LevelName = levelName
        };

        if (includeParams)
        {
            data.Parameters = GetElementParameters(element);
        }

        return data;
    }

    private static List<ParameterData> GetElementParameters(Element element)
    {
        var paramList = new List<ParameterData>();
        foreach (Parameter p in element.Parameters)
        {
            if (p.Definition == null) continue;

            string value = "";
            try
            {
                value = p.AsValueString() ?? p.AsString() ?? (p.StorageType == StorageType.Integer ? p.AsInteger().ToString() :
                    p.StorageType == StorageType.Double ? p.AsDouble().ToString("F4") :
                    p.StorageType == StorageType.ElementId ? p.AsElementId().IntegerValue.ToString() : "");
            }
            catch { }

            paramList.Add(new ParameterData
            {
                Name = p.Definition.Name,
                Value = value,
                StorageType = p.StorageType.ToString(),
                IsReadOnly = p.IsReadOnly,
                IsShared = p.IsShared,
                GroupName = p.Definition.GetGroupTypeId()?.TypeId ?? ""
            });
        }

        return paramList.OrderBy(p => p.Name).ToList();
    }

    /// <summary>
    /// Map category name string to BuiltInCategory enum.
    /// Supports both English names and Vietnamese display names.
    /// </summary>
    private static BuiltInCategory? GetBuiltInCategory(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;

        // Direct enum mapping
        var categoryMap = new Dictionary<string, BuiltInCategory>(StringComparer.OrdinalIgnoreCase)
        {
            ["Walls"] = BuiltInCategory.OST_Walls,
            ["Floors"] = BuiltInCategory.OST_Floors,
            ["Roofs"] = BuiltInCategory.OST_Roofs,
            ["Ceilings"] = BuiltInCategory.OST_Ceilings,
            ["Columns"] = BuiltInCategory.OST_Columns,
            ["StructuralColumns"] = BuiltInCategory.OST_StructuralColumns,
            ["StructuralFraming"] = BuiltInCategory.OST_StructuralFraming,
            ["StructuralFoundation"] = BuiltInCategory.OST_StructuralFoundation,
            ["Doors"] = BuiltInCategory.OST_Doors,
            ["Windows"] = BuiltInCategory.OST_Windows,
            ["Stairs"] = BuiltInCategory.OST_Stairs,
            ["Railings"] = BuiltInCategory.OST_StairsRailing,
            ["Ramps"] = BuiltInCategory.OST_Ramps,
            ["Rooms"] = BuiltInCategory.OST_Rooms,
            ["Furniture"] = BuiltInCategory.OST_Furniture,
            ["Plumbing"] = BuiltInCategory.OST_PlumbingFixtures,
            ["MechanicalEquipment"] = BuiltInCategory.OST_MechanicalEquipment,
            ["ElectricalEquipment"] = BuiltInCategory.OST_ElectricalEquipment,
            ["ElectricalFixtures"] = BuiltInCategory.OST_ElectricalFixtures,
            ["LightingFixtures"] = BuiltInCategory.OST_LightingFixtures,
            ["Pipes"] = BuiltInCategory.OST_PipeCurves,
            ["PipeFittings"] = BuiltInCategory.OST_PipeFitting,
            ["Ducts"] = BuiltInCategory.OST_DuctCurves,
            ["DuctFittings"] = BuiltInCategory.OST_DuctFitting,
            ["CableTray"] = BuiltInCategory.OST_CableTray,
            ["Conduit"] = BuiltInCategory.OST_Conduit,
            ["GenericModels"] = BuiltInCategory.OST_GenericModel,
            ["CurtainWalls"] = BuiltInCategory.OST_CurtainWallPanels,
            ["Parking"] = BuiltInCategory.OST_Parking,
            // Vietnamese aliases
            ["Tường"] = BuiltInCategory.OST_Walls,
            ["Sàn"] = BuiltInCategory.OST_Floors,
            ["Mái"] = BuiltInCategory.OST_Roofs,
            ["Cột"] = BuiltInCategory.OST_Columns,
            ["Dầm"] = BuiltInCategory.OST_StructuralFraming,
            ["Cửa"] = BuiltInCategory.OST_Doors,
            ["Cửa sổ"] = BuiltInCategory.OST_Windows,
            ["Phòng"] = BuiltInCategory.OST_Rooms,
            ["Đường ống"] = BuiltInCategory.OST_PipeCurves,
            ["Ống gió"] = BuiltInCategory.OST_DuctCurves,
            ["Móng"] = BuiltInCategory.OST_StructuralFoundation,
            ["Cầu thang"] = BuiltInCategory.OST_Stairs,
            ["Nội thất"] = BuiltInCategory.OST_Furniture,
            ["Trần"] = BuiltInCategory.OST_Ceilings,
        };

        if (categoryMap.TryGetValue(name, out var bic))
            return bic;

        // Try parsing as enum directly
        if (Enum.TryParse<BuiltInCategory>("OST_" + name, true, out var parsed))
            return parsed;
        if (Enum.TryParse<BuiltInCategory>(name, true, out var parsed2))
            return parsed2;

        return null;
    }

    // ═══════════════════════════════════════════════════════════════
    //  FIX ENDPOINTS — Model Modification
    // ═══════════════════════════════════════════════════════════════

    private Task<object?> HandleFixRoomBoundary(HttpListenerRequest request)
    {
        string body = "";
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            body = reader.ReadToEnd();
        }

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<object> { Success = false, Error = "No active document" };

            var parsed = SimpleJson.Deserialize(body) as Dictionary<string, object?>;
            var levelName = parsed?["level"]?.ToString() ?? "";

            // Find level
            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));

            if (level == null)
                return (object)new ApiResponse<object> { Success = false, Error = $"Level not found: {levelName}" };

            var svc = new AutoFixRoomBoundaryService();
            var fixResult = svc.Execute(doc, level.Id);

            return (object)new ApiResponse<object>
            {
                Success = true,
                Data = new
                {
                    sepLinesDeleted = fixResult.SepLinesDeleted,
                    redundantRoomsDeleted = fixResult.RedundantRoomsDeleted,
                    overlappingWallPairs = fixResult.OverlappingWallPairs,
                    problematicWallIds = fixResult.ProblematicWallIds,
                    messages = fixResult.Messages
                }
            };
        });
    }

    private Task<object?> HandleCreateRooms(HttpListenerRequest request)
    {
        string body = "";
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            body = reader.ReadToEnd();
        }

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<object> { Success = false, Error = "No active document" };

            var parsed = SimpleJson.Deserialize(body) as Dictionary<string, object?>;
            var levelName = parsed?["level"]?.ToString() ?? "";

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));

            if (level == null)
                return (object)new ApiResponse<object> { Success = false, Error = $"Level not found: {levelName}" };

            var svc = new AutoRoomService();
            var roomResult = svc.Execute(doc, new AutoRoomService.AutoRoomOptions { LevelId = level.Id });

            return (object)new ApiResponse<object>
            {
                Success = true,
                Data = new
                {
                    roomsCreated = roomResult.RoomsCreated,
                    linksSet = roomResult.LinksSet,
                    columnsSet = roomResult.ColumnsSet,
                    warnings = roomResult.Warnings
                }
            };
        });
    }

    private Task<object?> HandleCreateSepLines(HttpListenerRequest request)
    {
        string body = "";
        if (request.HasEntityBody)
        {
            using var reader = new StreamReader(request.InputStream, request.ContentEncoding);
            body = reader.ReadToEnd();
        }

        return _handler.ExecuteAsync(_externalEvent, app =>
        {
            var doc = app.ActiveUIDocument?.Document;
            if (doc == null)
                return new ApiResponse<object> { Success = false, Error = "No active document" };

            var parsed = SimpleJson.Deserialize(body) as Dictionary<string, object?>;
            var levelName = parsed?["level"]?.ToString() ?? "";

            var level = new FilteredElementCollector(doc)
                .OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => l.Name.Equals(levelName, StringComparison.OrdinalIgnoreCase));

            if (level == null)
                return (object)new ApiResponse<object> { Success = false, Error = $"Level not found: {levelName}" };

            var svc = new AutoSepLineService();
            var sepResult = svc.Execute(doc, level.Id);

            return (object)new ApiResponse<object>
            {
                Success = true,
                Data = new
                {
                    linesCreated = sepResult.LinesCreated,
                    gapsFound = sepResult.GapsFound,
                    wallsFromHost = sepResult.WallsFromHost,
                    wallsFromLinks = sepResult.WallsFromLinks,
                    messages = sepResult.Messages
                }
            };
        });
    }
}
