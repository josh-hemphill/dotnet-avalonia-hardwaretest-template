import { writeTextFile } from "./lib/fs.ts";
import { aggregatePassed, eventProfile } from "./lib/profile.ts";
import { runCapture } from "./lib/run.ts";

if (process.argv.slice(2)[0] === "aggregate") {
  const jobs = JSON.parse(process.env["CI_NEEDS"] ?? "{}") as Record<
    string,
    { result: string }
  >;
  const results = Object.fromEntries(
    Object.entries(jobs).map(([key, value]) => [key, value.result]),
  );
  const required = (process.env["CI_REQUIRED"] ?? "").split(",").filter(
    Boolean,
  );
  const optional = (process.env["CI_OPTIONAL"] ?? "").split(",").filter(
    Boolean,
  );
  if (required.length === 0 || !aggregatePassed(results, required, optional)) {
    console.error(
      "Required CI child failed, was cancelled, or did not run",
      results,
    );
    process.exit(1);
  }
} else {
  let paths: string[] | undefined;
  if (process.env["GITHUB_EVENT_NAME"] === "pull_request") {
    try {
      const base = process.env["CI_BASE_SHA"] ?? "";
      const head = process.env["CI_HEAD_SHA"] ?? "";
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
  const profile = eventProfile(process.env["GITHUB_EVENT_NAME"] ?? "", paths);
  console.log(
    `CI profile=${profile}; changed paths=${paths?.length ?? "unknown"}`,
  );
  const output = process.env["GITHUB_OUTPUT"];
  if (output) {
    await writeTextFile(
      output,
      `profile=${profile}\nfull=${profile === "full"}\n`,
      {
        append: true,
      },
    );
  }
}
