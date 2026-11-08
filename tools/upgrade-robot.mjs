import fs from 'fs';
import path from 'path';

const htmlPath = '/Users/yunyinglaohe/StudyGate/demo/app.html';
let content = fs.readFileSync(htmlPath, 'utf8');

// 1. 升级手臂动作：增加 lecture 讲课模式
const armTarget = "  if (isByte) {\n    if (mood === 'wave') {";
const armReplacement = `  if (isByte) {
    if (mood === 'lecture') {
      // 👨‍🏫 名师讲课姿态：右手握持全息激光教棒，富有节拍地在题板前点划重点；左手微托胸侧打节拍
      s += '<g class="m-hands-lecture">' +
           // 左手授课节拍手
           '<g class="m-teacher-l">' +
           '<path d="M-23 32 Q-38 38 -26 18" fill="none" stroke="url(#mShellGrad)" stroke-width="6.5" stroke-linecap="round"/>' +
           '<circle cx="-26" cy="18" r="5.5" fill="#ffffff" stroke="rgba(255,255,255,0.8)" stroke-width="1.2"/>' +
           '<circle cx="-26" cy="18" r="2.2" fill="' + c + '" filter="url(#mGlow)"/>' +
           '</g>' +
           // 右手全息激光教棒
           '<g class="m-pointer-arm">' +
           '<path d="M23 32 Q38 28 42 6" fill="none" stroke="url(#mShellGrad)" stroke-width="6.5" stroke-linecap="round"/>' +
           '<circle cx="42" cy="6" r="5.5" fill="#ffffff" stroke="rgba(255,255,255,0.8)" stroke-width="1.2"/>' +
           // 全息教棒手柄与棒身
           '<line x1="42" y1="6" x2="68" y2="-28" stroke="#334155" stroke-width="3" stroke-linecap="round"/>' +
           '<line x1="48" y1="-2" x2="68" y2="-28" stroke="' + c + '" stroke-width="2" stroke-linecap="round" filter="url(#mGlow)"/>' +
           // 教棒顶端高能聚焦全息光斑与光芒
           '<circle cx="68" cy="-28" r="4.5" fill="#ffffff" filter="url(#mStrongGlow)"/>' +
           '<circle cx="68" cy="-28" r="2.5" fill="' + c + '"/>' +
           '<circle cx="68" cy="-28" r="8" fill="none" stroke="' + c + '" stroke-width="1" class="m-pointer-sparkle" opacity="0.85"/>' +
           '</g>' +
           '</g>';
    } else if (mood === 'wave') {`;

if (!content.includes("mood === 'lecture'")) {
  if (!content.includes(armTarget)) {
    throw new Error("Could not find armTarget in app.html");
  }
  content = content.replace(armTarget, armReplacement);
  console.log("✓ Arm branch updated for 'lecture' mode");
}

// 2. 升级头部姿态：增加 m-lecture-head 与 m-think-head
const headTarget = `s += '<g class="mascot-head ' + (mood === 'scan' ? 'm-scan-head' : '') + '">';`;
const headReplacement = `s += '<g class="mascot-head ' + (mood === 'scan' ? 'm-scan-head' : (mood === 'lecture' ? 'm-lecture-head' : (mood === 'thinking' ? 'm-think-head' : ''))) + '">';`;
if (content.includes(headTarget)) {
  content = content.replace(headTarget, headReplacement);
  console.log("✓ Head posture updated for lecture and thinking");
}

// 3. 升级眼睛与表情系统：增加 lecture 眼神，优化 thinking 眨眼
const eyeTarget = `    // OLED 眼睛与表情系统\n    s += '<g id="robotEyeGroup" class="robot-eye-group">';\n    if (mood === 'celebrate' || mood === 'dance' || mood === 'wave') {`;
const eyeReplacement = `    // OLED 眼睛与表情系统
    s += '<g id="robotEyeGroup" class="robot-eye-group">';
    if (mood === 'lecture') {
      // 👨‍🏫 名师讲课大萌眼：水灵大萌眼 + 专注眼神 + 灵动眨眼微动
      s += '<g class="m-eyes-lecture">' +
           '<g class="m-eye-left">' +
           '<ellipse cx="-15" cy="-13" rx="8.5" ry="11.5" fill="url(#mEyeGrad)" filter="url(#mGlow)"/>' +
           '<circle cx="-17.5" cy="-16.5" r="3.2" fill="#ffffff"/>' +
           '<circle cx="-12" cy="-9" r="1.5" fill="#ffffff" opacity="0.9"/>' +
           '<path d="M-22 -7 Q-15 -3 -8 -7" fill="none" stroke="rgba(255,255,255,0.5)" stroke-width="1.2"/>' +
           '<circle cx="-15" cy="-13" r="6" fill="none" stroke="' + c + '" stroke-width="1" opacity="0.7"/>' +
           '</g>' +
           '<g class="m-eye-right">' +
           '<ellipse cx="15" cy="-13" rx="8.5" ry="11.5" fill="url(#mEyeGrad)" filter="url(#mGlow)"/>' +
           '<circle cx="12.5" cy="-16.5" r="3.2" fill="#ffffff"/>' +
           '<circle cx="18" cy="-9" r="1.5" fill="#ffffff" opacity="0.9"/>' +
           '<path d="M8 -7 Q15 -3 22 -7" fill="none" stroke="rgba(255,255,255,0.5)" stroke-width="1.2"/>' +
           '<circle cx="15" cy="-13" r="6" fill="none" stroke="' + c + '" stroke-width="1" opacity="0.7"/>' +
           '</g>' +
           '<ellipse cx="-21" cy="-5" rx="5.5" ry="2.8" fill="url(#mBlush)" opacity="0.6"/>' +
           '<ellipse cx="21" cy="-5" rx="5.5" ry="2.8" fill="url(#mBlush)" opacity="0.6"/>' +
           '</g>';
    } else if (mood === 'celebrate' || mood === 'dance' || mood === 'wave') {`;

if (content.includes(eyeTarget)) {
  content = content.replace(eyeTarget, eyeReplacement);
  console.log("✓ Eye system updated for lecture mode");
}

// 4. 升级嘴巴系统：在眼睛关闭后添加全息声波嘴
const mouthTarget = `           '<path d="M-3 -4 Q0 -2 3 -4" fill="none" stroke="' + c + '" stroke-width="1.3" stroke-linecap="round" opacity="0.75"/>' +
           '</g>';
    }
    s += '</g>';
  } else if (isCat) {`;

const mouthReplacement = `           '<path d="M-3 -4 Q0 -2 3 -4" fill="none" stroke="' + c + '" stroke-width="1.3" stroke-linecap="round" opacity="0.75"/>' +
           '</g>';
    }
    s += '</g>';

    // 全息动态声波嘴形系统 (Speaking Equalizer Waves / Idle Smile)
    s += '<g class="m-mouth-system">' +
         '<path class="m-mouth-smile" d="M-5 -2 Q0 2 5 -2" fill="none" stroke="' + c + '" stroke-width="1.8" stroke-linecap="round" opacity="0.85" filter="url(#mGlow)"/>' +
         '<g class="m-mouth-waves">' +
         '<rect class="m-vbar vbar-1" x="-8" y="-7" width="2" height="8" rx="1" fill="' + c + '" filter="url(#mGlow)"/>' +
         '<rect class="m-vbar vbar-2" x="-4" y="-10" width="2" height="14" rx="1" fill="' + c + '" filter="url(#mGlow)"/>' +
         '<rect class="m-vbar vbar-3" x="0" y="-12" width="2" height="18" rx="1" fill="#ffffff" filter="url(#mStrongGlow)"/>' +
         '<rect class="m-vbar vbar-4" x="4" y="-10" width="2" height="14" rx="1" fill="' + c + '" filter="url(#mGlow)"/>' +
         '<rect class="m-vbar vbar-5" x="8" y="-7" width="2" height="8" rx="1" fill="' + c + '" filter="url(#mGlow)"/>' +
         '</g>' +
         '</g>';
  } else if (isCat) {`;

if (content.includes(mouthTarget)) {
  content = content.replace(mouthTarget, mouthReplacement);
  console.log("✓ Holographic mouth system added");
}

// 5. 升级 updateMascotView
const updateViewTarget = `    if (mood === 'celebrate') animClass = 'm-anim-cheer';
    else if (mood === 'dance') animClass = 'm-anim-dance';
    else if (mood === 'spin') animClass = 'm-anim-spin';
    else if (mood === 'sleep') animClass = 'm-anim-sleep';
    else if (mood === 'charge') animClass = 'm-anim-charge';
    container.className = 'mascot-svg ' + animClass;`;

const updateViewReplacement = `    if (mood === 'celebrate') animClass = 'm-anim-cheer';
    else if (mood === 'dance') animClass = 'm-anim-dance';
    else if (mood === 'spin') animClass = 'm-anim-spin';
    else if (mood === 'sleep') animClass = 'm-anim-sleep';
    else if (mood === 'charge') animClass = 'm-anim-charge';
    else if (mood === 'lecture') animClass = 'm-anim-lecture';
    container.className = 'mascot-svg ' + animClass + (container.classList.contains('is-speaking') ? ' is-speaking' : '');`;

if (content.includes(updateViewTarget)) {
  content = content.replace(updateViewTarget, updateViewReplacement);
  console.log("✓ updateMascotView updated for lecture mode and speaking state");
}

// 6. 升级 triggerEmergencyLecture
const lecMascotTarget = `  if (mascotBox) {
    const origGl = MASCOT_CONFIG.glasses;
    MASCOT_CONFIG.glasses = 'tech';
    mascotBox.innerHTML = renderMascotSvg('thinking');
    MASCOT_CONFIG.glasses = origGl;
  }`;

const lecMascotReplacement = `  if (mascotBox) {
    const origGl = MASCOT_CONFIG.glasses;
    MASCOT_CONFIG.glasses = 'tech';
    mascotBox.innerHTML = renderMascotSvg('lecture');
    mascotBox.className = 'lecture-mascot-box mascot-svg m-anim-lecture is-speaking';
    MASCOT_CONFIG.glasses = origGl;

    // 点击机器人互动
    mascotBox.onclick = () => {
      if (typeof BotAudioEngine !== 'undefined') BotAudioEngine.beep('happy');
      if (bubble) {
        const encouragements = [
          "💪 少年别发愁！学霸都是踩坑踩出来的，听小博拆解，下道题必拿捏！",
          "🌟 稳住心态！这道题是经典出题陷阱，识破套路你就是大赢家！",
          "🚀 集中注意力！看右边学霸破局秘籍，提炼出的口诀背下来就能秒题！"
        ];
        bubble.innerHTML = encouragements[Math.floor(Math.random() * encouragements.length)];
      }
    };
  }`;

if (content.includes(lecMascotTarget)) {
  content = content.replace(lecMascotTarget, lecMascotReplacement);
  console.log("✓ triggerEmergencyLecture mascot initialized to active lecture mode");
}

// 7. 升级讲课语音事件，联动 is-speaking
const lecSpeechTarget = `  // 自动口述讲课语音
  if (window.speechSynthesis) {
    window.speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(content.speech);
    u.lang = 'zh-CN';
    u.rate = 1.0;
    u.pitch = 1.05;
    window.speechSynthesis.speak(u);

    if (btnSpeak) {
      btnSpeak.onclick = () => {
        if (window.speechSynthesis.speaking) {
          window.speechSynthesis.cancel();
          btnSpeak.textContent = '▶️ 播放讲课';
        } else {
          window.speechSynthesis.speak(u);
          btnSpeak.textContent = '⏹ 暂停讲课';
        }
      };
    }
  }`;

const lecSpeechReplacement = `  // 自动口述讲课语音
  if (window.speechSynthesis) {
    window.speechSynthesis.cancel();
    const u = new SpeechSynthesisUtterance(content.speech);
    u.lang = 'zh-CN';
    u.rate = 1.0;
    u.pitch = 1.05;

    u.onstart = () => {
      if (mascotBox) mascotBox.classList.add('is-speaking');
      if (btnSpeak) btnSpeak.textContent = '⏹ 暂停讲课';
    };

    u.onend = () => {
      if (mascotBox) mascotBox.classList.remove('is-speaking');
      if (btnSpeak) btnSpeak.textContent = '▶️ 重听讲课';
      if (bubble) bubble.innerHTML = '✨ <strong>核心考点讲解完毕！</strong><br>这道题的避坑心法你掌握了吗？准备好就点击下方继续战斗！';
    };

    u.onerror = () => {
      if (mascotBox) mascotBox.classList.remove('is-speaking');
      if (btnSpeak) btnSpeak.textContent = '▶️ 播放讲课';
    };

    window.speechSynthesis.speak(u);

    if (btnSpeak) {
      btnSpeak.onclick = () => {
        if (window.speechSynthesis.speaking) {
          window.speechSynthesis.cancel();
          if (mascotBox) mascotBox.classList.remove('is-speaking');
          btnSpeak.textContent = '▶️ 播放讲课';
        } else {
          if (mascotBox) mascotBox.classList.add('is-speaking');
          window.speechSynthesis.speak(u);
          btnSpeak.textContent = '⏹ 暂停讲课';
        }
      };
    }
  }`;

if (content.includes(lecSpeechTarget)) {
  content = content.replace(lecSpeechTarget, lecSpeechReplacement);
  console.log("✓ lecture speech synced with is-speaking state");
}

// 8. 升级 BotTeacherVoice 说话与讲课手势
const botVoiceTarget = `    if (window.speechSynthesis && soundEnabled) {
      try {
        window.speechSynthesis.cancel();
        const u = new SpeechSynthesisUtterance(plainText);
        u.lang = 'zh-CN';
        u.rate = 1.02;  // 自然流利的语速
        u.pitch = 1.08; // 充满亲切感与关切的少年导师语调

        let hasFinished = false;
        const finishOnce = () => {
          if (hasFinished) return;
          hasFinished = true;
          this.activeUtterance = null;
          clearTimeout(this.timer);
          if (onEnd) onEnd();
        };

        u.onend = finishOnce;
        u.onerror = finishOnce;`;

const botVoiceReplacement = `    if (window.speechSynthesis && soundEnabled) {
      try {
        window.speechSynthesis.cancel();
        const u = new SpeechSynthesisUtterance(plainText);
        u.lang = 'zh-CN';
        u.rate = 1.02;  // 自然流利的语速
        u.pitch = 1.08; // 充满亲切感与关切的少年导师语调

        const mascotEl = document.getElementById('mascotSvgContainer');
        if (mascotEl) mascotEl.classList.add('is-speaking');

        let hasFinished = false;
        const finishOnce = () => {
          if (hasFinished) return;
          hasFinished = true;
          const mEl = document.getElementById('mascotSvgContainer');
          if (mEl) mEl.classList.remove('is-speaking');
          this.activeUtterance = null;
          clearTimeout(this.timer);
          if (onEnd) onEnd();
        };

        u.onend = finishOnce;
        u.onerror = finishOnce;`;

if (content.includes(botVoiceTarget)) {
  content = content.replace(botVoiceTarget, botVoiceReplacement);
  console.log("✓ BotTeacherVoice synced with is-speaking state");
}

// 9. 升级 BotTeacherVoice.stop
const botStopTarget = `  stop() {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    if (window.speechSynthesis) {
      try { window.speechSynthesis.cancel(); } catch (e) {}
    }
    this.activeUtterance = null;
  },`;

const botStopReplacement = `  stop() {
    if (this.timer) { clearTimeout(this.timer); this.timer = null; }
    if (window.speechSynthesis) {
      try { window.speechSynthesis.cancel(); } catch (e) {}
    }
    this.activeUtterance = null;
    const mEl = document.getElementById('mascotSvgContainer');
    if (mEl) mEl.classList.remove('is-speaking');
  },`;

if (content.includes(botStopTarget)) {
  content = content.replace(botStopTarget, botStopReplacement);
  console.log("✓ BotTeacherVoice.stop cleans is-speaking state");
}

// 10. 第二幕主界面讲课时切换为 lecture 激光教棒模式
content = content.replace(
  "updateMascotView('wave'); // 切换为专注授课讲师姿态",
  "updateMascotView('lecture'); // 切换为手持激光教棒、专注授课讲师姿态"
);

// 11. 升级 EnglishTTS 纯正外教真人发音引擎
const ttsUrlTarget = `  // 获取纯正母语外教原声音频 URL (标准外教录音棚高清发音)
  getNativeAudioUrl(text, accent = 'us') {
    const isUk = (accent === 'uk');
    const encoded = encodeURIComponent(text);
    // 百度母语外教原声引擎：支持句子与短语，自然连读与爆破音，纯正美音/英音
    const lan = isUk ? 'uk' : 'en';
    return \`https://fanyi.baidu.com/gettts?lan=\${lan}&text=\${encoded}&spd=3&source=web\`;
  },`;

const ttsUrlReplacement = `  // 获取纯正母语外教原声音频 URL (权威外教录音棚高保真 MP3 发音)
  getNativeAudioUrl(text, accent = 'us') {
    const isUk = (accent === 'uk');
    const encoded = encodeURIComponent(text);
    // 权威中高考外教原声音频源：自然连读、弱读与升降调，极度清晰纯正
    const type = isUk ? '1' : '2';
    return \`https://dict.youdao.com/dictvoice?audio=\${encoded}&type=\${type}\`;
  },`;

if (content.includes(ttsUrlTarget)) {
  content = content.replace(ttsUrlTarget, ttsUrlReplacement);
  console.log("✓ EnglishTTS audio CDN upgraded to authentic native speaker MP3");
}

// 12. 升级 cleanText：将 ____ 替换为停顿逗号，剔除干扰标点与中文
const ttsCleanTarget = `  cleanText(text) {
    if (!text) return '';
    return text.replace(/_{2,}/g, ' blank ')
               .replace(/\\[\\.\\.\\.\\]/g, ' ')
               .replace(/[—–]/g, ' ')
               .replace(/<[^>]+>/g, '')
               .replace(/\\s+/g, ' ')
               .trim();
  },`;

const ttsCleanReplacement = `  cleanText(text) {
    if (!text) return '';
    return text.replace(/_{2,}/g, ', ')
               .replace(/\\[\\.\\.\\.\\]/g, ' ')
               .replace(/[—–]/g, ' ')
               .replace(/[\\u4e00-\\u9fa5]/g, ' ') // 彻底剔除题干中的中文干扰
               .replace(/<[^>]+>/g, '')
               .replace(/\\s+/g, ' ')
               .replace(/^[A-Za-z0-9]+[\\.\\、\\s]+/g, '') // 剔除题号
               .trim();
  },`;

if (content.includes(ttsCleanTarget)) {
  content = content.replace(ttsCleanTarget, ttsCleanReplacement);
  console.log("✓ EnglishTTS cleanText upgraded to natural cadence");
}

// 13. 升级 speakFallbackSpeech：严格限定纯英文发音人，禁止中文发音人念英文
const fallbackTarget = `  speakFallbackSpeech(text, onStart, onEnd) {
    if (!this.synth) return;
    this.synth.cancel();

    const u = new SpeechSynthesisUtterance(text);
    u.lang = this.accent === 'uk' ? 'en-GB' : 'en-US';
    if (this.bestVoice) u.voice = this.bestVoice;
    u.rate = 0.9;
    u.pitch = 1.04;`;

const fallbackReplacement = `  speakFallbackSpeech(text, onStart, onEnd) {
    if (!this.synth) return;
    this.synth.cancel();

    // 嗅探并锁定纯正英文母语发音人，绝对杜绝中文发音人念英语
    const voices = this.synth.getVoices();
    let engVoice = this.bestVoice;
    if (!engVoice && voices && voices.length) {
      engVoice = voices.find(v => v.lang.startsWith('en') && (
        v.name.includes('Samantha') || v.name.includes('Ava') || 
        v.name.includes('Daniel') || v.name.includes('Google US') || 
        v.name.includes('Natural') || v.name.includes('Karen')
      )) || voices.find(v => v.lang.startsWith('en')) || null;
    }

    const u = new SpeechSynthesisUtterance(text);
    u.lang = this.accent === 'uk' ? 'en-GB' : 'en-US';
    if (engVoice) u.voice = engVoice;
    u.rate = 0.92;
    u.pitch = 1.04;`;

if (content.includes(fallbackTarget)) {
  content = content.replace(fallbackTarget, fallbackReplacement);
  console.log("✓ EnglishTTS speakFallbackSpeech strictly locked to English native voices");
}

fs.writeFileSync(htmlPath, content, 'utf8');
console.log("🎉 All robot upgrades successfully applied to app.html!");
