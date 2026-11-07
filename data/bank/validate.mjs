#!/usr/bin/env node
// StudyGate 题库校验器  ——  node data/bank/validate.mjs
// 检查：JSON 合法 / 字面量与文件名一致 / options 恰 4 非空 / answer 0..3 整数 /
//       id 全局唯一且格式合规 / 跨文件题干去重（去空白）/ 每份 ≥40 题 / explanation 25-80 字

import { readdirSync, readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const BANK_DIR = dirname(fileURLToPath(import.meta.url));
const MIN_ITEMS = 40;
const MIN_EXPL = 25;   // 字符数（按 Unicode 码点计，汉字算 1）
const MAX_EXPL = 80;

// 学期清单为 8 个（六上 + 六下 + 七八九年级上下），共 24 份。
const SEMESTERS = [
  { grade: 6, term: '上' }, { grade: 6, term: '下' },
  { grade: 7, term: '上' }, { grade: 7, term: '下' },
  { grade: 8, term: '上' }, { grade: 8, term: '下' },
  { grade: 9, term: '上' }, { grade: 9, term: '下' },
];
const SUBJECTS = ['chinese', 'math', 'english'];
const SUBJ_CODE = { chinese: 'cn', math: 'ma', english: 'en' };
const TERM_CODE = { '上': 's', '下': 'x' };

const EXPECTED = SEMESTERS.flatMap(({ grade, term }) =>
  SUBJECTS.map(s => ({
    file: `g${grade}${term}-${s}.json`, grade, term, subject: s,
  })));

const errors = [];
const rows = [];
const idSeen = new Map();          // id -> file
const stemSeen = new Map();        // 去空白题干 -> {file, id}

const norm = (s) => String(s).replace(/\s+/g, '');
const chars = (s) => [...String(s)].length;

function addErr(file, id, msg) {
  errors.push({ file, id: id || '-', msg });
}

// ---- 1. 文件存在性与 JSON 合法性 ----
const present = new Set(readdirSync(BANK_DIR).filter(f => f.endsWith('.json')));

for (const spec of EXPECTED) {
  const row = {
    file: spec.file, count: 0, ok: true,
    problems: 0, answers: [0, 0, 0, 0],
  };
  if (!present.has(spec.file)) {
    addErr(spec.file, '-', '文件缺失');
    row.ok = false; row.problems++;
    rows.push(row);
    continue;
  }

  let data;
  try {
    data = JSON.parse(readFileSync(join(BANK_DIR, spec.file), 'utf8'));
  } catch (e) {
    addErr(spec.file, '-', `JSON 解析失败: ${e.message}`);
    row.ok = false; row.problems++;
    rows.push(row);
    continue;
  }

  // ---- 2. 顶层字段与文件名一致 ----
  const bad = [];
  if (data.grade !== spec.grade) bad.push(`grade=${JSON.stringify(data.grade)} 应为 ${spec.grade}`);
  if (data.term !== spec.term) bad.push(`term=${JSON.stringify(data.term)} 应为 "${spec.term}"`);
  if (data.subject !== spec.subject) bad.push(`subject=${JSON.stringify(data.subject)} 应为 "${spec.subject}"`);
  if (!Array.isArray(data.items)) bad.push('items 不是数组');
  for (const b of bad) { addErr(spec.file, '-', b); row.problems++; }
  if (!Array.isArray(data.items)) { row.ok = false; rows.push(row); continue; }

  const items = data.items;
  row.count = items.length;
  if (items.length < MIN_ITEMS) {
    addErr(spec.file, '-', `题数 ${items.length} < ${MIN_ITEMS}`);
    row.problems++;
  }

  const idPrefix = `g${spec.grade}${TERM_CODE[spec.term]}-${SUBJ_CODE[spec.subject]}-`;
  const idRe = new RegExp(`^${idPrefix}\\d{3}$`);

  items.forEach((it, idx) => {
    const where = `items[${idx}]`;
    const tag = (it && it.id) ? `${where} ${it.id}` : where;
    if (!it || typeof it !== 'object' || Array.isArray(it)) {
      addErr(spec.file, where, '不是对象'); row.problems++; return;
    }

    // id
    if (typeof it.id !== 'string' || !it.id) {
      addErr(spec.file, where, 'id 缺失或非字符串'); row.problems++;
    } else if (!idRe.test(it.id)) {
      addErr(spec.file, tag, `id 格式不符，应为 ${idPrefix}NNN`); row.problems++;
    } else if (idSeen.has(it.id)) {
      addErr(spec.file, tag, `id 重复，已出现在 ${idSeen.get(it.id)}`); row.problems++;
    } else {
      idSeen.set(it.id, spec.file);
    }

    // stem
    if (typeof it.stem !== 'string' || !norm(it.stem)) {
      addErr(spec.file, tag, 'stem 缺失或为空'); row.problems++;
    } else {
      const k = norm(it.stem);
      if (stemSeen.has(k)) {
        const p = stemSeen.get(k);
        addErr(spec.file, tag, `题干与 ${p.file}/${p.id} 重复`); row.problems++;
      } else {
        stemSeen.set(k, { file: spec.file, id: it.id });
      }
    }

    // options
    if (!Array.isArray(it.options) || it.options.length !== 4) {
      addErr(spec.file, tag, `options 必须恰 4 项，实际 ${Array.isArray(it.options) ? it.options.length : '非数组'}`);
      row.problems++;
    } else {
      it.options.forEach((o, oi) => {
        if (typeof o !== 'string' || !norm(o)) {
          addErr(spec.file, tag, `options[${oi}] 为空或非字符串`); row.problems++;
        }
      });
      const opts = it.options.map(norm);
      if (new Set(opts).size !== opts.length) {
        addErr(spec.file, tag, 'options 存在重复项'); row.problems++;
      }
    }

    // answer
    if (!Number.isInteger(it.answer) || it.answer < 0 || it.answer > 3) {
      addErr(spec.file, tag, `answer=${JSON.stringify(it.answer)} 不是 0..3 整数`); row.problems++;
    } else if (Array.isArray(it.options) && it.options.length === 4) {
      row.answers[it.answer]++;
    }

    // explanation
    if (typeof it.explanation !== 'string' || !norm(it.explanation)) {
      addErr(spec.file, tag, 'explanation 缺失或为空'); row.problems++;
    } else {
      const n = chars(it.explanation);
      if (n < MIN_EXPL || n > MAX_EXPL) {
        addErr(spec.file, tag, `explanation 长度 ${n}，要求 ${MIN_EXPL}-${MAX_EXPL} 字`);
        row.problems++;
      }
    }
  });

  row.ok = row.problems === 0;
  rows.push(row);
}

// 目录里的多余 json（非题库命名）
const stray = [...present].filter(f => !EXPECTED.some(e => e.file === f) && f !== 'validate.mjs');

// ---- 3. 汇总表 ----
// 中文按 2 列宽计，避免列粘连
const dispW = (s) => [...String(s)].reduce(
  (n, c) => n + (c.codePointAt(0) > 0x2e80 ? 2 : 1), 0);
const pad = (s, w) => String(s) + ' '.repeat(Math.max(0, w - dispW(s)));
const W = [20, 6, 15, 6, 8];
const line = (cells) => cells.map((c, i) => pad(c, W[i])).join('');
const head = line(['文件', '题数', '答案分布 0/1/2/3', '状态', '问题数']);
const sep = '-'.repeat(dispW(head));

console.log('\nStudyGate 题库校验 · data/bank');
console.log(sep);
console.log(head);
console.log(sep);
let total = 0;
for (const r of rows) {
  total += r.count;
  const dist = r.count ? r.answers.join('/') : '-';
  console.log(line([r.file, r.count, dist, r.ok ? 'PASS' : 'FAIL', r.problems]));
}
console.log(sep);
console.log(`文件 ${rows.length} 份 / 期望 ${EXPECTED.length} 份　合计题数 ${total}　问题 ${errors.length}`);

if (stray.length) {
  console.log(`\n提示：目录内非题库 JSON（已忽略）：${stray.join(', ')}`);
}

// answer 连续同索引预警（不判失败，只提示）
const warnings = [];
for (const spec of EXPECTED) {
  const p = join(BANK_DIR, spec.file);
  if (!present.has(spec.file)) continue;
  let data; try { data = JSON.parse(readFileSync(p, 'utf8')); } catch { continue; }
  if (!Array.isArray(data.items)) continue;
  let run = 1;
  for (let i = 1; i < data.items.length; i++) {
    if (data.items[i].answer === data.items[i - 1].answer) {
      run++;
      if (run >= 4) { warnings.push(`${spec.file}: items[${i - 3}..${i}] 连续 4 题同答案索引`); break; }
    } else run = 1;
  }
  const a = data.items.filter(x => x.answer === 0).length;
  if (data.items.length && a / data.items.length > 0.4) {
    warnings.push(`${spec.file}: answer=0 占比 ${a}/${data.items.length}，分布偏斜`);
  }
}

if (warnings.length) {
  console.log('\n警告（不计失败）：');
  for (const w of warnings.slice(0, 20)) console.log(`  · ${w}`);
}

if (errors.length) {
  console.log('\n错误明细：');
  for (const e of errors.slice(0, 80)) {
    console.log(`  ✗ ${e.file} :: ${e.id} :: ${e.msg}`);
  }
  if (errors.length > 80) console.log(`  … 其余 ${errors.length - 80} 条略`);
  console.log('\n结论：FAIL');
  process.exit(1);
}

console.log('\n结论：PASS');
process.exit(0);
