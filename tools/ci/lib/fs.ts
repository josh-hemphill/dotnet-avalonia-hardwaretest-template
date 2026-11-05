import { readFile, writeFile, mkdtemp } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

export const readTextFile = (file: string | URL): Promise<string> => readFile(file, "utf8");
export const writeTextFile = (file: string | URL, text: string, options: { append?: boolean } = {}): Promise<void> =>
  writeFile(file, text, { encoding: "utf8", flag: options.append ? "a" : "w" });
export const makeTempDir = (options: { prefix: string }): Promise<string> =>
  mkdtemp(join(tmpdir(), options.prefix));
