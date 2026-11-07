#!/usr/bin/env node
/**
 * StudyGate 静态审计器
 *
 * 本机（macOS）没有 dotnet，无法编译。这个脚本做纯静态检查，能抓到的错它都抓：
 *   - 必需文件缺失
 *   - .csproj 的 ProjectReference 指向不存在的项目
 *   - XAML 的 x:Class 与 code-behind 的命名空间/类名对不上
 *   - XAML 里绑定的 Click/事件 在 code-behind 里没有对应方法
 *   - code-behind 引用的事件在 XAML 里不存在
 *   - StaticResource 引用了未定义的资源 key
 *   - P/Invoke 的 DllImport 存在但 struct 缺失
 *   - Core 里出现 Console/Debug 输出（SPEC 禁止）
 *   - 任何 csproj 引入第三方 PackageReference（SPEC 禁止）
 *   - 危险注册表项（Userinit / LogonUI / Screensaver）
 *   - 题库与 Core 的 schema 一致性
 *
 * 用法： node tools/audit.mjs [--verbose]
 * 退出码：0 = 通过，1 = 有错
 */

import { readFileSync, existsSync, readdirSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = join(dirname(fileURLToPath(import.meta.url)), '..');
const VERBOSE = process.argv.includes('--verbose');

let errors = 0;
let warns = 0;
const problems = [];

function err(where, msg, hint) {
  errors++;
  problems.push({ level: 'ERR ', where, msg, hint });
}
function warn(where, msg, hint) {
  warns++;
  problems.push({ level: 'WARN', where, msg, hint });
}
function pass(msg) { if (VERBOSE) console.log(`  [PASS] ${msg}`); }
function section(t) { console.log(`\n${t}`); }

function read(p) {
  try { return readFileSync(p, 'utf8'); } catch { return null; }
}
function rel(p) { return relative(ROOT, p) || '.'; }

/**
 * 去掉 C# 注释（// 行注释、块注释、XML 文档注释）。
 * 用于「禁止出现某词」这类扫描 —— 注释里解释"我们不动 X"不该被当成违规，
 * 否则代码里每写一句说明都得绕着词走，反而没人愿意解释。
 * 注意：不做字符串字面量剥离，字符串里的词仍然算违规（那才是真的会被执行的分支）。
 */
function stripCsComments(src) {
  let out = '';
  let i = 0;
  const n = src.length;
  while (i < n) {
    const c = src[i];
    if (c === '/' && src[i + 1] === '/') {
      while (i < n && src[i] !== '\n') i++;
    } else if (c === '/' && src[i + 1] === '*') {
      i += 2;
      while (i < n && !(src[i] === '*' && src[i + 1] === '/')) i++;
      i += 2;
    } else if (c === '"') {
      // 保留字符串原样（含转义），字符串里的内容参与扫描
      out += c;
      i++;
      while (i < n && src[i] !== '"') {
        if (src[i] === '\\') { out += src[i]; i++; }
        if (i < n) { out += src[i]; i++; }
      }
      if (i < n) { out += src[i]; i++; }
    } else {
      out += c;
      i++;
    }
  }
  return out;
}

function walk(dir, filter = () => true) {
  const out = [];
  if (!existsSync(dir)) return out;
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    const p = join(dir, e.name);
    if (e.isDirectory()) {
      if (['bin', 'obj', '.git', 'node_modules'].includes(e.name)) continue;
      out.push(...walk(p, filter));
    } else if (filter(p)) out.push(p);
  }
  return out;
}

// ============================================================ 1. 必需文件
section('1. 必需文件清单');

const REQUIRED = [
  'StudyGate.sln',
  'README.md',
  'docs/SPEC.md',
  'docs/CONTRACTS.md',
  'docs/RISKS.md',
  'docs/ESCAPE.md',

  'src/Core/StudyGate.Core.csproj',
  'src/Core/Types.cs',
  'src/Core/GateStateCodec.cs',
  'src/Core/QuestionBank.cs',
  'src/Core/Grader.cs',
  'src/Core/GateState.cs',
  'src/Core/FileGateStore.cs',
  'src/Core/Vault.cs',
  'src/Core/HiddenChannel.cs',
  'src/Core/Tests/StudyGate.CoreTests.csproj',
  'src/Core/Tests/CoreTests.cs',

  'src/App/StudyGate.App.csproj',
  'src/App/Program.cs',
  'src/App/WlxShell.cs',
  'src/App/InputLock.cs',
  'src/App/ProcessGuard.cs',
  'src/App/SelfDefense.cs',
  'src/App/TaskRegistrar.cs',
  'src/App/ShellExit.cs',
  'src/App/Escalation.cs',
  'src/App/Coordinator.cs',

  'src/Ui/StudyGate.Ui.csproj',
  'src/Ui/IGateViewHost.cs',
  'src/Ui/Theme.xaml',
  'src/Ui/GateWindow.xaml',
  'src/Ui/GateWindow.xaml.cs',
  'src/Ui/QuizView.xaml',
  'src/Ui/QuizView.xaml.cs',
  'src/Ui/ResultView.xaml',
  'src/Ui/ResultView.xaml.cs',

  'data/bank/validate.mjs',
  'tools/core-reference.mjs',
  'scripts/install.ps1',
  'scripts/uninstall.ps1',
  'scripts/verify.ps1',
  'scripts/build.ps1',
];

for (const f of REQUIRED) {
  const p = join(ROOT, f);
  if (!existsSync(p)) err(f, '文件缺失', '对照 docs/SPEC.md 目录归属补上');
  else pass(f);
}
console.log(`  ${REQUIRED.length} 项检查，缺失 ${problems.length} 项`);

// ============================================================ 2. csproj
section('2. 项目文件与引用');

const csprojs = walk(ROOT, p => p.endsWith('.csproj'));

for (const p of csprojs) {
  const txt = read(p) ?? '';
  const name = rel(p);

  // TargetFramework
  const tfm = txt.match(/<TargetFramework>([^<]+)<\/TargetFramework>/);
  if (!tfm) err(name, '缺 <TargetFramework>');
  else {
    const t = tfm[1].trim();
    const expectWindows = /src\/App|src\/Ui/.test(name);
    if (expectWindows && !t.includes('-windows')) err(name, `TargetFramework=${t}，App/Ui 必须是 net8.0-windows`);
    else if (!expectWindows && t.includes('-windows')) err(name, `TargetFramework=${t}，Core 必须跨平台`);
    else pass(`${name} -> ${t}`);
  }

  // 第三方包
  const pkgs = [...txt.matchAll(/<PackageReference\s+Include="([^"]+)"/g)].map(m => m[1]);
  if (pkgs.length) err(name, `引入第三方包：${pkgs.join(', ')}`, 'SPEC 要求零第三方 NuGet');
  else pass(`${name} 零第三方包`);

  // ProjectReference 路径存在
  const refs = [...txt.matchAll(/<ProjectReference\s+Include="([^"]+)"/g)].map(m => m[1]);
  for (const r of refs) {
    const target = join(dirname(p), r.replace(/\\/g, '/'));
    if (!existsSync(target)) err(name, `ProjectReference 指向不存在的项目：${r}`);
    else pass(`${name} -> ${r}`);
  }

  // WPF
  if (/src\/Ui/.test(name)) {
    if (!/<UseWPF>true<\/UseWPF>/.test(txt)) err(name, 'Ui 项目缺 <UseWPF>true</UseWPF>');
    if (!/<EnableDefaultItems>false<\/EnableDefaultItems>/.test(txt) &&
        !/<ApplicationDefinition/.test(txt)) {
      warn(name, '未显式声明 WPF 页面/资源的默认项策略，多 XAML 项目易报 duplicate 项');
    }
  }
  if (/src\/App/.test(name) && !/<UseWPF>true<\/UseWPF>/.test(txt)) {
    err(name, 'App 需要 UseWPF=true（它要创建 GateWindow）');
  }
}

// ============================================================ 3. XAML 对齐
section('3. XAML 与 code-behind 对齐');

const xamls = walk(join(ROOT, 'src/Ui'), p => p.endsWith('.xaml'));

// 收集 Theme.xaml 里定义的资源 key
const themePath = join(ROOT, 'src/Ui/Theme.xaml');
const themeTxt = read(themePath) ?? '';
const definedKeys = new Set(
  [...themeTxt.matchAll(/x:Key="([^"]+)"/g)].map(m => m[1])
);

for (const xp of xamls) {
  const name = rel(xp);
  const txt = read(xp) ?? '';
  const base = xp.slice(0, -'.xaml'.length);
  const cb = base + '.xaml.cs';

  const xClass = txt.match(/x:Class="([^"]+)"/);
  if (xClass) {
    const full = xClass[1];
    if (!existsSync(cb)) {
      err(name, `x:Class="${full}" 但找不到 ${rel(cb)}`);
      continue;
    }
    const cbt = read(cb) ?? '';
    const ns = full.slice(0, full.lastIndexOf('.'));
    const cls = full.slice(full.lastIndexOf('.') + 1);

    // namespace 声明
    if (!new RegExp(`namespace\\s+${ns.replace(/\./g, '\\.')}\\s*[;{]`).test(cbt)) {
      err(rel(cb), `namespace 不是 ${ns}`);
    }
    // 类声明
    if (!new RegExp(`(class|struct)\\s+${cls}\\b`).test(cbt)) {
      err(rel(cb), `找不到 class ${cls}`);
    }
    // InitializeComponent
    if (!/InitializeComponent\s*\(\s*\)/.test(cbt)) {
      err(rel(cb), '缺 InitializeComponent() 调用');
    }
    if (!/partial/.test(cbt)) {
      err(rel(cb), `class ${cls} 必须是 partial`);
    }

    // 事件处理器：XAML 里的 Click="X" / x:Name 与 code-behind
    const handlers = [...txt.matchAll(/(?:Click|Checked|Unchecked|SelectionChanged|Loaded|KeyDown|KeyUp|TextChanged|MouseLeftButtonUp)="([^"{]+)"/g)]
      .map(m => m[1].trim());
    for (const h of handlers) {
      if (!new RegExp(`(private|public|internal|protected)[^\\n]*\\b${h}\\s*\\(`).test(cbt)) {
        err(name, `事件处理器 ${h} 在 code-behind 中不存在`);
      }
    }
    if (handlers.length) pass(`${name} ${handlers.length} 个事件处理器均存在`);

    // x:Name 元素：code-behind 里引用到的字段应声明，反之不强制
    const xNames = [...txt.matchAll(/x:Name="([^"]+)"/g)].map(m => m[1]);
    pass(`${name} ${xNames.length} 个命名元素`);
  }

  // StaticResource 引用必须在 Theme.xaml 或本文件里定义
  const srs = [...txt.matchAll(/StaticResource\s+([A-Za-z0-9_.-]+)/g)].map(m => m[1]);
  for (const k of srs) {
    if (!definedKeys.has(k) && !new RegExp(`x:Key="${k}"`).test(txt)) {
      err(name, `StaticResource "${k}" 未定义（Theme.xaml 与本地都没有）`);
    }
  }
  if (srs.length) pass(`${name} ${srs.length} 处 StaticResource 均有定义`);

  // 禁止项：AI slop 视觉
  const slopPatterns = [
    [/LinearGradientBrush|RadialGradientBrush/i, '渐变画刷'],
    [/DropShadowEffect/i, '投影特效（发光）'],
    [/<Border[^>]*CornerRadius="(?!0|1|2|3|4|5|6|7|8")[0-9]+/, '圆角 > 8px'],
  ];
  for (const [re, label] of slopPatterns) {
    if (re.test(txt)) err(name, `视觉红线：出现${label}`, 'SPEC 第三节禁止 AI slop 视觉');
  }
}

// ============================================================ 4. P/Invoke
section('4. P/Invoke 与结构体');

const appFiles = walk(join(ROOT, 'src/App'), p => p.endsWith('.cs'));
const allCs = [...appFiles, ...walk(join(ROOT, 'src/Core'), p => p.endsWith('.cs'))];

for (const p of allCs) {
  const txt = read(p) ?? '';
  const name = rel(p);

  // 属性参数里出现 EntryPoint = "..." 这种带引号的写法很常见，
  // 用 [^)]* 收窄参数区，否则贪婪匹配会一路吃到文件里下一个 ") ]" 组合上。
  const imports = [...txt.matchAll(/\[DllImport\s*\(\s*"([^"]+)"[^\]]*\)\s*\]/g)];
  const structs = [...txt.matchAll(/\[StructLayout\s*\(\s*LayoutKind\.(Sequential|Explicit)/g)];

  if (imports.length) {
    pass(`${name} ${imports.length} 处 DllImport`);
    // 每个 DllImport 的函数声明应在紧随其后的几行内
    for (const m of imports) {
      const after = txt.slice(m.index + m[0].length, m.index + m[0].length + 400);
      if (!/\b(extern|static)\b[^\n]*\b([A-Za-z_][A-Za-z0-9_]*)\s*\(/.test(after)) {
        err(name, `DllImport "${m[1]}" 后未紧跟 extern/static 方法声明`);
      }
    }
  }

  // 钩子相关：必须有 KBDLLHOOKSTRUCT / MSLLHOOKSTRUCT
  if (/SetWindowsHookEx|WH_KEYBOARD_LL|WH_MOUSE_LL/.test(txt)) {
    for (const s of ['KBDLLHOOKSTRUCT', 'MSLLHOOKSTRUCT']) {
      if (!new RegExp(`${s}`).test(txt)) {
        err(name, `用了钩子但缺 ${s} 定义`, '64 位布局必须显式 StructLayout(LayoutKind.Sequential)');
      }
    }
    if (structs.length === 0) err(name, '钩子 struct 缺 StructLayout 标注');
    // 委托必须被字段持有，否则 GC 回收
    if (!/private\s+[\w.]+\s+[A-Za-z_]\w*\s*(;|=)/.test(txt)) {
      warn(name, '未发现持有钩子委托的字段，可能被 GC 回收导致钩子失效');
    }
  }
}

// ============================================================ 5. 禁止项
section('5. 硬约束扫描');

const coreFiles = walk(join(ROOT, 'src/Core'), p => p.endsWith('.cs'));

for (const p of coreFiles) {
  const txt = read(p) ?? '';
  const name = rel(p);
  if (/Console\.(Write|Read)|Debug\.(Write|Assert)|Trace\.Write/.test(txt)) {
    if (!/Tests/.test(name)) err(name, 'Core 里出现 Console/Debug 输出（SPEC 禁止）');
  }
}

// 危险注册表项（先剥注释：注释里写"我们不动 X"是解释，不是违规；字符串里出现才算真分支）
for (const p of allCs) {
  const raw = read(p) ?? '';
  const txt = stripCsComments(raw);
  const name = rel(p);
  for (const bad of ['Userinit', 'LogonUI', 'Screensaver', 'Winlogon\\\\Notify']) {
    if (new RegExp(bad, 'i').test(txt)) {
      err(name, `出现被禁止的注册表项 ${bad}`, 'SPEC 第二节明令禁止');
    }
  }
}

// Shell 写入必须备份
// 备份路径常写成 GatePaths.ShellBackupPath（真正的 "shell.bak" 字面量在 Program.cs 的
// GatePaths 定义里），所以在 WlxShell.cs 里只查 "shell.bak" 会永远误报。两个都认。
{
  const wl = read(join(ROOT, 'src/App/WlxShell.cs'));
  const gp = read(join(ROOT, 'src/App/Program.cs'));
  if (wl) {
    const hasBackupRef = /shell\.bak/i.test(wl) || /ShellBackupPath/.test(wl);
    const hasBackupConst = /shell\.bak/i.test(gp ?? '') || /ShellBackupPath/.test(gp ?? '');
    if (!hasBackupRef || !hasBackupConst) err('src/App/WlxShell.cs', '未备份 shell.bak（既无 ShellBackupPath 也无 shell.bak）');
    if (!/explorer\.exe/.test(wl)) err('src/App/WlxShell.cs', '回滚未落 explorer.exe 兜底');
    pass('WlxShell 有备份与兜底');
  }
}

// 输入锁必须回流传键
// InputLock 刻意不直接引用 HiddenChannel（分层：App 内部用 Action<char> 回调），
// 真正接到 HiddenChannel 的是 Coordinator.FeedHiddenChar。两条通路任一存在即合格。
{
  const il = read(join(ROOT, 'src/App/InputLock.cs')) ?? '';
  const co = read(join(ROOT, 'src/App/Coordinator.cs')) ?? '';
  const hasCallback = /Action<char>\s+_onChar/.test(stripCsComments(il)) && /_onChar\s*\(/.test(stripCsComments(il));
  const hasSink = /HiddenChannel/.test(stripCsComments(co)) || /HiddenChannel/.test(stripCsComments(il));
  if (!hasCallback) err('src/App/InputLock.cs', '未把原始字符经 Action<char> 回调出去（隐蔽口令唯一入口断了）');
  else if (!hasSink) err('src/App/Coordinator.cs', '回调链路末端没接到 HiddenChannel.Feed（口令通道无人消费）');
  else pass('InputLock -> Action<char> -> HiddenChannel 通路存在');
}

// ============================================================ 6. 题库契约
section('6. 题库契约');

const bankDir = join(ROOT, 'data/bank');
const EXPECT = ['g6下','g7上','g7下','g8上','g8下','g9上','g9下'];
const SUBJECTS = ['chinese','math','english'];
const missing = [];
const stats = [];

for (const term of EXPECT) {
  for (const sub of SUBJECTS) {
    const f = join(bankDir, `${term}-${sub}.json`);
    if (!existsSync(f)) { missing.push(`${term}-${sub}.json`); continue; }
    try {
      const d = JSON.parse(read(f));
      const n = d.items?.length ?? 0;
      if (n < 40) err(rel(f), `题数 ${n} < 40`);
      const dist = [0,0,0,0];
      let badIdx = 0;
      for (const it of d.items ?? []) {
        if (!Array.isArray(it.options) || it.options.length !== 4) badIdx++;
        if (!Number.isInteger(it.answer) || it.answer < 0 || it.answer > 3) badIdx++;
        else dist[it.answer]++;
      }
      if (badIdx) err(rel(f), `${badIdx} 处 schema 违规（options 非 4 项或 answer 越界）`);
      const bias = Math.max(...dist) / Math.max(1, n);
      if (bias > 0.40) warn(rel(f), `答案分布偏斜：${dist.join('/')}（最大占比 ${(bias*100).toFixed(1)}%）`);
      stats.push({ term, sub, n, dist });
    } catch (e) {
      err(rel(f), `JSON 解析失败：${e.message}`);
    }
  }
}
if (missing.length) err('data/bank', `缺 ${missing.length} 个题库文件：${missing.join(', ')}`);
else pass(`21 个题库文件齐全，合计 ${stats.reduce((a,b)=>a+b.n,0)} 题`);

// validate.mjs 退出码语义
{
  const vm = read(join(bankDir, 'validate.mjs'));
  if (vm) {
    if (!/process\.exit\(\s*1\s*\)/.test(vm)) err('data/bank/validate.mjs', '失败路径没有 exit(1)，校验器形同虚设');
    else pass('validate.mjs 失败会 exit(1)');
  }
}

// ============================================================ 7. Core 契约面
section('7. Core 公共签名');

const CORE_TYPES = [
  ['Question.cs',      ['record Question', 'enum Subject']],
  ['QuestionBank.cs',  ['class QuestionBank', 'LoadFromDirectory', 'Pick']],
  ['Grader.cs',        ['class Grader', 'Score']],
  ['GateState.cs',     ['enum GatePhase', 'class GateState']],
  ['FileGateStore.cs', ['interface IGateStore', 'class FileGateStore', 'Load', 'Save']],
  ['Vault.cs',         ['class Vault', 'Verify']],
  ['HiddenChannel.cs', ['class HiddenChannel', 'Feed']],
];
for (const [file, needles] of CORE_TYPES) {
  const p = join(ROOT, 'src/Core', file);
  if (!existsSync(p)) continue;
  const txt = read(p) ?? '';
  for (const n of needles) {
    if (!new RegExp(n.replace(/\s+/g, '\\s+')).test(txt)) {
      err(rel(p), `缺契约成员：${n}`);
    }
  }
}

// GateState 必须字段
{
  const gs = read(join(ROOT, 'src/Core/GateState.cs'));
  if (gs) {
    const fields = ['Phase','UnlockUntilUtc','UnlockBootUtc','Grade','Term','AttemptsToday',
                    'LastAttemptUtc','PasswordFailCount','PasswordLockedUntilUtc','PaperSeed',
                    'CurrentPaperIds','CurrentAnswers','CurrentIndex','History'];
    const miss = fields.filter(f => !new RegExp(`\\b${f}\\b`).test(gs));
    if (miss.length) err('src/Core/GateState.cs', `缺字段：${miss.join(', ')}（见 docs/CONTRACTS.md A 节）`);
    else pass('GateState 14 个契约字段齐全');
  }
}

// ============================================================ 汇总
section('汇总');
for (const p of problems) {
  const color = p.level === 'ERR ' ? '\x1b[31m' : '\x1b[33m';
  console.log(`  ${color}${p.level}\x1b[0m ${p.where}`);
  console.log(`        ${p.msg}`);
  if (p.hint) console.log(`        \x1b[90m${p.hint}\x1b[0m`);
}
if (!problems.length) console.log('  无问题。');

console.log('');
console.log(`  ${errors} 错 / ${warns} 警`);
console.log('');
if (errors === 0) {
  console.log('\x1b[32m静态审计通过。仍需在 Windows + .NET 8 上做真实编译与手工验收。\x1b[0m');
  process.exit(0);
} else {
  console.log('\x1b[31m静态审计未通过。\x1b[0m');
  process.exit(1);
}
