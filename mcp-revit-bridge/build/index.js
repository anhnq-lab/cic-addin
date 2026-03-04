#!/usr/bin/env node
"use strict";
/**
 * CIC Revit Bridge MCP Server
 *
 * Connects Claude (Antigravity) to Autodesk Revit via a REST API bridge.
 * The bridge runs inside Revit as part of the CIC BIM Addin.
 *
 * Usage:
 *   node build/index.js
 *
 * Environment:
 *   REVIT_BRIDGE_URL - URL of the Revit Bridge (default: http://localhost:52140)
 */
Object.defineProperty(exports, "__esModule", { value: true });
const mcp_js_1 = require("@modelcontextprotocol/sdk/server/mcp.js");
const stdio_js_1 = require("@modelcontextprotocol/sdk/server/stdio.js");
const zod_1 = require("zod");
const revit_client_js_1 = require("./revit-client.js");
const client = new revit_client_js_1.RevitClient();
const server = new mcp_js_1.McpServer({
    name: "revit-bridge",
    version: "1.0.0",
});
// ═══════════════════════════════════════════════════════════════
//  HELPER
// ═══════════════════════════════════════════════════════════════
function formatResponse(result) {
    if (!result.success) {
        return `❌ Error: ${result.error}`;
    }
    return JSON.stringify(result.data, null, 2);
}
// ═══════════════════════════════════════════════════════════════
//  MODEL TOOLS
// ═══════════════════════════════════════════════════════════════
server.tool("get_model_info", "Get general information about the currently open Revit model: name, file path, levels, phases, total element count, active view, and units", {}, async () => {
    const result = await client.modelInfo();
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("get_model_summary", "Get a quick summary of the model with element counts grouped by category (e.g., 150 Walls, 80 Floors, 45 Columns). Useful for understanding model composition at a glance.", {}, async () => {
    const result = await client.modelSummary();
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  ELEMENT TOOLS
// ═══════════════════════════════════════════════════════════════
server.tool("get_elements", "Query elements from the Revit model filtered by category and/or level. Returns element ID, category, family name, type name, and level for each element. Supports both English category names (Walls, Floors, Columns) and Vietnamese (Tường, Sàn, Cột).", {
    category: zod_1.z
        .string()
        .optional()
        .describe("Category filter, e.g. 'Walls', 'Floors', 'StructuralFraming', 'Tường', 'Sàn', 'Dầm'"),
    level: zod_1.z
        .string()
        .optional()
        .describe("Level name filter, e.g. 'Level 1', 'Tầng 1'"),
    limit: zod_1.z
        .number()
        .optional()
        .default(100)
        .describe("Max number of elements to return (default 100)"),
}, async ({ category, level, limit }) => {
    const result = await client.getElements(category, level, limit);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("get_element_detail", "Get detailed information about a specific element by its ID, including all parameters with their values, storage types, and whether they are shared/read-only.", {
    elementId: zod_1.z.number().describe("The Revit element ID (integer)"),
}, async ({ elementId }) => {
    const result = await client.getElement(elementId);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  PARAMETER TOOLS
// ═══════════════════════════════════════════════════════════════
server.tool("get_parameters", "Get all parameters for a specific element (by ID) or for the first element of a category. Shows parameter names, values, storage types, and groups. Useful for understanding what data is available on elements.", {
    elementId: zod_1.z
        .number()
        .optional()
        .describe("Specific element ID to read parameters from"),
    category: zod_1.z
        .string()
        .optional()
        .describe("Category name - will read params from the first element of this category"),
}, async ({ elementId, category }) => {
    const result = await client.getParameters(elementId, category);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("get_shared_parameters", "List all shared parameters found across elements in the model. Shared parameters are custom parameters that carry project-specific data.", {}, async () => {
    const result = await client.getSharedParameters();
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  QUERY TOOLS
// ═══════════════════════════════════════════════════════════════
server.tool("get_categories", "List all element categories present in the model with their element counts. Sorted by count descending. Useful for understanding what types of elements exist.", {}, async () => {
    const result = await client.getCategories();
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("get_levels", "List all levels in the model with their elevations in meters. Sorted by elevation ascending.", {}, async () => {
    const result = await client.getLevels();
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("get_rooms", "List all placed rooms in the model with name, number, level, area (m²), and perimeter (m). Can filter by level.", {
    level: zod_1.z
        .string()
        .optional()
        .describe("Optional level name to filter rooms"),
}, async ({ level }) => {
    const result = await client.getRooms(level);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("get_views", "List all views in the model (floor plans, sections, 3D views, etc.). Can filter by view type.", {
    type: zod_1.z
        .string()
        .optional()
        .describe("Optional view type filter: 'plan', 'section', '3d', 'elevation', 'schedule'"),
}, async ({ type }) => {
    const result = await client.getViews(type);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  BOQ / QTO TOOLS
// ═══════════════════════════════════════════════════════════════
server.tool("extract_boq", "Extract Bill of Quantities (BOQ) from the model. Calculates volume (m³), area (m²), length (m), and count for each element type, optionally grouped by level. Supports categories: Walls, Floors, Columns, StructuralFraming, StructuralColumns, Roofs, Ceilings, Doors, Windows, etc.", {
    categories: zod_1.z
        .string()
        .optional()
        .default("Walls,Floors,Columns,StructuralFraming,Roofs")
        .describe("Comma-separated category names, e.g. 'Walls,Floors,Columns'"),
    groupByLevel: zod_1.z
        .boolean()
        .optional()
        .default(true)
        .describe("Group quantities by level (default: true)"),
}, async ({ categories, groupByLevel }) => {
    const result = await client.extractBoq(categories, groupByLevel);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  QA/QC TOOLS
// ═══════════════════════════════════════════════════════════════
server.tool("check_warnings", "Get all model warnings/errors from Revit. Each warning includes a description, severity, and the IDs of affected elements. Essential for QA/QC model checking.", {}, async () => {
    const result = await client.getWarnings();
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("check_parameters", "Check elements of a category for missing or empty required parameters. Returns a list of elements that are missing or have empty values for the specified parameters. Useful for QA/QC validation.", {
    category: zod_1.z
        .string()
        .describe("Category to check, e.g. 'Walls', 'Columns', 'Tường'"),
    requiredParams: zod_1.z
        .array(zod_1.z.string())
        .describe("List of parameter names that should exist and have values, e.g. ['Mark', 'Comments', 'CIC_MaCauKien']"),
}, async ({ category, requiredParams }) => {
    const result = await client.checkParameters(category, requiredParams);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("check_naming", "Check element type naming convention against a regex pattern. Returns elements whose type names do NOT match the pattern. Useful for enforcing BIM standards.", {
    category: zod_1.z
        .string()
        .describe("Category to check, e.g. 'Walls', 'Columns'"),
    pattern: zod_1.z
        .string()
        .describe("Regex pattern that type names should match, e.g. '^RC-\\\\d+x\\\\d+' for columns"),
}, async ({ category, pattern }) => {
    const result = await client.checkNaming(category, pattern);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  FIX TOOLS — Model Modification
// ═══════════════════════════════════════════════════════════════
server.tool("fix_room_boundary", "Auto-fix room boundary issues on a specific level. Deletes duplicate room separation lines, removes separation lines overlapping walls, cleans up zero-area rooms, and detects overlapping walls. Run this BEFORE create_rooms to fix boundary problems.", {
    level: zod_1.z
        .string()
        .describe("Level name to fix, e.g. '22FL_FFL_TA'"),
}, async ({ level }) => {
    const result = await client.fixRoomBoundary(level);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
server.tool("create_rooms", "Auto-create rooms on a specific level using PlanTopology circuits. Automatically enables Room Bounding on links and columns first. Use fix_room_boundary before this to clean up boundary issues.", {
    level: zod_1.z
        .string()
        .describe("Level name to create rooms on, e.g. '22FL_FFL_TA'"),
}, async ({ level }) => {
    const result = await client.createRooms(level);
    return { content: [{ type: "text", text: formatResponse(result) }] };
});
// ═══════════════════════════════════════════════════════════════
//  START SERVER
// ═══════════════════════════════════════════════════════════════
async function main() {
    const transport = new stdio_js_1.StdioServerTransport();
    await server.connect(transport);
    console.error("CIC Revit Bridge MCP Server running on stdio");
}
main().catch((error) => {
    console.error("Fatal error:", error);
    process.exit(1);
});
//# sourceMappingURL=index.js.map