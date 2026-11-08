// 输出 preview.html：自绘图标和几个 Lucide 原版图标并排，深色 / 浅色背景、多种尺寸，用来检查比例和线条。
// 用法：node preview.mjs [输出路径] [草稿模块]，草稿模块导出 DRAFTS 数组时只预览草稿

import { writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { CUSTOM_ICONS } from "./custom-icons.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const core = (p) => pathToFileURL(resolve(here, "node_modules/pulsoicon-core/dist", p)).href;
const { iconFrame, frameMarkup } = await import(core("frame.js"));

const REFERENCES = ["house", "gamepad-2", "images", "sparkles", "puzzle"];
const refs = [];
for (const key of REFERENCES) refs.push(Object.values(await import(core(`icons/${key}.js`)))[0]);

const drafts = process.argv[3] ? (await import(pathToFileURL(resolve(process.argv[3])).href)).DRAFTS : null;
const mine = drafts ?? CUSTOM_ICONS;
const icons = [...mine, ...refs];
const sizes = [16, 20, 24, 48, 96];
let id = 0;
const svg = (icon, size) => frameMarkup(iconFrame(icon, { size, strokeWidth: 1.5, maskId: `m${id++}` }));

const row = (bg, fg) =>
  `<div class="row" style="background:${bg};color:${fg}">` +
  icons.map((icon) => `<div class="cell">${sizes.map((s) => svg(icon, s)).join("")}<span>${icon.key}</span></div>`).join("") +
  `</div>`;

// 大图加 24 格网格，看对齐
const grid = (icon) => {
  let lines = "";
  for (let i = 0; i <= 24; i++) lines += `<path d="M${i} 0v24M0 ${i}h24" stroke="${i % 2 ? "#2a2f3a" : "#3a4150"}" stroke-width="0.04"/>`;
  lines += `<rect x="2" y="2" width="20" height="20" fill="none" stroke="#c0504d" stroke-width="0.05"/>`;
  const inner = svg(icon, 240).replace(/<svg([^>]*)>/, `<svg$1>${lines}`);
  return `<div class="big">${inner}</div>`;
};

const html = `<!doctype html><meta charset="utf-8"><style>
body{margin:0;font:12px Segoe UI;background:#111}
.row{display:flex;gap:24px;padding:16px;align-items:flex-end}
.cell{display:flex;flex-direction:column;align-items:center;gap:6px}
.cell svg{display:block}
.big{display:inline-block;background:#1b1f27;color:#e8e8e8;margin:16px}
</style>
${row("#1b1f27", "#e8e8e8")}
${row("#f3f3f3", "#1a1a1a")}
${mine.map(grid).join("")}`;

const out = process.argv[2] ?? resolve(here, "preview.html");
writeFileSync(out, html);
console.log(out);
