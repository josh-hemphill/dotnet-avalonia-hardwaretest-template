import * as path from "@std/path";

/** One workspace, deduplicated evaluated traversal inputs; generated in the root
 * so SDK selection and Directory.Build/Packages lookup match the regular build. */
export function formatSolution(
  root: string,
  projects: readonly string[],
): string {
  const unique = [
    ...new Set(projects.map((project) => path.relative(root, project))),
  ].sort();
  if (unique.length === 0) {
    throw new Error("Cannot format an empty project graph");
  }
  const escape = (value: string) =>
    value.replaceAll("&", "&amp;")
      .replaceAll('"', "&quot;").replaceAll("<", "&lt;").replaceAll(
        ">",
        "&gt;",
      );
  return `<Solution>\n${
    unique.map((project) => `  <Project Path="${escape(project)}" />`).join(
      "\n",
    )
  }\n</Solution>\n`;
}
