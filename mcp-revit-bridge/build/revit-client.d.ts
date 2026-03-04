/**
 * Revit Bridge HTTP Client
 * Communicates with the C# REST API server running inside Revit
 */
interface ApiResponse<T> {
    success: boolean;
    data?: T;
    error?: string;
    count?: number;
}
export declare class RevitClient {
    private baseUrl;
    constructor(baseUrl?: string);
    get<T>(path: string, params?: Record<string, string>): Promise<ApiResponse<T>>;
    post<T>(path: string, body: any): Promise<ApiResponse<T>>;
    health(): Promise<ApiResponse<unknown>>;
    modelInfo(): Promise<ApiResponse<unknown>>;
    modelSummary(): Promise<ApiResponse<unknown>>;
    getElements(category?: string, level?: string, limit?: number): Promise<ApiResponse<unknown>>;
    getElement(id: number): Promise<ApiResponse<unknown>>;
    getParameters(elementId?: number, category?: string): Promise<ApiResponse<unknown>>;
    getSharedParameters(): Promise<ApiResponse<unknown>>;
    getCategories(): Promise<ApiResponse<unknown>>;
    getLevels(): Promise<ApiResponse<unknown>>;
    getRooms(level?: string): Promise<ApiResponse<unknown>>;
    getViews(type?: string): Promise<ApiResponse<unknown>>;
    extractBoq(categories?: string, groupByLevel?: boolean): Promise<ApiResponse<unknown>>;
    getWarnings(): Promise<ApiResponse<unknown>>;
    checkParameters(category: string, requiredParams: string[]): Promise<ApiResponse<unknown>>;
    checkNaming(category: string, pattern: string): Promise<ApiResponse<unknown>>;
    fixRoomBoundary(level: string): Promise<ApiResponse<unknown>>;
    createRooms(level: string): Promise<ApiResponse<unknown>>;
}
export {};
//# sourceMappingURL=revit-client.d.ts.map