// Wallpaper Engine plugin for OpenClaw.
// Paints wallpapers from the local Steam Wallpaper Engine library behind the
// OpenClaw Control UI (web interface), and optionally as the Windows desktop
// wallpaper.

import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { promises as fs } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

// Structural subset of the public OpenClaw plugin API; no runtime dependency is needed.
type ToolResult = {content: {type: "text"; text: string}[]; details: unknown};
type AnyAgentTool = {name: string; label: string; description: string; parameters: Schema;
  execute: (id: string, params: Record<string, unknown>) => Promise<ToolResult>};
type PluginApi = {
  pluginConfig?: Record<string, unknown>;
  registerTool: (tool: AnyAgentTool, options?: {optional?: boolean}) => void;
  registerGatewayMethod: (name: string, handler: (request: {params?: Record<string, unknown>;
    respond: (ok: boolean, payload?: unknown, error?: {code: string; message: string}) => void}) => Promise<void>, options?: {scope: "operator.read" | "operator.write"}) => void;
  registerService: (service: {id: string; start: () => Promise<void>}) => void;
};
type ToolDefinition<P> = {name: string; description: string; parameters: Schema;
  execute: (params: P, config?: Record<string, unknown>) => Promise<unknown>};
type WallpaperTool = ToolDefinition<Record<string, unknown>>;
function tool<P>(definition: ToolDefinition<P>): WallpaperTool {
  return {...definition, execute: (params, config) => definition.execute(params as P, config)};
}

// Minimal JSON Schema builders (avoids a runtime dependency on typebox;
// openclaw passes tool `parameters` straight through to the model).
type Schema = Record<string, unknown>;
const OPTIONAL = Symbol("openclaw-optional");

function optional<T extends Schema>(schema: T): T {
  return { ...schema, [OPTIONAL]: true } as T;
}

function objectSchema(properties: Record<string, Schema>, options?: Schema): Schema {
  const cleaned: Record<string, Schema> = {};
  const required: string[] = [];
  for (const [key, value] of Object.entries(properties)) {
    const { [OPTIONAL]: _flag, ...rest } = value as Schema & { [OPTIONAL]?: boolean };
    cleaned[key] = rest;
    if (!_flag) required.push(key);
  }
  return {
    type: "object",
    properties: cleaned,
    ...(required.length > 0 ? { required } : {}),
    additionalProperties: false,
    ...options,
  };
}

const Type = {
  Object: objectSchema,
  String: (options?: Schema): Schema => ({ type: "string", ...options }),
  Number: (options?: Schema): Schema => ({ type: "number", ...options }),
  Boolean: (options?: Schema): Schema => ({ type: "boolean", ...options }),
  Array: (items: Schema, options?: Schema): Schema => ({ type: "array", items, ...options }),
  Optional: optional,
};

const execFileAsync = promisify(execFile);

const DEFAULT_LIBRARY_ROOT =
  process.platform === "win32"
    ? "D:\\steam\\steamapps\\workshop\\content\\431960"
    : "/steam/steamapps/workshop/content/431960";

const DEFAULT_CONTROL_UI_ROOT =
  process.platform === "win32" ? "E:\\openclaw\\dist\\control-ui" : "";

const IMAGE_EXTENSIONS = new Set([".jpg", ".jpeg", ".png", ".bmp", ".webp"]);
const NEEDS_FRAME_EXTRACTION = new Set([".gif", ".mp4", ".webm", ".mov", ".mkv", ".avi"]);
// Formats accepted by the "import a local media file" path (independent of the
// Wallpaper Engine library). GIF stays animated; still images are shown as-is.
const IMPORT_IMAGE_EXTENSIONS = new Set([".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif"]);
const IMPORT_VIDEO_EXTENSIONS = new Set([".mp4", ".webm", ".mov", ".mkv", ".avi"]);

type WallpaperType = "video" | "scene" | "web" | "unknown";

type WallpaperEntry = {
  id: string;
  title: string;
  type: WallpaperType;
  preview: string | null;
  media: string | null;
  tags: string[];
  workshopUrl: string | null;
  directory: string;
  sizeMb: number | null;
};

type UiConfig = {
  mediaType?: "image" | "video";
  enabled: boolean;
  image: string | null;
  wallpaperId: string | null;
  title: string | null;
  fit: string;
  opacity: number;
  blur: number;
  scrim: number;
  scrimColor: string;
  translucentApp: boolean;
  appAlpha: number;
  // Console typography (borrowed from the DSHL wallpaper plugin's fontCustom block).
  // All values are inert unless fontCustom is true.
  fontCustom: boolean;
  fontColor: string; // #RRGGBB text colour for the Control UI
  fontSize: number; // base px applied to the console root; 0 = keep default
  fontWeight: number; // 100..900; 0 = keep default
  fontFamily: string; // "" = inherit
  caretColor: string; // #RRGGBB text caret / selection colour
  updatedAt: string | null;
};

type PluginConfig = {
  libraryRoot: string;
  controlUiRoot: string;
  ffmpegPath: string;
  cacheDir: string;
};

function normalizeType(raw: unknown): WallpaperType {
  const value = String(raw ?? "").toLowerCase();
  if (value === "video") return "video";
  if (value === "scene") return "scene";
  if (value === "web") return "web";
  return "unknown";
}

async function readPluginConfig(provided?: Record<string, unknown>): Promise<PluginConfig> {
  const home = os.homedir();
  const config: PluginConfig = {
    libraryRoot: DEFAULT_LIBRARY_ROOT,
    controlUiRoot: DEFAULT_CONTROL_UI_ROOT,
    ffmpegPath: process.platform === "win32" ? "C:\\ffmpeg\\bin\\ffmpeg.exe" : "ffmpeg",
    cacheDir: path.join(home, ".openclaw", "wallpaper-engine", "cache"),
  };

  const merge = (source: Record<string, unknown> | undefined) => {
    if (!source) return;
    if (typeof source.libraryRoot === "string" && source.libraryRoot.trim()) {
      config.libraryRoot = source.libraryRoot;
    }
    if (typeof source.controlUiRoot === "string" && source.controlUiRoot.trim()) {
      config.controlUiRoot = source.controlUiRoot;
    }
    if (typeof source.ffmpegPath === "string" && source.ffmpegPath.trim()) {
      config.ffmpegPath = source.ffmpegPath;
    }
    if (typeof source.cacheDir === "string" && source.cacheDir.trim()) {
      config.cacheDir = source.cacheDir;
    }
  };

  // The host has already resolved the selected instance's config. Never override it with another profile.
  merge(provided);
  return config;
}

async function scanLibrary(libraryRoot: string): Promise<WallpaperEntry[]> {
  let dirs: string[] = [];
  try {
    const items = await fs.readdir(libraryRoot, { withFileTypes: true });
    dirs = items.filter((item) => item.isDirectory()).map((item) => item.name);
  } catch {
    throw new Error(`Wallpaper library is not readable: ${libraryRoot}`);
  }

  const entries: WallpaperEntry[] = [];
  await Promise.all(
    dirs.map(async (id) => {
      const directory = path.join(libraryRoot, id);
      try {
        const raw = await fs.readFile(path.join(directory, "project.json"), "utf8");
        const project = JSON.parse(raw) as Record<string, unknown>;
        const previewName = typeof project.preview === "string" ? project.preview : null;
        const mediaName = typeof project.file === "string" ? project.file : null;
        let sizeMb: number | null = null;
        try {
          const siblings = await fs.readdir(directory, { withFileTypes: true });
          let total = 0;
          for (const sibling of siblings) {
            if (!sibling.isFile()) continue;
            try {
              total += (await fs.stat(path.join(directory, sibling.name))).size;
            } catch {
              // ignore
            }
          }
          sizeMb = Math.round((total / (1024 * 1024)) * 10) / 10;
        } catch {
          sizeMb = null;
        }
        entries.push({
          id,
          title: typeof project.title === "string" && project.title.trim() ? project.title : id,
          type: normalizeType(project.type),
          preview: previewName ? await safeMediaPath(directory, previewName) : null,
          media: mediaName ? await safeMediaPath(directory, mediaName) : null,
          tags: Array.isArray(project.tags) ? project.tags.map((tag) => String(tag)) : [],
          workshopUrl:
            typeof project.workshopurl === "string"
              ? project.workshopurl
              : `steam://url/CommunityFilePage/${id}`,
          directory,
          sizeMb,
        });
      } catch {
        // skip directories without a readable project.json
      }
    }),
  );

  entries.sort((a, b) => a.title.localeCompare(b.title, "zh-Hans-CN"));
  return entries;
}

async function safeMediaPath(directory: string, name: string): Promise<string | null> {
  try {
    const root = await fs.realpath(directory);
    const target = await fs.realpath(path.resolve(directory, name));
    const relative = path.relative(root, target);
    if (relative.startsWith("..") || path.isAbsolute(relative)) return null;
    return (await fs.stat(target)).isFile() ? target : null;
  } catch { return null; }
}

async function fileExists(target: string): Promise<boolean> {
  try {
    await fs.access(target);
    return true;
  } catch {
    return false;
  }
}

function pickEntry(entries: WallpaperEntry[], query: string): WallpaperEntry | undefined {
  const needle = query.trim().toLowerCase();
  if (!needle) return undefined;
  const exact = entries.find((entry) => entry.id === needle);
  if (exact) return exact;
  return entries.find((entry) => entry.title.toLowerCase().includes(needle));
}

/**
 * The Control UI can only display static images, so animated previews (GIF or
 * video) are converted to a still JPEG with ffmpeg and cached.
 */
async function toStaticImage(source: string, config: PluginConfig): Promise<string> {
  const ext = path.extname(source).toLowerCase();
  if (IMAGE_EXTENSIONS.has(ext)) return source;
  if (!NEEDS_FRAME_EXTRACTION.has(ext)) {
    throw new Error(`Unsupported preview format for wallpaper: ${ext}`);
  }

  await fs.mkdir(config.cacheDir, { recursive: true });
  const safeName = source.replace(/[^a-zA-Z0-9]/g, "_").slice(-120);
  const target = path.join(config.cacheDir, `${safeName}.jpg`);
  if (await fileExists(target)) return target;

  const args = ["-y", "-i", source];
  if (ext !== ".gif") args.push("-ss", "0");
  args.push("-frames:v", "1", "-q:v", "2", target);

  try {
    await execFileAsync(config.ffmpegPath, args, { windowsHide: true });
  } catch (error) {
    const ffmpegAvailable = await fileExists(config.ffmpegPath);
    throw new Error(
      ffmpegAvailable
        ? `Failed to extract a still frame from ${source}: ${(error as Error).message}`
        : `ffmpeg not found at ${config.ffmpegPath}; cannot convert ${ext} preview. Set plugins.entries.wallpaper-engine.config.ffmpegPath.`,
    );
  }
  return target;
}

function uiAssetDir(config: PluginConfig): string {
  return path.join(config.controlUiRoot, "wallpaper");
}

function defaultUiConfig(): UiConfig {
  return {
    enabled: false,
    image: null,
    wallpaperId: null,
    title: null,
    fit: "cover",
    opacity: 0.55,
    blur: 0,
    scrim: 0.45,
    scrimColor: "#0b0e13",
    translucentApp: true,
    appAlpha: 0.45,
    fontCustom: false,
    fontColor: "#f2f3f5",
    fontSize: 0,
    fontWeight: 0,
    fontFamily: "",
    caretColor: "#ffffff",
    updatedAt: null,
  };
}

async function readUiConfig(config: PluginConfig): Promise<UiConfig> {
  try {
    const raw = await fs.readFile(path.join(uiAssetDir(config), "wallpaper.json"), "utf8");
    return { ...defaultUiConfig(), ...(JSON.parse(raw) as Partial<UiConfig>) };
  } catch {
    return defaultUiConfig();
  }
}

async function writeUiConfig(config: PluginConfig, next: UiConfig): Promise<UiConfig> {
  const dir = uiAssetDir(config);
  await fs.mkdir(dir, { recursive: true });
  const payload = { ...next, updatedAt: new Date().toISOString() };
  await fs.writeFile(path.join(dir, "wallpaper.json"), JSON.stringify(payload, null, 2), "utf8");
  return payload;
}

/** Copy the bundled browser script into the Control UI asset directory. */
async function ensureUiScript(config: PluginConfig): Promise<string> {
  const dir = uiAssetDir(config);
  await fs.mkdir(dir, { recursive: true });
  const target = path.join(dir, "wallpaper.js");
  const here = path.dirname(fileURLToPath(import.meta.url));
  const candidates = [
    path.join(here, "..", "ui", "wallpaper.js"),
    path.join(here, "..", "..", "ui", "wallpaper.js"),
    path.join(here, "..", "..", "src", "..", "ui", "wallpaper.js"),
  ];
  for (const candidate of candidates) {
    if (await fileExists(candidate)) {
      const source = await fs.readFile(candidate, "utf8");
      const existing = (await fileExists(target)) ? await fs.readFile(target, "utf8") : null;
      if (existing !== source) await fs.writeFile(target, source, "utf8");
      const index = path.join(config.controlUiRoot, "index.html");
      const html = await fs.readFile(index, "utf8");
      if (!html.includes('src="./wallpaper/wallpaper.js"')) {
        if (!html.includes("</head>")) throw new Error("Control UI index.html has no head element.");
        if (!(await fileExists(index + ".bak-wallpaper"))) await fs.copyFile(index, index + ".bak-wallpaper");
        await fs.writeFile(index, html.replace("</head>", '<script src="./wallpaper/wallpaper.js" defer></script>\n</head>'));
      }
      return target;
    }
  }
  throw new Error(
    `Bundled ui/wallpaper.js not found (looked in ${candidates.join(", ")}). Reinstall the plugin.`,
  );
}

async function applyWallpaperToDesktop(imagePath: string): Promise<void> {
  if (process.platform !== "win32") {
    throw new Error("Applying a desktop wallpaper is only supported on Windows.");
  }
  // NOTE: PowerShell here-strings require real newlines, so this is joined with
  // "\n" (not "; ") and passed as a single -Command argument.
  const script = [
    "$ErrorActionPreference = 'Stop'",
    "[Console]::OutputEncoding = [System.Text.Encoding]::UTF8",
    "$path = $env:OC_WALLPAPER_PATH",
    "if (-not (Test-Path -LiteralPath $path)) { throw \"wallpaper file missing: $path\" }",
    "Add-Type -Namespace OpenClawWallpaper -Name Native -MemberDefinition @\"",
    "[DllImport(\"user32.dll\", SetLastError = true, CharSet = CharSet.Auto)]",
    "public static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);",
    "\"@",
    "$ok = [OpenClawWallpaper.Native]::SystemParametersInfo(20, 0, $path, 3)",
    "if (-not $ok) { throw 'SystemParametersInfo(SPI_SETDESKWALLPAPER) failed' }",
  ].join("\n");

  await execFileAsync(
    "powershell",
    ["-NoProfile", "-NonInteractive", "-Command", script],
    { env: { ...process.env, OC_WALLPAPER_PATH: imagePath }, windowsHide: true },
  );
}

const definition = {
  id: "wallpaper-engine",
  name: "Wallpaper Engine",
  description:
    "Put wallpapers from the local Steam Wallpaper Engine library behind the OpenClaw Control UI (web interface), or apply them to the Windows desktop.",
  configSchema: Type.Object(
    {
      libraryRoot: Type.Optional(
        Type.String({
          description:
            "Wallpaper Engine workshop content directory. Defaults to D:\\steam\\steamapps\\workshop\\content\\431960",
        }),
      ),
      controlUiRoot: Type.Optional(
        Type.String({
          description:
            "OpenClaw Control UI asset root served by the gateway. Defaults to E:\\openclaw\\dist\\control-ui",
        }),
      ),
      ffmpegPath: Type.Optional(
        Type.String({
          description: "Path to ffmpeg.exe used to convert GIF/video previews into still images.",
        }),
      ),
      cacheDir: Type.Optional(
        Type.String({ description: "Where converted still frames are cached." }),
      ),
    },
    { additionalProperties: false },
  ),
  tools: [
    tool({
      name: "wallpaper_list",
      description:
        "List wallpapers available in the local Wallpaper Engine library (id, title, type, tags, size).",
      parameters: Type.Object({
        type: Type.Optional(
          Type.String({ description: "Filter by wallpaper type: video, scene, web, or all." }),
        ),
        limit: Type.Optional(
          Type.Number({
            description: "Maximum number of entries to return (default 50).",
            minimum: 1,
          }),
        ),
      }),
      execute: async (
        { type, limit }: { type?: string; limit?: number },
        config?: Record<string, unknown>,
      ) => {
        const resolved = await readPluginConfig(config);
        const entries = await scanLibrary(resolved.libraryRoot);
        const wanted = String(type ?? "all").toLowerCase();
        const filtered =
          wanted === "all" ? entries : entries.filter((entry) => entry.type === wanted);
        const capped = filtered.slice(0, Math.max(1, Math.min(limit ?? 50, 500)));
        return {
          libraryRoot: resolved.libraryRoot,
          total: entries.length,
          returned: capped.length,
          wallpapers: capped.map((entry) => ({
            id: entry.id,
            title: entry.title,
            type: entry.type,
            tags: entry.tags,
            sizeMb: entry.sizeMb,
          })),
        };
      },
    }),
    tool({
      name: "wallpaper_search",
      description:
        "Search the Wallpaper Engine library by title, tag, or id. Returns ids suitable for wallpaper_ui_set.",
      parameters: Type.Object({
        query: Type.String({ description: "Keyword to search for." }),
        limit: Type.Optional(
          Type.Number({ description: "Maximum number of matches (default 20).", minimum: 1 }),
        ),
      }),
      execute: async (
        { query, limit }: { query: string; limit?: number },
        config?: Record<string, unknown>,
      ) => {
        const resolved = await readPluginConfig(config);
        const entries = await scanLibrary(resolved.libraryRoot);
        const needle = query.trim().toLowerCase();
        const matches = entries.filter(
          (entry) =>
            entry.title.toLowerCase().includes(needle) ||
            entry.tags.some((tag) => tag.toLowerCase().includes(needle)) ||
            entry.id.includes(needle),
        );
        const capped = matches.slice(0, Math.max(1, Math.min(limit ?? 20, 200)));
        return {
          query,
          total: matches.length,
          returned: capped.length,
          matches: capped.map((entry) => ({
            id: entry.id,
            title: entry.title,
            type: entry.type,
            tags: entry.tags,
          })),
        };
      },
    }),
    tool({
      name: "wallpaper_info",
      description: "Show detailed information about one wallpaper by id or title keyword.",
      parameters: Type.Object({
        query: Type.String({ description: "Wallpaper id (workshop id) or a title keyword." }),
      }),
      execute: async ({ query }: { query: string }, config?: Record<string, unknown>) => {
        const resolved = await readPluginConfig(config);
        const entries = await scanLibrary(resolved.libraryRoot);
        const entry = pickEntry(entries, query);
        if (!entry) {
          return { found: false, query, hint: "No wallpaper matched. Try wallpaper_search first." };
        }
        return {
          found: true,
          id: entry.id,
          title: entry.title,
          type: entry.type,
          tags: entry.tags,
          directory: entry.directory,
          preview: entry.preview,
          media: entry.media,
          workshopUrl: entry.workshopUrl,
          sizeMb: entry.sizeMb,
        };
      },
    }),
    tool({
      name: "wallpaper_ui_set",
      description:
        "Set the wallpaper shown behind the OpenClaw Control UI (web interface). Accepts a wallpaper id or title keyword. MP4/WebM and GIF stay animated; native scenes use their previews. The page picks up the change within seconds without restarting the gateway.",
      parameters: Type.Object({
        query: Type.String({ description: "Wallpaper id (workshop id) or title keyword." }),
      }),
      execute: async ({ query }: { query: string }, config?: Record<string, unknown>) => {
        const resolved = await readPluginConfig(config);
        if (!resolved.controlUiRoot) {
          return {
            applied: false,
            reason: "controlUiRoot is not configured; cannot locate the Control UI asset root.",
          };
        }
        const entries = await scanLibrary(resolved.libraryRoot);
        const entry = pickEntry(entries, query);
        if (!entry) {
          return { applied: false, query, hint: "No wallpaper matched. Try wallpaper_search first." };
        }
        const source = entry.media && [".mp4", ".webm"].includes(path.extname(entry.media).toLowerCase()) ? entry.media : entry.preview ?? entry.media;
        if (!source || !(await fileExists(source))) {
          return {
            applied: false,
            id: entry.id,
            title: entry.title,
            reason: "No readable preview or media file inside the wallpaper directory.",
          };
        }

        const ext = path.extname(source).toLowerCase();
        const video = [".mp4", ".webm"].includes(ext);
        const still = video || ext === ".gif" || IMAGE_EXTENSIONS.has(ext) ? source : await toStaticImage(source, resolved);
        const scriptPath = await ensureUiScript(resolved);
        const assetName = `wp-${entry.id}${path.extname(still).toLowerCase()}`;
        const assetPath = path.join(uiAssetDir(resolved), assetName);
        await fs.copyFile(still, assetPath);

        const current = await readUiConfig(resolved);
        const next = await writeUiConfig(resolved, {
          ...current,
          enabled: true,
          image: assetName,
          mediaType: video ? "video" : "image",
          translucentApp: true,
          wallpaperId: entry.id,
          title: entry.title,
        });

        return {
          applied: true,
          target: "openclaw-control-ui",
          id: entry.id,
          title: entry.title,
          type: entry.type,
          sourceFile: source,
          uiAsset: assetPath,
          uiScript: scriptPath,
          config: next,
          note:
            still === source
              ? "Copied the library preview into the Control UI assets."
              : "Animated source converted to a still frame before copying (browsers cannot play Wallpaper Engine scenes).",
        };
      },
    }),
    tool({
      name: "wallpaper_ui_import",
      description:
        "Import a local image, GIF or video file from any path on this machine (not only the Wallpaper Engine library) and use it as the OpenClaw Control UI background. Copies the file into the Control UI assets so the browser can load it.",
      parameters: Type.Object({
        path: Type.String({ description: "Absolute path to a local image / GIF / video file." }),
        title: Type.Optional(
          Type.String({ description: "Label shown in status; defaults to the file name." }),
        ),
      }),
      execute: async (
        { path: source, title }: { path: string; title?: string },
        config?: Record<string, unknown>,
      ) => {
        const resolved = await readPluginConfig(config);
        if (!resolved.controlUiRoot) {
          return {
            applied: false,
            reason: "controlUiRoot is not configured; cannot locate the Control UI asset root.",
          };
        }
        const ext = path.extname(source).toLowerCase();
        const isVideo = IMPORT_VIDEO_EXTENSIONS.has(ext);
        const isImage = IMPORT_IMAGE_EXTENSIONS.has(ext);
        if (!isVideo && !isImage) {
          throw new Error(`Unsupported media extension: ${ext || "(none)"}`);
        }
        if (!(await fileExists(source))) throw new Error(`Media file not found: ${source}`);
        const stat = await fs.stat(source);
        if (!stat.isFile()) throw new Error("The given path is not a file.");

        await ensureUiScript(resolved);
        const digest = createHash("sha1")
          .update(`${source}:${stat.size}:${stat.mtimeMs}`)
          .digest("hex")
          .slice(0, 12);
        const assetName = `import-${digest}${ext}`;
        const assetPath = path.join(uiAssetDir(resolved), assetName);
        await fs.copyFile(source, assetPath);

        const current = await readUiConfig(resolved);
        const next = await writeUiConfig(resolved, {
          ...current,
          enabled: true,
          image: assetName,
          mediaType: isVideo ? "video" : "image",
          translucentApp: true,
          wallpaperId: null,
          title: title && title.trim() ? title.trim() : path.basename(source),
        });

        return {
          applied: true,
          target: "openclaw-control-ui",
          imported: source,
          uiAsset: assetPath,
          sizeBytes: stat.size,
          kind: isVideo ? "video" : "image",
          config: next,
          note: "Imported a local media file into the Control UI assets (not from the Wallpaper Engine library).",
        };
      },
    }),
    tool({
      name: "wallpaper_ui_config",
      description:
        "Tune the Control UI background (opacity, blur, dark scrim, fit, translucency) and the console typography (font colour, size, weight, family, caret colour).",
      parameters: Type.Object({
        opacity: Type.Optional(
          Type.Number({ description: "Wallpaper opacity 0..1 (default 0.55).", minimum: 0, maximum: 1 }),
        ),
        blur: Type.Optional(Type.Number({ description: "Blur radius in px (default 0).", minimum: 0 })),
        scrim: Type.Optional(
          Type.Number({
            description: "Dark overlay opacity 0..1 for readability (default 0.45).",
            minimum: 0,
            maximum: 1,
          }),
        ),
        scrimColor: Type.Optional(Type.String({ description: "Scrim color, e.g. #0b0e13." })),
        fit: Type.Optional(
          Type.String({ description: "CSS background-size: cover, contain, or auto." }),
        ),
        translucentApp: Type.Optional(
          Type.Boolean({
            description: "Let the app shell show the wallpaper through (default false).",
          }),
        ),
        appAlpha: Type.Optional(
          Type.Number({
            description: "App tint opacity when translucentApp is on (default 0.82).",
            minimum: 0,
            maximum: 1,
          }),
        ),
        fontCustom: Type.Optional(
          Type.Boolean({ description: "Enable the custom console typography below (default false)." }),
        ),
        fontColor: Type.Optional(
          Type.String({ description: "Console text colour #RRGGBB (default #f2f3f5)." }),
        ),
        fontSize: Type.Optional(
          Type.Number({
            description: "Console base font size in px, 10..28; 0 keeps the default.",
            minimum: 0,
            maximum: 28,
          }),
        ),
        fontWeight: Type.Optional(
          Type.Number({
            description: "Console font weight 100..900 in steps of 100; 0 keeps the default.",
            minimum: 0,
            maximum: 900,
          }),
        ),
        fontFamily: Type.Optional(
          Type.String({ description: "Console font family; empty string inherits." }),
        ),
        caretColor: Type.Optional(
          Type.String({ description: "Console caret / selection colour #RRGGBB." }),
        ),
      }),
      execute: async (
        patch: {
          opacity?: number;
          blur?: number;
          scrim?: number;
          scrimColor?: string;
          fit?: string;
          translucentApp?: boolean;
          appAlpha?: number;
          fontCustom?: boolean;
          fontColor?: string;
          fontSize?: number;
          fontWeight?: number;
          fontFamily?: string;
          caretColor?: string;
        },
        config?: Record<string, unknown>,
      ) => {
        const resolved = await readPluginConfig(config);
        await ensureUiScript(resolved);
        if (patch.fit && !["cover", "contain", "auto"].includes(patch.fit)) throw new Error("Invalid fit");
        if (patch.scrimColor && !/^#[0-9a-f]{6}$/i.test(patch.scrimColor)) throw new Error("Use a #RRGGBB scrim color");
        if (patch.fontColor && !/^#[0-9a-f]{6}$/i.test(patch.fontColor)) throw new Error("Use a #RRGGBB font color");
        if (patch.caretColor && !/^#[0-9a-f]{6}$/i.test(patch.caretColor)) throw new Error("Use a #RRGGBB caret color");
        if (patch.fontSize !== undefined && (!Number.isFinite(patch.fontSize) || patch.fontSize < 0 || patch.fontSize > 28)) {
          throw new Error("fontSize must be 0..28");
        }
        if (patch.fontWeight !== undefined && (!Number.isFinite(patch.fontWeight) || patch.fontWeight < 0 || patch.fontWeight > 900)) {
          throw new Error("fontWeight must be 0..900");
        }
        if (patch.fontFamily !== undefined && patch.fontFamily.length > 120) throw new Error("fontFamily is too long");
        const current = await readUiConfig(resolved);
        const next = await writeUiConfig(resolved, { ...current, ...patch });
        return { updated: true, config: next };
      },
    }),
    tool({
      name: "wallpaper_ui_off",
      description: "Turn off the Control UI wallpaper (keeps the last selection for later).",
      parameters: Type.Object({}),
      execute: async (_params: Record<string, never>, config?: Record<string, unknown>) => {
        const resolved = await readPluginConfig(config);
        const current = await readUiConfig(resolved);
        const next = await writeUiConfig(resolved, { ...current, enabled: false });
        return { enabled: false, config: next };
      },
    }),
    tool({
      name: "wallpaper_ui_status",
      description: "Report the wallpaper currently configured for the OpenClaw Control UI.",
      parameters: Type.Object({}),
      execute: async (_params: Record<string, never>, config?: Record<string, unknown>) => {
        const resolved = await readPluginConfig(config);
        const current = await readUiConfig(resolved);
        const scriptInstalled = await fileExists(path.join(uiAssetDir(resolved), "wallpaper.js"));
        const library = await scanLibrary(resolved.libraryRoot);
        return {
          controlUiRoot: resolved.controlUiRoot,
          libraryRoot: resolved.libraryRoot,
          scriptInstalled,
          libraryCount: library.length,
          config: current,
        };
      },
    }),
    tool({
      name: "wallpaper_desktop_set",
      description:
        "Apply a wallpaper from the library as the Windows desktop wallpaper (separate from the Control UI background).",
      parameters: Type.Object({
        query: Type.String({ description: "Wallpaper id or title keyword." }),
      }),
      execute: async ({ query }: { query: string }, config?: Record<string, unknown>) => {
        const resolved = await readPluginConfig(config);
        const entries = await scanLibrary(resolved.libraryRoot);
        const entry = pickEntry(entries, query);
        if (!entry) {
          return { applied: false, query, hint: "No wallpaper matched. Try wallpaper_search first." };
        }
        const source = entry.preview ?? entry.media;
        if (!source || !(await fileExists(source))) {
          return { applied: false, id: entry.id, reason: "No readable preview file." };
        }
        const still = await toStaticImage(source, resolved);
        await applyWallpaperToDesktop(still);
        return {
          applied: true,
          target: "windows-desktop",
          id: entry.id,
          title: entry.title,
          appliedFile: still,
        };
      },
    }),
  ],
};
const toolPlugin = {
  id: definition.id, name: definition.name, description: definition.description,
  configSchema: {jsonSchema: definition.configSchema},
  register(api: PluginApi) {
    for (const definition of definitionTools) api.registerTool({
      name: definition.name, label: definition.name, description: definition.description, parameters: definition.parameters,
      execute: async (_id, params) => {
        const details = await definition.execute(params, api.pluginConfig);
        return {content:[{type:"text",text:JSON.stringify(details)}],details};
      },
    });
  },
};
const definitionTools = definition.tools;

// Reuse the exact tool implementations for the authenticated Control UI RPCs.
// No local server, unauthenticated write endpoint, or browser credential storage is added.
export default {
  ...toolPlugin,
  register(api: Parameters<typeof toolPlugin.register>[0]) {
    const registered = new Map<string, AnyAgentTool>();
    toolPlugin.register({ ...api, registerTool(tool, options) {
      if (typeof tool !== "function") registered.set(tool.name, tool);
      api.registerTool(tool, options);
    }});
    for (const [method, name] of Object.entries({
      "wallpaper.list": "wallpaper_list", "wallpaper.set": "wallpaper_ui_set",
      "wallpaper.status": "wallpaper_ui_status", "wallpaper.off": "wallpaper_ui_off",
      "wallpaper.configure": "wallpaper_ui_config",
      "wallpaper.import": "wallpaper_ui_import",
    })) {
      api.registerGatewayMethod(method, async ({ params, respond }) => {
        try {
          const tool = registered.get(name);
          if (!tool) throw new Error("Wallpaper tool not available");
          const input = params ?? {};
          if (method === "wallpaper.set" && (typeof input.query !== "string" || !input.query.trim())) throw new Error("Wallpaper id is required");
          if (method === "wallpaper.import" && (typeof input.path !== "string" || !input.path.trim())) throw new Error("A local media path is required");
          if (method === "wallpaper.configure") {
            const allowed = new Set(["opacity", "blur", "scrim", "scrimColor", "fit", "translucentApp", "appAlpha", "fontCustom", "fontColor", "fontSize", "fontWeight", "fontFamily", "caretColor"]);
            for (const [key,value] of Object.entries(input)) {
              if (!allowed.has(key)) throw new Error("Unknown wallpaper setting");
              if (["opacity","scrim","appAlpha","blur"].includes(key) && (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > (key === "blur" ? 40 : 1))) throw new Error("Wallpaper setting outside supported range");
              if ((key === "translucentApp" || key === "fontCustom") && typeof value !== "boolean") throw new Error("Expected boolean");
              if (key === "fontSize" && (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 28)) throw new Error("Wallpaper setting outside supported range");
              if (key === "fontWeight" && (typeof value !== "number" || !Number.isFinite(value) || value < 0 || value > 900)) throw new Error("Wallpaper setting outside supported range");
              if ((key === "fontColor" || key === "caretColor") && (typeof value !== "string" || !/^#[0-9a-f]{6}$/i.test(value))) throw new Error("Use a #RRGGBB colour");
              if (key === "fontFamily" && (typeof value !== "string" || value.length > 120)) throw new Error("Invalid font family");
            }
          }
          const result = await tool.execute("wallpaper-control-ui", method === "wallpaper.list" ? {limit:500} : input);
          respond(true, result.details);
        } catch(error) { respond(false, undefined, {code:"INVALID_REQUEST",message:error instanceof Error?error.message:String(error)}); }
      }, {scope: method === "wallpaper.list" || method === "wallpaper.status" ? "operator.read" : "operator.write"});
    }
    api.registerService({id:"wallpaper-engine-ui",start:async()=>{
      const config = await readPluginConfig(api.pluginConfig);
      await ensureUiScript(config);
    }});
  },
};
