/** Run a subprocess; inherit stdio; throw on non-zero exit. */
export async function run(
  cmd: string[],
  options: { cwd?: string; env?: Record<string, string> } = {},
): Promise<void> {
  const [exe, ...args] = cmd;
  if (!exe) throw new Error("run() requires a non-empty command");

  console.log(`+ ${cmd.map(shellQuote).join(" ")}`);
  const proc = new Deno.Command(exe, {
    args,
    cwd: options.cwd,
    env: options.env,
    stdin: "inherit",
    stdout: "inherit",
    stderr: "inherit",
  });
  const status = await proc.output();
  if (!status.success) {
    throw new Error(`Command failed (exit ${status.code}): ${cmd.join(" ")}`);
  }
}

/** Run a subprocess and capture stdout/stderr. */
export async function runCapture(
  cmd: string[],
  options: { cwd?: string; env?: Record<string, string> } = {},
): Promise<{ code: number; stdout: string; stderr: string }> {
  const [exe, ...args] = cmd;
  if (!exe) throw new Error("runCapture() requires a non-empty command");

  const proc = new Deno.Command(exe, {
    args,
    cwd: options.cwd,
    env: options.env,
    stdout: "piped",
    stderr: "piped",
  });
  const out = await proc.output();
  return {
    code: out.code,
    stdout: new TextDecoder().decode(out.stdout),
    stderr: new TextDecoder().decode(out.stderr),
  };
}

function shellQuote(s: string): string {
  return /\s/.test(s) ? JSON.stringify(s) : s;
}
