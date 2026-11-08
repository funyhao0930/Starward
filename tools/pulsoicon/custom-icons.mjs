// 自己画的图标，格式和 pulsoicon-core 一样：24×24 格子、2 单位圆头线，四周留 2 格。
// 每个形状是 [标签, 属性, 动作]，动作见 pulsoicon-core 的 recipes（r 动作名、d 延迟秒、x/y 方向、a 幅度、o 支点）。
// 改完执行 node generate.mjs；node preview.mjs 可以先输出预览页，和 Lucide 原版图标并排比较。

/** 四角星，(cx, cy) 为中心、r 为半径、w 控制腰身粗细 */
const star = (cx, cy, r, w = 0.6) =>
  `M${cx} ${cy - r}Q${cx + w} ${cy - w} ${cx + r} ${cy}Q${cx + w} ${cy + w} ${cx} ${cy + r}` +
  `Q${cx - w} ${cy + w} ${cx - r} ${cy}Q${cx - w} ${cy - w} ${cx} ${cy - r}Z`;

/** 棋盘被星星砸到时一起压一下，支点在棋盘底角 */
const boardImpact = { r: "press", d: 0.28, a: 0.08, o: [12, 22] };

/**
 * 千星奇域：一颗星星落在等角视角的棋盘上。
 * 指针移上去时星星从上方落下、带一点回弹，落地时棋盘被压一下再弹回。
 */
const MiliastraBoard = {
  name: "MiliastraBoard",
  key: "miliastra-board",
  nodes: [
    // 棋盘面
    ["path", { d: "M12 13l9 4.5-9 4.5-9-4.5z" }, boardImpact],
    // 把棋盘分成四格（16 像素时再加台座厚度会糊成一团，所以不画）
    ["path", { d: "M7.5 15.25l9 4.5M16.5 15.25l-9 4.5" }, boardImpact],
    // 落下的星星
    ["path", { d: star(12, 6, 4) }, { r: "drop", y: -1, a: 5, o: [12, 6] }],
  ],
};

export const CUSTOM_ICONS = [MiliastraBoard];
