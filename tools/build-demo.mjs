#!/usr/bin/env node
/**
 * 把 data/bank/*.json 打包进 demo/app.html，产出 demo/index.html。
 *
 * 目的：本机没有 dotnet，也没有 Windows，无法把 WPF 版本跑起来给人看。
 * 但 UI 的视觉规格（docs/SPEC.md 第三节 + docs/CONTRACTS.md D 节）是像素级的，
 * 用一份等价的前端实现先把观感和交互验证掉 —— 判分口径、抽题算法、隐蔽口令
 * 状态机全部直接搬 tools/core-reference.mjs 的实现，不是重新发明。
 *
 * 用法： node tools/build-demo.mjs
 */

import { readFileSync, writeFileSync, readdirSync, existsSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const BANK_DIR = join(ROOT, 'data/bank');
const TEMPLATE = join(ROOT, 'demo/app.html');
const OUT = join(ROOT, 'demo/index.html');

const slug = ['chinese', 'math', 'english'];

/** "6上" / "7下" 这类学期键 → UI 显示名 */
const TERM_LABEL = {
  '6上': '六年级上',
  '6下': '六年级下',
  '7上': '初一上',
  '7下': '初一下',
  '8上': '初二上',
  '8下': '初二下',
  '9上': '初三上',
  '9下': '初三下',
};

const rows = [];
const seenIds = new Set();
const perFile = [];

if (!existsSync(BANK_DIR)) {
  console.error(`题库目录不存在：${BANK_DIR}`);
  process.exit(1);
}

for (const file of readdirSync(BANK_DIR).filter(f => f.endsWith('.json')).sort()) {
  let doc;
  try {
    doc = JSON.parse(readFileSync(join(BANK_DIR, file), 'utf8'));
  } catch (e) {
    console.error(`跳过 ${file}：JSON 解析失败 ${e.message}`);
    continue;
  }
  if (!Array.isArray(doc.items) || !doc.items.length) continue;

  const sIdx = slug.indexOf(doc.subject);
  if (sIdx < 0) { console.error(`跳过 ${file}：subject 非法 ${doc.subject}`); continue; }

  const key = `${doc.grade}${doc.term}`;
  let kept = 0;

  for (const it of doc.items) {
    if (!it || typeof it.id !== 'string' || !it.id) continue;
    if (seenIds.has(it.id)) continue;
    if (!Array.isArray(it.options) || it.options.length !== 4) continue;
    if (!Number.isInteger(it.answer) || it.answer < 0 || it.answer > 3) continue;

    seenIds.add(it.id);
    rows.push({
      id: it.id,
      key,
      s: sIdx,
      q: it.stem,
      o: it.options,
      a: it.answer,
      e: it.explanation ?? '',
    });
    kept++;
  }
  perFile.push({ file, kept });
}

if (!rows.length) {
  console.error('题库为空，无法生成演示页面。');
  process.exit(1);
}

// 学期清单按 6上、6下、7上… 排序
const TERM_ORDER = ['6上', '6下', '7上', '7下', '8上', '8下', '9上', '9下'];
const terms = TERM_ORDER.filter(k => rows.some(r => r.key === k));
const labels = Object.fromEntries(terms.map(k => [k, TERM_LABEL[k] ?? k]));

// 混合卷：六上 + 六下 视为一个池
const MIX_6 = ['6上', '6下'].filter(k => terms.includes(k));

const payload = {
  rows,
  terms,
  labels,
  mix6: MIX_6,
};

const json = JSON.stringify(payload);

if (!existsSync(TEMPLATE)) {
  console.error(`模板不存在：${TEMPLATE}`);
  process.exit(1);
}
const tpl = readFileSync(TEMPLATE, 'utf8');
if (!tpl.includes('__BANK__')) {
  console.error('模板里没有 __BANK__ 占位符。');
  process.exit(1);
}

mkdirSync(dirname(OUT), { recursive: true });
writeFileSync(OUT, tpl.replace('__BANK__', json), 'utf8');

console.log(`生成 ${OUT.replace(ROOT + '/', '')}`);
console.log('');
for (const p of perFile) {
  console.log(`  ${p.file.padEnd(22)} ${String(p.kept).padStart(4)} 题`);
}
console.log('');
console.log(`  合计 ${rows.length} 题，${terms.length} 个学期：${terms.map(k => labels[k]).join(' / ')}`);
console.log(`  混合卷池：${MIX_6.map(k => labels[k]).join(' + ') || '（无）'}`);
