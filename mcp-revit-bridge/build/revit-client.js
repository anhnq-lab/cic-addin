"use strict";
/**
 * Revit Bridge HTTP Client
 * Communicates with the C# REST API server running inside Revit
 */
Object.defineProperty(exports, "__esModule", { value: true });
exports.RevitClient = void 0;
const REVIT_BRIDGE_URL = process.env.REVIT_BRIDGE_URL || "http://localhost:52140";
class RevitClient {
    baseUrl;
    constructor(baseUrl) {
        this.baseUrl = baseUrl || REVIT_BRIDGE_URL;
    }
    async get(path, params) {
        let url = `${this.baseUrl}${path}`;
        if (params) {
            const searchParams = new URLSearchParams();
            for (const [key, value] of Object.entries(params)) {
                if (value !== undefined && value !== null && value !== "") {
                    searchParams.append(key, value);
                }
            }
            const qs = searchParams.toString();
            if (qs)
                url += `?${qs}`;
        }
        try {
            const response = await fetch(url, {
                method: "GET",
                headers: { "Content-Type": "application/json" },
            });
            return (await response.json());
        }
        catch (error) {
            if (error.code === "ECONNREFUSED") {
                return {
                    success: false,
                    error: "Cannot connect to Revit Bridge. Make sure Revit is open with the CIC Addin loaded.",
                };
            }
            return { success: false, error: error.message };
        }
    }
    async post(path, body) {
        try {
            const response = await fetch(`${this.baseUrl}${path}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body),
            });
            return (await response.json());
        }
        catch (error) {
            if (error.code === "ECONNREFUSED") {
                return {
                    success: false,
                    error: "Cannot connect to Revit Bridge. Make sure Revit is open with the CIC Addin loaded.",
                };
            }
            return { success: false, error: error.message };
        }
    }
    // Convenience methods
    async health() {
        return this.get("/health");
    }
    async modelInfo() {
        return this.get("/model/info");
    }
    async modelSummary() {
        return this.get("/model/summary");
    }
    async getElements(category, level, limit) {
        return this.get("/elements", {
            category: category || "",
            level: level || "",
            limit: limit?.toString() || "100",
        });
    }
    async getElement(id) {
        return this.get("/element", { id: id.toString() });
    }
    async getParameters(elementId, category) {
        return this.get("/parameters", {
            elementId: elementId?.toString() || "",
            category: category || "",
        });
    }
    async getSharedParameters() {
        return this.get("/parameters/shared");
    }
    async getCategories() {
        return this.get("/categories");
    }
    async getLevels() {
        return this.get("/levels");
    }
    async getRooms(level) {
        return this.get("/rooms", { level: level || "" });
    }
    async getViews(type) {
        return this.get("/views", { type: type || "" });
    }
    async extractBoq(categories, groupByLevel) {
        return this.get("/qto", {
            categories: categories || "Walls,Floors,Columns,StructuralFraming,Roofs",
            groupByLevel: groupByLevel ? "true" : "false",
        });
    }
    async getWarnings() {
        return this.get("/warnings");
    }
    async checkParameters(category, requiredParams) {
        return this.post("/check/parameters", { category, requiredParams });
    }
    async checkNaming(category, pattern) {
        return this.post("/check/naming", { category, pattern });
    }
    async fixRoomBoundary(level) {
        return this.post("/fix/room-boundary", { level });
    }
    async createRooms(level) {
        return this.post("/fix/rooms", { level });
    }
}
exports.RevitClient = RevitClient;
//# sourceMappingURL=revit-client.js.map