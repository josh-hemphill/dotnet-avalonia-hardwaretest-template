/** Map Deno OS/arch to a .NET RID, or undefined when unknown. */
export function mapRid(os: string, arch: string): string | undefined {
  if (os === "windows" && arch === "x86_64") return "win-x64";
  if (os === "windows" && arch === "aarch64") return "win-arm64";
  if (os === "linux" && arch === "x86_64") return "linux-x64";
  if (os === "linux" && arch === "aarch64") return "linux-arm64";
  if (os === "darwin" && arch === "aarch64") return "osx-arm64";
  if (os === "darwin" && arch === "x86_64") return "osx-x64";
  return undefined;
}

/** Resolve a RID for OS/arch, or throw when unmapped. */
export function requireRid(os: string, arch: string): string {
  const mapped = mapRid(os, arch);
  if (!mapped) {
    throw new Error(`No default RID for ${os}/${arch}; pass --rid explicitly.`);
  }
  return mapped;
}

/** Resolve the default RID for the host OS/arch. */
export function defaultRid(): string {
  return requireRid(Deno.build.os, Deno.build.arch);
}

/** Whether this RID can run natively on the given host (defaults to this process). */
export function isNativeRid(
  rid: string,
  os: string = Deno.build.os,
  arch: string = Deno.build.arch,
): boolean {
  const mapped = mapRid(os, arch);
  return mapped !== undefined && rid === mapped;
}

/** Published HardwareTest file name for a RID (not the host OS). */
export function publishedExeName(rid: string): string {
  return rid.startsWith("win-") ? "HardwareTest.exe" : "HardwareTest";
}
