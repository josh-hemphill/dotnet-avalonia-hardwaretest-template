/**
 * Cobertura floor checks ported from tests/check-coverage.py.
 * Fail if Core < 70%, Hardware < 80%, or Engine < 80% when Engine lines exist.
 */

export type CoverageReport = {
  corePct: number;
  enginePct: number;
  hardwarePct: number;
  coreLines: number;
  coreCovered: number;
  engineLines: number;
  engineCovered: number;
  hardwareLines: number;
  hardwareCovered: number;
  ok: boolean;
  failures: string[];
  summary: string;
};

function pct(covered: number, total: number): number {
  return total === 0 ? 100 : (covered * 100) / total;
}

function isEngine(filename: string): boolean {
  return (
    filename.includes(".Engine.") ||
    filename.includes("/Engine/") ||
    filename.includes("\\Engine\\") ||
    filename.includes("Engine.")
  );
}

function isIviVisa(filename: string): boolean {
  return filename.includes("IviVisa") || filename.includes("IviMessage");
}

function isHardware(filename: string): boolean {
  return (
    filename.includes(".Hardware.") ||
    filename.includes("/Hardware/") ||
    filename.includes("\\Hardware\\") ||
    filename.includes("Hardware.")
  );
}

function attr(tag: string, name: string): string {
  const re = new RegExp(`\\b${name}="([^"]*)"`, "i");
  const m = tag.match(re);
  return m?.[1] ?? "";
}

/** Parse Cobertura XML and evaluate Core / Engine / Hardware floors. */
export function evaluateCobertura(xml: string): CoverageReport {
  if (!/<packages[\s>]/i.test(xml)) {
    throw new Error("no packages in cobertura report");
  }

  let coreLines = 0;
  let covered = 0;
  let engineLines = 0;
  let engineCovered = 0;
  let hardwareLines = 0;
  let hardwareCovered = 0;

  const packageRe = /<package\b[^>]*>[\s\S]*?<\/package>/gi;
  for (const pkgMatch of xml.matchAll(packageRe)) {
    const pkgXml = pkgMatch[0];
    const openTag = pkgXml.match(/^<package\b[^>]*>/i)?.[0] ?? "";
    const name = attr(openTag, "name");
    if (!name.includes("HardwareTest.Core")) continue;

    const classRe = /<class\b[^>]*>[\s\S]*?<\/class>/gi;
    for (const classMatch of pkgXml.matchAll(classRe)) {
      const classXml = classMatch[0];
      const classOpen = classXml.match(/^<class\b[^>]*>/i)?.[0] ?? "";
      const filename =
        `${attr(classOpen, "filename")} ${attr(classOpen, "name")}`;

      const lineRe = /<line\b[^>]*\/?>/gi;
      for (const lineMatch of classXml.matchAll(lineRe)) {
        const hits = Number.parseInt(attr(lineMatch[0], "hits") || "0", 10);
        coreLines += 1;
        if (hits > 0) covered += 1;

        if (isEngine(filename)) {
          engineLines += 1;
          if (hits > 0) engineCovered += 1;
        }

        if (isIviVisa(filename)) continue;

        if (isHardware(filename)) {
          hardwareLines += 1;
          if (hits > 0) hardwareCovered += 1;
        }
      }
    }
  }

  const corePct = pct(covered, coreLines);
  const enginePct = pct(engineCovered, engineLines);
  const hardwarePct = pct(hardwareCovered, hardwareLines);

  const failures: string[] = [];
  if (corePct < 70) failures.push("FAIL: Core coverage below 70%");
  if (engineLines > 0 && enginePct < 80) {
    failures.push("FAIL: Engine coverage below 80%");
  }
  if (hardwarePct < 80) failures.push("FAIL: Hardware coverage below 80%");

  const summary = [
    `Core line coverage: ${corePct.toFixed(1)}% (${covered}/${coreLines})`,
    `Engine line coverage: ${enginePct.toFixed(1)}% (${engineCovered}/${engineLines})`,
    `Hardware line coverage: ${hardwarePct.toFixed(1)}% (${hardwareCovered}/${hardwareLines})`,
  ].join("\n");

  return {
    corePct,
    enginePct,
    hardwarePct,
    coreLines,
    coreCovered: covered,
    engineLines,
    engineCovered,
    hardwareLines,
    hardwareCovered,
    ok: failures.length === 0,
    failures,
    summary,
  };
}

/** Find the first coverage.cobertura.xml under a results directory. */
export async function findCobertura(resultsDir: string): Promise<string | null> {
  try {
    for await (const entry of walkFiles(resultsDir)) {
      if (entry.endsWith("coverage.cobertura.xml")) return entry;
    }
  } catch (err) {
    if (err instanceof Deno.errors.NotFound) return null;
    throw err;
  }
  return null;
}

async function* walkFiles(dir: string): AsyncGenerator<string> {
  for await (const entry of Deno.readDir(dir)) {
    const full = `${dir}/${entry.name}`.replaceAll("\\", "/");
    if (entry.isDirectory) {
      yield* walkFiles(full);
    } else if (entry.isFile) {
      yield full;
    }
  }
}
