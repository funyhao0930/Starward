// 修正 pulsoicon-core 原版动作的地方：key 是图标名称，值是「形状序号 → 要换上的动作字段」。
// 生成器和预览页都会套用，所以 app 和预览看到的一样。

export const MOTION_OVERRIDES = {
  // 勾勾的路径是从右上角往左下画的，原版 draw 看起来像倒着画；改成 drawBack，从左边短笔画开始画
  "circle-check": { 1: { r: "drawBack" } },
  check: { 0: { r: "drawBack" } },
  // 回转箭头的弧线圆心在 (17, 17)（两段弧各以 (17,17)、(17.5,17) 为圆心），原版支点 (16, 16) 会让箭头绕着偏左上的点甩出去
  "database-backup": { 4: { o: [17, 17] }, 5: { o: [17, 17] } },
};

/** 返回套用修正后的图标（不改动原对象） */
export function applyOverrides(icon) {
  const fix = MOTION_OVERRIDES[icon.key];
  if (!fix) return icon;
  return {
    ...icon,
    nodes: icon.nodes.map((node, i) => (fix[i] && node[2] ? [node[0], node[1], { ...node[2], ...fix[i] }] : node)),
  };
}
