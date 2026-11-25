import * as fs from "node:fs/promises";
import { basename, join } from "node:path";

type NoticeEntry = {
  source?: string;
  files?: string[];
  packageFiles?: string[];
};

type NoticeCatalog = Record<string, NoticeEntry>;

const noticeName = /^(licen[cs]e|notice|copying|third[ ._-]*party[ ._-]*notices)/i;

async function publishedPackages(payload: string): Promise<string[]> {
  const directories = await fs.readdir(payload, { recursive: true, withFileTypes: true });
  const manifests = directories.filter((file) => file.isFile() && file.name.endsWith(".deps.json"));
  if (manifests.length === 0) throw new Error("The release has no published dependency manifests.");
  const packages = new Set<string>();
  for (const manifest of manifests) {
    const graph: { libraries: Record<string, { type: string }> } = JSON.parse(
      await fs.readFile(join(manifest.parentPath, manifest.name), "utf8"),
    );
    for (const [library, { type }] of Object.entries(graph.libraries)) {
      if (type !== "package" && type !== "runtimepack") continue;
      const identity = (type === "runtimepack" ? library.replace(/^runtimepack\./i, "") : library).toLowerCase();
      if (!/^[a-z0-9][a-z0-9_.-]*\/[0-9][a-z0-9.+-]*$/.test(identity)) {
        throw new Error(`Unexpected published package identity: ${library}`);
      }
      packages.add(identity);
    }
  }
  return [...packages].sort();
}

export async function stagePublishedNotices(
  payload: string,
  packageCache: string,
  sourceNotices: string,
): Promise<{ packages: string[]; files: string[] }> {
  const catalog: NoticeCatalog = JSON.parse(await fs.readFile(join(sourceNotices, "catalog.json"), "utf8"));
  const packages = await publishedPackages(payload);
  const files: string[] = [];
  const index = ["# Published package notices", "", "| Package | Notices | Upstream |", "| --- | --- | --- |"];
  for (const identity of packages) {
    const directory = join(packageCache, identity);
    const entry = catalog[identity];
    const packaged = (await fs.readdir(directory, { withFileTypes: true }))
      .filter((file) => file.isFile() && noticeName.test(file.name))
      .map((file) => file.name).sort();
    const candidates = new Map([
      ...packaged.map((file) => [file, join(directory, file)] as const),
      ...(entry?.packageFiles ?? []).map((file) => [basename(file), join(directory, file)] as const),
      ...(entry?.files ?? []).map((file) => [basename(file), join(sourceNotices, file)] as const),
    ]);
    if (candidates.size === 0) throw new Error(`No release license or notice files for ${identity}.`);
    const links: string[] = [];
    await fs.mkdir(join(payload, "licenses", "packages", identity), { recursive: true });
    for (const [file, input] of candidates) {
      const output = `packages/${identity}/${file}`;
      await fs.copyFile(input, join(payload, "licenses", output));
      files.push(`licenses/${output}`);
      links.push(`[${file}](${encodeURI(output)})`);
    }
    index.push(`| ${identity} | ${links.join(", ")} | ${entry?.source ? `[Source](${entry.source})` : ""} |`);
  }
  const inventory = "licenses/DEPENDENCIES.md";
  await fs.writeFile(join(payload, inventory), index.join("\n") + "\n");
  return { packages, files: [...files, inventory] };
}
