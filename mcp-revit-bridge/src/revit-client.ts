/**
 * Revit Bridge HTTP Client
 * Communicates with the C# REST API server running inside Revit
 */

const REVIT_BRIDGE_URL = process.env.REVIT_BRIDGE_URL || "http://localhost:52140";

interface ApiResponse<T> {
    success: boolean;
    data?: T;
    error?: string;
    count?: number;
}

export class RevitClient {
    private baseUrl: string;

    constructor(baseUrl?: string) {
        this.baseUrl = baseUrl || REVIT_BRIDGE_URL;
    }

    async get<T>(path: string, params?: Record<string, string>): Promise<ApiResponse<T>> {
        let url = `${this.baseUrl}${path}`;
        if (params) {
            const searchParams = new URLSearchParams();
            for (const [key, value] of Object.entries(params)) {
                if (value !== undefined && value !== null && value !== "") {
                    searchParams.append(key, value);
                }
            }
            const qs = searchParams.toString();
            if (qs) url += `?${qs}`;
        }

        try {
            const response = await fetch(url, {
                method: "GET",
                headers: { "Content-Type": "application/json" },
            });
            return (await response.json()) as ApiResponse<T>;
        } catch (error: any) {
            if (error.code === "ECONNREFUSED") {
                return {
                    success: false,
                    error: "Cannot connect to Revit Bridge. Make sure Revit is open with the CIC Addin loaded.",
                };
            }
            return { success: false, error: error.message };
        }
    }

    async post<T>(path: string, body: any): Promise<ApiResponse<T>> {
        try {
            const response = await fetch(`${this.baseUrl}${path}`, {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(body),
            });
            return (await response.json()) as ApiResponse<T>;
        } catch (error: any) {
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
    async getElements(category?: string, level?: string, limit?: number) {
        return this.get("/elements", {
            category: category || "",
            level: level || "",
            limit: limit?.toString() || "100",
        });
    }
    async getElement(id: number) {
        return this.get("/element", { id: id.toString() });
    }
    async getParameters(elementId?: number, category?: string) {
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
    async getRooms(level?: string) {
        return this.get("/rooms", { level: level || "" });
    }
    async getViews(type?: string) {
        return this.get("/views", { type: type || "" });
    }
    async extractBoq(categories?: string, groupByLevel?: boolean) {
        return this.get("/qto", {
            categories: categories || "Walls,Floors,Columns,StructuralFraming,Roofs",
            groupByLevel: groupByLevel ? "true" : "false",
        });
    }
    async getWarnings() {
        return this.get("/warnings");
    }
    async checkParameters(category: string, requiredParams: string[]) {
        return this.post("/check/parameters", { category, requiredParams });
    }
    async checkNaming(category: string, pattern: string) {
        return this.post("/check/naming", { category, pattern });
    }
    async fixRoomBoundary(level: string) {
        return this.post("/fix/room-boundary", { level });
    }
    async createRooms(level: string) {
        return this.post("/fix/rooms", { level });
    }
}
