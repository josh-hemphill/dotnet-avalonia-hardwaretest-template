import { aggregatePassed, eventProfile } from "./lib/profile.ts";
import { runCapture } from "./lib/run.ts";

if (Deno.args[0] === "aggregate") {
  const jobs = JSON.parse(Deno.env.get("CI_NEEDS") ?? "{}") as Record<
    string,
    { result: string }
  >;
  const results = Object.fromEntries(
    Object.entries(jobs).map(([key, value]) => [key, value.result]),
  );
  const required = (Deno.env.get("CI_REQUIRED") ?? "").split(",").filter(
    Boolean,
  );
  const optional = (Deno.env.get("CI_OPTIONAL") ?? "").split(",").filter(
    Boolean,
  );
  if (required.length === 0 || !aggregatePassed(results, required, optional)) {
    console.error(
      "Required CI child failed, was cancelled, or did not run",
      results,
    );
    Deno.exit(1);
  }
} else {
  let paths: string[] | undefined;
  if (Deno.env.get("GITHUB_EVENT_NAME") === "pull_request") {
    try {
      const base = Deno.env.get("CI_BASE_SHA") ?? "";
      const head = Deno.env.get("CI_HEAD_SHA") ?? "";
      if (!/^[a-f0-9]{40}$/i.test(base) || !/^[a-f0-9]{40}$/i.test(head)) {
        throw new Error("Missing valid PR commit identities");
      }
      // Separate arguments, no branch interpolation. --no-renames includes both
      // deleted and added paths, so renames cannot conceal a shared dependency.
      const diff = await runCapture([
        "git",
        "diff",
        "--name-only",
        "--no-renames",
        "-z",
        `${base}...${head}`,
        "--",
      ]);
      if (diff.code !== 0) throw new Error(diff.stderr);
      paths = diff.stdout.split("\0").filter(Boolean);
    } catch (error) {
      console.warn(`Diff unavailable; selecting full validation: ${error}`);
    }
  }
  const profile = eventProfile(Deno.env.get("GITHUB_EVENT_NAME") ?? "", paths);
  console.log(
    `CI profile=${profile}; changed paths=${paths?.length ?? "unknown"}`,
  );
  const output = Deno.env.get("GITHUB_OUTPUT");
  if (output) {
    await Deno.writeTextFile(
      output,
      `profile=${profile}\nfull=${profile === "full"}\n`,
      {
        append: true,
      },
    );
  }
}
