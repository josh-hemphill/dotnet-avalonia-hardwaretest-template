import { test } from "node:test";
import { assertEquals, assertThrows } from "./lib/assert.ts";
import {
  defaultRid,
  isNativeRid,
  mapRid,
  publishedExeName,
  requireRid,
} from "./lib/rid.ts";
import { publishedExe } from "./lib/paths.ts";

test("mapRid covers CI and extra native hosts", () => {
  assertEquals(mapRid("win32", "x64"), "win-x64");
  assertEquals(mapRid("win32", "arm64"), "win-arm64");
  assertEquals(mapRid("linux", "x64"), "linux-x64");
  assertEquals(mapRid("linux", "arm64"), "linux-arm64");
  assertEquals(mapRid("darwin", "arm64"), "osx-arm64");
  assertEquals(mapRid("darwin", "x64"), "osx-x64");
  assertEquals(mapRid("freebsd", "x64"), undefined);
});

test("defaultRid and isNativeRid agree on this host", () => {
  const rid = defaultRid();
  assertEquals(isNativeRid(rid), true);
  assertEquals(isNativeRid("not-a-rid"), false);
});

test("isNativeRid is false on an unmapped host without throwing", () => {
  assertEquals(mapRid("plan9", "x64"), undefined);
  assertEquals(isNativeRid("linux-x64", "plan9", "x64"), false);
});

test("publishedExeName follows the RID, not the host OS", () => {
  assertEquals(publishedExeName("win-x64"), "HardwareTest.exe");
  assertEquals(publishedExeName("win-arm64"), "HardwareTest.exe");
  assertEquals(publishedExeName("linux-x64"), "HardwareTest");
  assertEquals(publishedExeName("osx-arm64"), "HardwareTest");
});

test("publishedExe joins publish dir with the RID file name", () => {
  const win = publishedExe("win-x64", "/repo");
  const linux = publishedExe("linux-x64", "/repo");
  assertEquals(win.replaceAll("\\", "/").endsWith("artifacts/publish/win-x64/HardwareTest.exe"), true);
  assertEquals(linux.replaceAll("\\", "/").endsWith("artifacts/publish/linux-x64/HardwareTest"), true);
});

test("requireRid throws a useful error for unknown OS/arch", () => {
  assertThrows(() => requireRid("plan9", "x64"), Error, "No default RID");
});
