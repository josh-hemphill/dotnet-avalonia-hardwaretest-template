import * as path from "@std/path";

/** Repo root (tools/ci → ../..). */
export function repoRoot(): string {
  return path.resolve(path.dirname(path.fromFileUrl(import.meta.url)), "../../..");
}

export function coverageDir(root = repoRoot()): string {
  return path.join(root, "artifacts", "coverage");
}

export function publishDir(rid: string, root = repoRoot()): string {
  return path.join(root, "artifacts", "publish", rid);
}
