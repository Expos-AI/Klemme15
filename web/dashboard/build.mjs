// Builds the dashboard into src/Klemme15.Dashboard/wwwroot (committed, so building Klemme15 doesn't need Node).
import * as esbuild from "esbuild";
import { readFileSync, writeFileSync, mkdirSync } from "node:fs";

const out = new URL("../../src/Klemme15.Dashboard/wwwroot/", import.meta.url);
mkdirSync(out, { recursive: true });
const options = {
  entryPoints: ["src/main.tsx"],
  bundle: true,
  minify: true,
  format: "iife",
  target: ["es2020", "safari14.1"],   // phones from 2021 on (iOS 14.5+): esbuild 0.28 no longer rewrites destructuring for older Safari
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
