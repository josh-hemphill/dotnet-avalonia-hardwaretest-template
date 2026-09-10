import * as path from "@std/path";
import { publishedExeName } from "./rid.ts";

/** Repo root (tools/ci/lib → ../../..). */
export function repoRoot(): string {
  return path.resolve(path.dirname(path.fromFileUrl(import.meta.url)), "../../..");
}

export function coverageDir(root = repoRoot()): string {
  return path.join(root, "artifacts", "coverage");
}

export function publishDir(rid: string, root = repoRoot()): string {
  return path.join(root, "artifacts", "publish", rid);
}

/** Published HardwareTest path for a RID (win-* uses .exe). */
export function publishedExe(rid: string, root = repoRoot()): string {
  return path.join(publishDir(rid, root), publishedExeName(rid));
}
