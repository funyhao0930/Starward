// 把 PulsoIcon（https://github.com/CityPulso/PulsoIcon，ISC）挑出来的图标转成 C# 数据，
// 输出到 src/Starward/Controls/PulsoIcon/PulsoIconData.g.cs。
//
// 用法：
//   cd tools/pulsoicon
//   npm install
//   node generate.mjs
//
// 要加图标就改 ICONS 列表（kebab-case 名称，在 https://pulsoicon.com 查），重新执行。
// 自己画的图标放在 custom-icons.mjs，格式和 pulsoicon-core 一样，会一起生成。
// 路径在这里先转成绝对坐标，弧线和二次曲线都转成三次贝塞尔，C# 那边只要处理 M / L / C / Z。

import { createRequire } from "node:module";
import { readFileSync, writeFileSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { CUSTOM_ICONS } from "./custom-icons.mjs";
import { applyOverrides } from "./overrides.mjs";

const ICONS = [
  // 刷新按钮
  "refresh-cw",
  // 主导航栏
  "house",
  "gamepad-2",
  "images",
  "sparkles",
  "toolbox",
  "scroll-text",
  // 唤取记录页、游戏选择器
  "settings",
  "panel-right-open",
  "search",
  // 设置页导航
  "info",
  "sliders-horizontal",
  "cloud-download",
  "folder-cog",
  "keyboard",
  "gamepad",
  "flask-conical",
  // 设置页内容
  "circle-fading-arrow-up",
  "link",
  "activity",
  "folder-input",
  "x",
  "external-link",
  "database-backup",
  "trash",
  "folder-open",
  "brush-cleaning",
  "circle-check",
  "delete",
  "undo-2",
  // 主窗口、游戏选择器的固定、唤取记录的其他设置
  "layout-grid",
  "pin",
  "copy",
  "circle-question-mark",
  "check",
  "import",
];

const here = dirname(fileURLToPath(import.meta.url));
const require = createRequire(import.meta.url);
const svgpath = require("svgpath");
const coreDir = resolve(here, "node_modules/pulsoicon-core");
const coreVersion = JSON.parse(readFileSync(resolve(coreDir, "package.json"), "utf8")).version;
const output = resolve(here, "../../src/Starward/Controls/PulsoIcon/PulsoIconData.g.cs");

const num = (v) => Number.parseFloat(v ?? "0") || 0;

/** 各种 SVG 形状转成路径字符串 */
function toPathData(tag, a) {
  switch (tag) {
    case "path":
      return a.d;
    case "line":
      return `M${num(a.x1)} ${num(a.y1)}L${num(a.x2)} ${num(a.y2)}`;
    case "circle": {
      const cx = num(a.cx), cy = num(a.cy), r = num(a.r);
      return `M${cx - r} ${cy}a${r} ${r} 0 1 0 ${2 * r} 0a${r} ${r} 0 1 0 ${-2 * r} 0z`;
    }
    case "ellipse": {
      const cx = num(a.cx), cy = num(a.cy), rx = num(a.rx), ry = num(a.ry);
      return `M${cx - rx} ${cy}a${rx} ${ry} 0 1 0 ${2 * rx} 0a${rx} ${ry} 0 1 0 ${-2 * rx} 0z`;
    }
    case "rect": {
      const x = num(a.x), y = num(a.y), w = num(a.width), h = num(a.height);
      let rx = a.rx !== undefined ? num(a.rx) : a.ry !== undefined ? num(a.ry) : 0;
      let ry = a.ry !== undefined ? num(a.ry) : rx;
      rx = Math.min(rx, w / 2);
      ry = Math.min(ry, h / 2);
      if (!rx || !ry) return `M${x} ${y}h${w}v${h}h${-w}z`;
      return (
        `M${x + rx} ${y}h${w - 2 * rx}a${rx} ${ry} 0 0 1 ${rx} ${ry}v${h - 2 * ry}a${rx} ${ry} 0 0 1 ${-rx} ${ry}` +
        `h${-(w - 2 * rx)}a${rx} ${ry} 0 0 1 ${-rx} ${-ry}v${-(h - 2 * ry)}a${rx} ${ry} 0 0 1 ${rx} ${-ry}z`
      );
    }
    case "polyline":
    case "polygon": {
      const pts = a.points.trim().split(/[\s,]+/).map(Number);
      let d = `M${pts[0]} ${pts[1]}`;
      for (let i = 2; i < pts.length; i += 2) d += `L${pts[i]} ${pts[i + 1]}`;
      return tag === "polygon" ? d + "z" : d;
    }
    default:
      throw new Error(`unsupported tag ${tag}`);
  }
}

/** 路径转成操作码：0 M x y、1 L x y、2 C x1 y1 x2 y2 x y、3 Z */
function toOps(d, transform) {
  let p = svgpath(d);
  if (transform) p = p.transform(transform);
  p = p.abs().unshort().unarc();
  const ops = [];
  let cx = 0, cy = 0, sx = 0, sy = 0;
  p.iterate((seg) => {
    const [c, ...v] = seg;
    switch (c) {
      case "M": ops.push(0, v[0], v[1]); cx = sx = v[0]; cy = sy = v[1]; break;
      case "L": ops.push(1, v[0], v[1]); cx = v[0]; cy = v[1]; break;
      case "H": ops.push(1, v[0], cy); cx = v[0]; break;
      case "V": ops.push(1, cx, v[0]); cy = v[0]; break;
      case "C": ops.push(2, ...v); cx = v[4]; cy = v[5]; break;
      case "Q": {
        const [qx, qy, x, y] = v;
        ops.push(2, cx + (2 / 3) * (qx - cx), cy + (2 / 3) * (qy - cy), x + (2 / 3) * (qx - x), y + (2 / 3) * (qy - y), x, y);
        cx = x; cy = y; break;
      }
      case "Z": case "z": ops.push(3); cx = sx; cy = sy; break;
      default: throw new Error(`unexpected segment ${c}`);
    }
  });
  return ops;
}

const f = (n) => {
  const r = Math.round(n * 1000) / 1000;
  return `${Object.is(r, -0) ? 0 : r}f`;
};
const pascal = (s) => s.replace(/(^|-)([a-z])/g, (_, __, c) => c.toUpperCase());

function motion(m) {
  if (!m) return "null";
  const [ox, oy] = m.o ?? [12, 12];
  return `new(PulsoRecipe.${pascal(m.r)}, ${f(m.d ?? 0)}, ${f(m.x ?? 0)}, ${f(m.y ?? 0)}, ${f(m.a ?? 0)}, ${f(ox)}, ${f(oy)})`;
}

const enumLines = [];
const dataLines = [];
const sources = [];
for (const key of ICONS) {
  const mod = await import(pathToFileURL(resolve(coreDir, "dist/icons", `${key}.js`)).href);
  sources.push(applyOverrides(Object.values(mod)[0]));
}
sources.push(...CUSTOM_ICONS);

for (const icon of sources) {
  const name = icon.name;
  enumLines.push(`    ${name},`);
  const nodes = icon.nodes.map(([tag, attrs, m]) => {
    const pathData = toPathData(tag, attrs);
    const ops = toOps(pathData, m ? undefined : attrs.transform);
    const filled = attrs.fill !== undefined && attrs.fill !== "none";
    // 有闭合的子路径（z）才算闭合形状，PulsoIcon.IsFilled 只填满这种
    const closed = /z/i.test(pathData);
    return `            new([${ops.map((o) => (Number.isInteger(o) && o >= 0 && o <= 3 ? `${o}f` : f(o))).join(", ")}], ${filled}, ${closed}, ${motion(m)}),`;
  });
  dataLines.push(`        [PulsoIconKind.${name}] = new(${motion(icon.svg)},\n        [\n${nodes.join("\n")}\n        ]),`);
}

const code = `// <auto-generated>
// 由 tools/pulsoicon/generate.mjs 从 pulsoicon-core ${coreVersion} 和 custom-icons.mjs 生成，不要手改。
// 图标图形和动作来自 PulsoIcon（ISC），其中 Lucide 图形为 ISC，见 https://github.com/CityPulso/PulsoIcon/blob/main/LICENSE
// </auto-generated>
using System.Collections.Generic;

namespace Starward.Controls;

public enum PulsoIconKind
{
    None,
${enumLines.join("\n")}
}

internal static partial class PulsoIconData
{
    public static readonly Dictionary<PulsoIconKind, PulsoIconDefinition> Icons = new()
    {
${dataLines.join("\n")}
    };
}
`;

writeFileSync(output, code.replace(/\n/g, "\r\n"));
console.log(`wrote ${sources.length} icons (${CUSTOM_ICONS.length} custom) to ${output}`);
