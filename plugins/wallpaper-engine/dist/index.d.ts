type ToolResult = {
    content: {
        type: "text";
        text: string;
    }[];
    details: unknown;
};
type AnyAgentTool = {
    name: string;
    label: string;
    description: string;
    parameters: Schema;
    execute: (id: string, params: Record<string, unknown>) => Promise<ToolResult>;
};
type PluginApi = {
    pluginConfig?: Record<string, unknown>;
    registerTool: (tool: AnyAgentTool, options?: {
        optional?: boolean;
    }) => void;
    registerGatewayMethod: (name: string, handler: (request: {
        params?: Record<string, unknown>;
        respond: (ok: boolean, payload?: unknown, error?: {
            code: string;
            message: string;
        }) => void;
    }) => Promise<void>, options?: {
        scope: "operator.read" | "operator.write";
    }) => void;
    registerService: (service: {
        id: string;
        start: () => Promise<void>;
    }) => void;
};
type Schema = Record<string, unknown>;
declare const toolPlugin: {
    id: string;
    name: string;
    description: string;
    configSchema: {
        jsonSchema: Schema;
    };
    register(api: PluginApi): void;
};
declare const _default: {
    register(api: Parameters<typeof toolPlugin.register>[0]): void;
    id: string;
    name: string;
    description: string;
    configSchema: {
        jsonSchema: Schema;
    };
};
export default _default;
