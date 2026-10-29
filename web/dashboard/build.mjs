// Builds the dashboard into src/Klemme15.Telemetry/wwwroot (committed, so building Klemme15 doesn't need Node).
import * as esbuild from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";

const out = new URL("../../src/Klemme15.Telemetry/wwwroot/", import.meta.url);
mkdirSync(out, { recursive: true });
const options = {
  entryPoints: ["src/main.tsx"],
  bundle: true,
  minify: true,
  format: "iife",
  target: ["es2019", "safari13"],
  jsx: "automatic",
  define: { "process.env.NODE_ENV": '"production"' },
  outfile: new URL("dashboard.js", out).pathname,
  legalComments: "eof",
};
if (process.argv.includes("--watch")) {
  const ctx = await esbuild.context(options);
  await ctx.watch();
} else {
  await esbuild.build(options);
}
writeFileSync(new URL("index.html", out), readFileSync("index.html"));
console.log("Built dashboard into", out.pathname);
