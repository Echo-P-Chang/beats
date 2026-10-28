import { cp, mkdir, readdir, rm } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const siteDirectory = resolve(scriptDirectory, "..");
const vendorDirectory = resolve(siteDirectory, "wwwroot/vendor");
const assetsDirectory = resolve(siteDirectory, "wwwroot/assets");

await rm(vendorDirectory, { recursive: true, force: true });
await rm(assetsDirectory, { recursive: true, force: true });
await mkdir(vendorDirectory, { recursive: true });
await mkdir(resolve(vendorDirectory, "marked"), { recursive: true });
await mkdir(resolve(vendorDirectory, "mermaid/chunks"), { recursive: true });

await cp(
  resolve(siteDirectory, "node_modules/marked/lib/marked.esm.js"),
  resolve(vendorDirectory, "marked/marked.esm.js"),
  { recursive: true }
);

await cp(
  resolve(siteDirectory, "node_modules/mermaid/dist/mermaid.esm.min.mjs"),
  resolve(vendorDirectory, "mermaid/mermaid.esm.min.mjs")
);

const mermaidChunks = resolve(vendorDirectory, "mermaid/chunks/mermaid.esm.min");
await cp(
  resolve(siteDirectory, "node_modules/mermaid/dist/chunks/mermaid.esm.min"),
  mermaidChunks,
  { recursive: true }
);

async function removeSourceMaps(directory) {
  for (const entry of await readdir(directory, { withFileTypes: true })) {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory()) {
      await removeSourceMaps(path);
    } else if (entry.name.endsWith(".map")) {
      await rm(path);
    }
  }
}

await removeSourceMaps(mermaidChunks);

await mkdir(resolve(siteDirectory, "wwwroot/content"), { recursive: true });
await cp(
  resolve(siteDirectory, "../README.md"),
  resolve(siteDirectory, "wwwroot/content/README.md")
);
await cp(
  resolve(siteDirectory, "../README.zh-TW.md"),
  resolve(siteDirectory, "wwwroot/content/README.zh-TW.md")
);

await cp(
  resolve(siteDirectory, "../assets"),
  assetsDirectory,
  { recursive: true }
);

console.log("Client assets and Markdown content are ready.");
