import fs from 'fs';

const htmlPath = '/Users/yunyinglaohe/StudyGate/demo/app.html';
let content = fs.readFileSync(htmlPath, 'utf8');

// 1. 注入 CSS 样式
const cssTarget = `.btn-run-code:hover {
  transform: translateY(-1px); box-shadow: 0 4px 16px rgba(16, 185, 129, 0.6);
}`;

const cssReplacement = `.btn-run-code:hover {
  transform: translateY(-1px); box-shadow: 0 4px 16px rgba(16, 185, 129, 0.6);
}
.btn-auto-fill {
  background: linear-gradient(135deg, #f59e0b, #d97706) !important;
  border: 1px solid rgba(251, 191, 36, 0.5) !important;
  color: #0f172a !important; font-weight: 800 !important;
  box-shadow: 0 4px 14px rgba(245, 158, 11, 0.35) !important;
  transition: all .2s cubic-bezier(0.34, 1.56, 0.64, 1) !important;
}
.btn-auto-fill:hover {
  background: linear-gradient(135deg, #fbbf24, #f59e0b) !important;
  transform: translateY(-2px) scale(1.03) !important;
  box-shadow: 0 6px 20px rgba(245, 158, 11, 0.5) !important;
  color: #000 !important;
}
.level-blocks-container {
  display: flex; flex-wrap: wrap; gap: 8px; align-items: center;
}
.block-chip-custom {
  background: linear-gradient(135deg, rgba(30, 41, 59, 0.85), rgba(15, 23, 42, 0.95));
  border: 1px solid rgba(56, 189, 248, 0.35);
  color: #38bdf8; font-size: 12px; font-family: 'JetBrains Mono', Consolas, Monaco, monospace;
  font-weight: 700; padding: 6px 12px; border-radius: 8px;
  cursor: pointer; transition: all .16s ease;
  display: inline-flex; align-items: center; gap: 6px;
  box-shadow: 0 2px 6px rgba(0,0,0,0.3);
}
.block-chip-custom:hover {
  background: rgba(56, 189, 248, 0.25);
  border-color: #38bdf8; color: #fff;
  transform: translateY(-2px) scale(1.04);
  box-shadow: 0 4px 14px rgba(56, 189, 248, 0.4);
}
.block-chip-custom:active {
  transform: translateY(1px) scale(0.97);
}`;

if (!content.includes(".btn-auto-fill")) {
  content = content.replace(cssTarget, cssReplacement);
  console.log("✓ CSS updated with btn-auto-fill & level-blocks styles");
}

// 2. 更新 HTML 中的积木行与操作栏
const blockRowTarget = `<div class="ribbon-row block-row">
              <span class="ribbon-label">🧩 积木贴片:</span>
              <button class="block-chip" data-code='print("")'>🖨️ print("")</button>
              <button class="block-chip" data-code='input("")'>📥 input("")</button>
              <button class="block-chip" data-code='for i in range(3):\\n    '>🔁 for 循环</button>
              <button class="block-chip" data-code='if :\\n    '>❓ if 条件</button>
              <button class="block-chip" data-code='else:\\n    '>🔀 else</button>
              <button class="block-chip" data-code='def func():\\n    '>📦 def 函数</button>
              <button class="block-chip" data-code='import bot\\nbot.wave()'>👋 bot.wave()</button>
              <button class="block-chip" data-code='bot.dance()'>💃 bot.dance()</button>
              <button class="block-chip" data-code='bot.spin()'>🔄 bot.spin()</button>
              <button class="block-chip" data-code='bot.beep()'>🔊 bot.beep()</button>
              <button class="block-chip" data-code='bot.say("你好少年！")'>💬 bot.say()</button>
            </div>`;

const blockRowReplacement = `<div class="ribbon-row block-row" id="quickBlockRow">
              <span class="ribbon-label">🧩 本关速拼积木 (点击即填入):</span>
              <div class="level-blocks-container" id="levelQuickBlocks"></div>
            </div>`;

if (content.includes(blockRowTarget)) {
  content = content.replace(blockRowTarget, blockRowReplacement);
  console.log("✓ HTML updated with dynamic levelQuickBlocks row");
}

// 3. 在操作栏中加入 🪄 召唤小博一键拼装 按钮
const toolbarTarget = `<button class="btn-run-code btn-judge" id="btnJudgePython">🚀 运行并提交测试 (Test)</button>
              <button class="btn-run-code btn-run-only" id="btnRunPythonOnly">▶ 仅运行代码</button>`;

const toolbarReplacement = `<button class="btn-run-code btn-judge" id="btnJudgePython">🚀 运行并提交测试 (Test)</button>
              <button class="btn-run-code btn-auto-fill" id="btnAutoFillCode" title="不想敲键盘？点此一键帮拼装整套积木代码！">🪄 召唤小博一键拼装</button>
              <button class="btn-run-code btn-run-only" id="btnRunPythonOnly">▶ 仅运行代码</button>`;

if (content.includes(toolbarTarget)) {
  content = content.replace(toolbarTarget, toolbarReplacement);
  console.log("✓ Toolbar updated with btnAutoFillCode button");
}

// 4. JS 中在 selectLesson 动态渲染积木并绑定一键拼装
const jsSelectTarget = `    // 填充【编程任务台】
    if (taskPromptEl) {
      taskPromptEl.innerHTML = lesson.taskPrompt || lesson.goal;
    }`;

const jsSelectReplacement = `    // 填充【编程任务台】
    if (taskPromptEl) {
      taskPromptEl.innerHTML = lesson.taskPrompt || lesson.goal;
    }

    // 动态渲染当前关卡专属速拼积木 (点击直接插入代码)
    const blockBox = document.getElementById('levelQuickBlocks');
    if (blockBox) {
      blockBox.innerHTML = '';
      const bList = (lesson.blocks && lesson.blocks.length) ? lesson.blocks : [lesson.solution];
      bList.forEach(codeStr => {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'block-chip-custom';
        btn.innerHTML = '🧩 <code>' + escapeHtml(codeStr) + '</code>';
        btn.title = '点击直接填入这块积木！';
        btn.onclick = () => {
          if (!editor) return;
          const s = editor.selectionStart || 0;
          const end = editor.selectionEnd || 0;
          const val = editor.value;
          const prefix = val.substring(0, s);
          const suffix = val.substring(end);
          const newLine = (prefix.length && !prefix.endsWith('\\n') && !prefix.endsWith(' ')) ? '\\n' : '';
          editor.value = prefix + newLine + codeStr + suffix;
          editor.selectionStart = editor.selectionEnd = (prefix + newLine + codeStr).length;
          editor.focus();
          playSound('click');
          if (typeof BotAudioEngine !== 'undefined') BotAudioEngine.beep('happy');
        };
        blockBox.appendChild(btn);
      });
    }`;

if (content.includes(jsSelectTarget)) {
  content = content.replace(jsSelectTarget, jsSelectReplacement);
  console.log("✓ selectLesson updated to dynamically render quick blocks");
}

// 5. 绑定 btnAutoFillCode 事件
const jsBtnTarget = `  if (btnReset) {
    btnReset.onclick = () => {
      const lesson = lessons.find(l => l.id === PyLabState.currentLessonId);
      if (editor && lesson) {
        editor.value = lesson.starter || '# 编写你的 Python 代码\\n';
        PyConsole.log('[RESET] 代码已重置为初始脚手架', 'step');
        playSound('click');
      }
    };
  }`;

const jsBtnReplacement = `  if (btnReset) {
    btnReset.onclick = () => {
      const lesson = lessons.find(l => l.id === PyLabState.currentLessonId);
      if (editor && lesson) {
        editor.value = lesson.starter || '# 编写你的 Python 代码\\n';
        PyConsole.log('[RESET] 代码已重置为初始脚手架', 'step');
        playSound('click');
      }
    };
  }

  // 🪄 召唤小博一键拼装魔法按钮
  const btnAutoFill = document.getElementById('btnAutoFillCode');
  if (btnAutoFill) {
    btnAutoFill.onclick = () => {
      const lesson = lessons.find(l => l.id === PyLabState.currentLessonId);
      if (editor && lesson) {
        editor.value = lesson.solution;
        if (typeof BotAudioEngine !== 'undefined') BotAudioEngine.beep('powerup');
        playSound('combo');
        saySomething('🪄 小博已经为你一键拼装好啦！仔细看每行代码的意思，点击左边【🚀 运行并提交测试】启动机甲吧！', '🪄 魔法一键拼装', 'celebrate');
        PyConsole.log(\`[AUTO-FILL] 🪄 已为你一键拼装关卡 L\${lesson.id} 标准机甲代码！\`, 'ok');
        if (btnJudge) {
          btnJudge.style.transform = 'scale(1.08)';
          btnJudge.style.boxShadow = '0 0 20px #10b981';
          setTimeout(() => {
            btnJudge.style.transform = '';
            btnJudge.style.boxShadow = '';
          }, 1200);
        }
      }
    };
  }`;

if (content.includes(jsBtnTarget)) {
  content = content.replace(jsBtnTarget, jsBtnReplacement);
  console.log("✓ btnAutoFillCode event bound successfully");
}

fs.writeFileSync(htmlPath, content, 'utf8');
console.log("🎉 All Python Lab UI upgrades applied successfully!");
