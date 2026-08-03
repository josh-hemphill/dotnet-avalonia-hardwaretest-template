/** Resolve the default RID for the host OS/arch. */
export function defaultRid(): string {
  const os = Deno.build.os;
  const arch = Deno.build.arch;
  if (os === "windows" && arch === "x86_64") return "win-x64";
  if (os === "linux" && arch === "x86_64") return "linux-x64";
  if (os === "darwin" && arch === "aarch64") return "osx-arm64";
  if (os === "darwin" && arch === "x86_64") return "osx-x64";
  throw new Error(`No default RID for ${os}/${arch}; pass --rid explicitly.`);
}

/** Whether this RID can run natively on the current host. */
export function isNativeRid(rid: string): boolean {
  return rid === defaultRid();
}
