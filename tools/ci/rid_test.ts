import { assertEquals, assertThrows } from "@std/assert";
import {
  defaultRid,
  isNativeRid,
  mapRid,
  publishedExeName,
  requireRid,
} from "./lib/rid.ts";
import { publishedExe } from "./lib/paths.ts";

Deno.test("mapRid covers CI and extra native hosts", () => {
  assertEquals(mapRid("windows", "x86_64"), "win-x64");
  assertEquals(mapRid("windows", "aarch64"), "win-arm64");
  assertEquals(mapRid("linux", "x86_64"), "linux-x64");
  assertEquals(mapRid("linux", "aarch64"), "linux-arm64");
  assertEquals(mapRid("darwin", "aarch64"), "osx-arm64");
  assertEquals(mapRid("darwin", "x86_64"), "osx-x64");
  assertEquals(mapRid("freebsd", "x86_64"), undefined);
});

Deno.test("defaultRid and isNativeRid agree on this host", () => {
  const rid = defaultRid();
  assertEquals(isNativeRid(rid), true);
  assertEquals(isNativeRid("not-a-rid"), false);
});

Deno.test("isNativeRid is false on an unmapped host without throwing", () => {
  assertEquals(mapRid("plan9", "x86_64"), undefined);
  assertEquals(isNativeRid("linux-x64", "plan9", "x86_64"), false);
});

Deno.test("publishedExeName follows the RID, not the host OS", () => {
  assertEquals(publishedExeName("win-x64"), "HardwareTest.exe");
  assertEquals(publishedExeName("win-arm64"), "HardwareTest.exe");
  assertEquals(publishedExeName("linux-x64"), "HardwareTest");
  assertEquals(publishedExeName("osx-arm64"), "HardwareTest");
});

Deno.test("publishedExe joins publish dir with the RID file name", () => {
  const win = publishedExe("win-x64", "/repo");
  const linux = publishedExe("linux-x64", "/repo");
  assertEquals(win.replaceAll("\\", "/").endsWith("artifacts/publish/win-x64/HardwareTest.exe"), true);
  assertEquals(linux.replaceAll("\\", "/").endsWith("artifacts/publish/linux-x64/HardwareTest"), true);
});

Deno.test("requireRid throws a useful error for unknown OS/arch", () => {
  assertThrows(() => requireRid("plan9", "x86_64"), Error, "No default RID");
});
