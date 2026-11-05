import { spawn } from "node:child_process";

/** Run without a shell so argument boundaries are preserved on every platform. */
async function execute(
  cmd: string[],
  options: { cwd?: string; env?: Record<string, string> },
  capture: boolean,
): Promise<{ code: number; stdout: string; stderr: string }> {
  const [exe, ...args] = cmd;
  if (!exe) throw new Error("A non-empty command is required");
  return await new Promise((resolve, reject) => {
    const child = spawn(exe, args, {
      cwd: options.cwd,
      env: { ...process.env, ...options.env },
      stdio: capture ? ["ignore", "pipe", "pipe"] : "inherit",
    });
    let stdout = "";
    let stderr = "";
    child.stdout?.setEncoding("utf8").on("data", (data: string) => stdout += data);
    child.stderr?.setEncoding("utf8").on("data", (data: string) => stderr += data);
    child.on("error", reject);
    child.on("close", (code, signal) => {
      if (signal) reject(new Error(`Command terminated by ${signal}: ${cmd.join(" ")}`));
      else resolve({ code: code ?? 1, stdout, stderr });
    });
  });
}

/** Run a subprocess; inherit stdio; throw on non-zero exit. */
export async function run(
  cmd: string[],
  options: { cwd?: string; env?: Record<string, string> } = {},
): Promise<void> {
  console.log(`+ ${cmd.map(shellQuote).join(" ")}`);
  const started = performance.now();
  const status = await execute(cmd, options, false);
  console.log(`duration=${((performance.now() - started) / 1000).toFixed(1)}s exit=${status.code} command=${cmd.join(" ")}`);
  if (status.code !== 0) throw new Error(`Command failed (exit ${status.code}): ${cmd.join(" ")}`);
}

/** Run a subprocess and capture stdout/stderr. */
export async function runCapture(
  cmd: string[],
  options: { cwd?: string; env?: Record<string, string> } = {},
): Promise<{ code: number; stdout: string; stderr: string }> {
  return await execute(cmd, options, true);
}

function shellQuote(s: string): string {
  return /\s/.test(s) ? JSON.stringify(s) : s;
}
