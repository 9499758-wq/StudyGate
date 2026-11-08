/* StudyGate 专为 13 岁初中少年打造的趣味机甲 Python 体系 (超低门槛、积木速拼、零敲键盘) */
window.PYTHON_39_COURSES = [
  {
    "id": 1,
    "level": 1,
    "tierName": "Lv.1 机甲学员 · 指令唤醒",
    "badge": "🌱 唤醒启蒙",
    "title": "唤醒小博：说出第一句启动咒语",
    "img": "assets/python/l01.jpg",
    "goal": "点一下下方积木或运行按钮，让小博在屏幕上面罩亮起，说出：你好，小博！",
    "story": [
      "🎮 欢迎来到极客编程世界！电脑就像一个<b>超级听话的机器人士兵</b>，它不会读心术，你给它什么指令，它就执行什么动作！",
      "在 Python 里，让小博把字打在屏幕上的魔法指令叫 <code>print</code>（英语原义就是显示、打印）。",
      "双引号里的文字就是小博要说的话！点一下下方绿色积木或者直接点运行，马上唤醒小博！"
    ],
    "vocab": [
      {
        "w": "print",
        "ph": "/prɪnt/",
        "kind": "输出指令",
        "zh": "显示；打印",
        "root": "就像小博的发声扬声器，把括号里的文字在屏幕上大声念出来！",
        "ex": [
          "print(\"你好\")",
          "屏幕显示你好"
        ]
      }
    ],
    "errors": [
      {
        "code": "print(\"你好)",
        "say": "双引号必须成双成对！前后都要有哦！",
        "fix": "print(\"你好\")"
      }
    ],
    "demo": "print(\"你好，小博！\")",
    "taskPrompt": "让程序在屏幕上显示一行：<br><code>你好，小博！</code><br><span style=\"color:#34d399\">💡 提示：不想打字？直接点击下方【🧩 本关速拼积木】或【🪄 召唤小博一键拼装】！</span>",
    "starter": "# 任务：让小博说出第一句启动问候（点击下方积木即可填入）\nprint(\"你好，小博！\")\n",
    "blocks": [
      "print(\"你好，小博！\")",
      "print(\"小博你好！\")",
      "print"
    ],
    "hints": [
      "第一关小博已经为你写好啦！直接点击右下角【🚀 运行并提交测试】就能通关！",
      "如果代码被不小心删了，点击上方积木块【print(\"你好，小博！\")】一键填回！"
    ],
    "must": [
      "print"
    ],
    "solution": "print(\"你好，小博！\")",
    "tests": [
      {
        "inputs": [],
        "out": "你好，小博！\n"
      }
    ]
  },
  {
    "id": 2,
    "level": 1,
    "tierName": "Lv.1 机甲学员 · 指令唤醒",
    "badge": "🌱 连续指令",
    "title": "机甲声光秀：连续两行指令",
    "img": "assets/python/l02.jpg",
    "goal": "让小博按顺序依次说出两句话：第一行“系统启动完毕”，第二行“电量100%！”",
    "story": [
      "电脑执行代码非常简单：<b>从上到下，一行一行顺序执行</b>！",
      "上一行没做完，绝不跳到下一行。你写两行 <code>print</code>，小博就会分两行整齐报数！"
    ],
    "vocab": [
      {
        "w": "line",
        "ph": "/laɪn/",
        "kind": "代码行",
        "zh": "一行代码",
        "root": "每一行代码就是一个独立的动作，回车键就能换到下一行！",
        "ex": [
          "print(1)\nprint(2)",
          "分两行执行"
        ]
      }
    ],
    "errors": [
      {
        "code": "print(\"A\") print(\"B\")",
        "say": "两条指令挤在同一行了！请分成上下两行！",
        "fix": "print(\"A\")\nprint(\"B\")"
      }
    ],
    "demo": "print(\"系统启动完毕\")\nprint(\"电量100%！\")",
    "taskPrompt": "让程序依次输出两行：<br><code>系统启动完毕</code><br><code>电量100%！</code>",
    "starter": "print(\"系统启动完毕\")\n# 👈 点击上方积木，填入第二行电量指令：\nprint(\"电量100%！\")\n",
    "blocks": [
      "print(\"电量100%！\")",
      "print(\"系统启动完毕\")",
      "print(\"雷达正常\")"
    ],
    "hints": [
      "第一行已经写好了，第二行只需要填入：print(\"电量100%！\")",
      "直接点击上方积木【print(\"电量100%！\")】即可！"
    ],
    "must": [
      "print"
    ],
    "solution": "print(\"系统启动完毕\")\nprint(\"电量100%！\")",
    "tests": [
      {
        "inputs": [],
        "out": "系统启动完毕\n电量100%！\n"
      }
    ]
  },
  {
    "id": 3,
    "level": 1,
    "tierName": "Lv.1 机甲学员 · 指令唤醒",
    "badge": "🌱 变量标签",
    "title": "建立机甲档案：给数据贴标签",
    "img": "assets/python/l03.jpg",
    "goal": "创建机甲名字和等级变量，并把它们打印出来！",
    "story": [
      "📦 <b>变量就像一个贴了名字的储物盒</b>！",
      "写 <code>name = \"雷霆机甲\"</code>，就是把“雷霆机甲”放进名叫 <code>name</code> 的盒子里。以后随时叫它的名字，就能取出里面的东西！"
    ],
    "vocab": [
      {
        "w": "variable",
        "ph": "/ˈveəriəbl/",
        "kind": "核心概念",
        "zh": "变量",
        "root": "可以改变的量，就像游戏里的昵称卡槽，随时可以换内容！",
        "ex": [
          "hero = \"孙悟空\"",
          "存入英雄名"
        ]
      }
    ],
    "errors": [
      {
        "code": "name == \"机甲\"",
        "say": "存数据用一个等号 =（赋值），不是两个等号 == 哦！",
        "fix": "name = \"机甲\""
      }
    ],
    "demo": "name = \"雷霆机甲\"\nlevel = 1\nprint(name)\nprint(level)",
    "taskPrompt": "分别打印出机甲名字 <code>雷霆机甲</code> 和等级 <code>1</code>",
    "starter": "name = \"雷霆机甲\"\nlevel = 1\n# 👈 点击上方积木，分别打印它们：\nprint(name)\nprint(level)\n",
    "blocks": [
      "print(name)",
      "print(level)",
      "name = \"雷霆机甲\"",
      "level = 1"
    ],
    "hints": [
      "打印变量名字时，不需要加双引号哦！直接写 print(name) 和 print(level)",
      "点击上方积木一键填入！"
    ],
    "must": [
      "name",
      "level",
      "print"
    ],
    "solution": "name = \"雷霆机甲\"\nlevel = 1\nprint(name)\nprint(level)",
    "tests": [
      {
        "inputs": [],
        "out": "雷霆机甲\n1\n"
      }
    ]
  },
  {
    "id": 4,
    "level": 1,
    "tierName": "Lv.1 机甲学员 · 指令唤醒",
    "badge": "🌱 战力升级",
    "title": "击败怪兽，战力升级！",
    "img": "assets/python/l04.jpg",
    "goal": "初始攻击力 50，击败小怪后攻击力增加 30，算出最新战力！",
    "story": [
      "在打怪升级时，战力会不断变化！",
      "写 <code>atk = atk + 30</code>，意思是：<b>把原来的 atk 拿出来加上 30，再重新存回 atk 盒子里</b>！",
      "这就是程序员最常用的“自增升级法”！"
    ],
    "vocab": [
      {
        "w": "assignment",
        "ph": "/əˈsaɪnmənt/",
        "kind": "操作",
        "zh": "赋值",
        "root": "把等号右边算出来的结果，装进等号左边的变量盒子里！",
        "ex": [
          "x = x + 1",
          "自己加1"
        ]
      }
    ],
    "errors": [
      {
        "code": "atk + 30",
        "say": "光算加法没存回变量里！必须写 atk = atk + 30 重新存入！",
        "fix": "atk = atk + 30"
      }
    ],
    "demo": "atk = 50\natk = atk + 30\nprint(atk)",
    "taskPrompt": "让程序计算并输出最终战力：<code>80</code>",
    "starter": "atk = 50\n# 👈 战力提升 30（点击上方积木填入）：\natk = atk + 30\nprint(atk)\n",
    "blocks": [
      "atk = atk + 30",
      "print(atk)",
      "atk = 50"
    ],
    "hints": [
      "点击上方积木【atk = atk + 30】，再点击【print(atk)】！"
    ],
    "must": [
      "atk",
      "print"
    ],
    "solution": "atk = 50\natk = atk + 30\nprint(atk)",
    "tests": [
      {
        "inputs": [],
        "out": "80\n"
      }
    ]
  },
  {
    "id": 5,
    "level": 1,
    "tierName": "Lv.1 机甲学员 · 指令唤醒",
    "badge": "🌱 四则运算",
    "title": "激光射击：怪物血量结算器",
    "img": "assets/python/l05.jpg",
    "goal": "怪物 100 血，被 25 伤激光打了 3 枪，用电脑自动算出剩余血量！",
    "story": [
      "电脑天生就是超级计算器！",
      "数学里的乘号 × 在键盘上用星号 <code>*</code> 表示！电脑同样遵循<b>先乘除、后加减</b>的规则！",
      "<code>100 - 3 * 25</code> 电脑会先算 3 * 25 = 75，再用 100 - 75 = 25！"
    ],
    "vocab": [
      {
        "w": "operator",
        "ph": "/ˈɒpəreɪtə/",
        "kind": "运算符",
        "zh": "运算符",
        "root": "+ 加、- 减、* 乘、/ 除，是指挥电脑计算的符号工具！",
        "ex": [
          "3 * 25",
          "计算3乘以25"
        ]
      }
    ],
    "errors": [
      {
        "code": "100 - 3 x 25",
        "say": "代码里的乘号是键盘上的星号 *（Shift+8），不是字母 x 哦！",
        "fix": "100 - 3 * 25"
      }
    ],
    "demo": "hp = 100 - 3 * 25\nprint(hp)",
    "taskPrompt": "计算并打印剩余血量：<code>25</code>",
    "starter": "# 怪物 100 血，挨了 3 发 25 伤害激光\nhp = 100 - 3 * 25\n# 👈 点击上方积木，打印剩余血量：\nprint(hp)\n",
    "blocks": [
      "print(hp)",
      "hp = 100 - 3 * 25",
      "3 * 25"
    ],
    "hints": [
      "点击上方积木【print(hp)】，然后运行即可！"
    ],
    "must": [
      "hp",
      "print"
    ],
    "solution": "hp = 100 - 3 * 25\nprint(hp)",
    "tests": [
      {
        "inputs": [],
        "out": "25\n"
      }
    ]
  },
  {
    "id": 6,
    "level": 2,
    "tierName": "Lv.2 进阶极客 · 逻辑大脑",
    "badge": "⚡ 智能决策",
    "title": "智能电量感知：if / else 分支",
    "img": "assets/python/l06.jpg",
    "goal": "当电量低于 20 时，打印“进入节能模式”；否则打印“能量充足，出击！”",
    "story": [
      "🤖 机器人最神奇的地方是有<b>判断力</b>！",
      "<code>if</code> 在英语里是“如果”，<code>else</code> 是“否则”。",
      "如果条件满足，就执行 if 后面的缩进代码；如果不满足，就执行 else 后面的代码！"
    ],
    "vocab": [
      {
        "w": "if",
        "ph": "/ɪf/",
        "kind": "关键字",
        "zh": "如果",
        "root": "条件判断大门，条件成立才能通过！",
        "ex": [
          "if power < 20:",
          "若电量低于20"
        ]
      },
      {
        "w": "else",
        "ph": "/els/",
        "kind": "关键字",
        "zh": "否则",
        "root": "如果 if 的条件不满足，就走这道备用大门！",
        "ex": [
          "else:",
          "否则执行此处"
        ]
      }
    ],
    "errors": [
      {
        "code": "if power < 20\nprint(1)",
        "say": "if 条件末尾必须带冒号 :，下一行必须按 Tab 缩进！",
        "fix": "if power < 20:\n    print(1)"
      }
    ],
    "demo": "power = 15\nif power < 20:\n    print(\"进入节能模式\")\nelse:\n    print(\"能量充足，出击！\")",
    "taskPrompt": "当电量为 15 时，程序输出：<br><code>进入节能模式</code>",
    "starter": "power = 15\nif power < 20:\n    # 👈 电量低，点击下方积木填入：\n    print(\"进入节能模式\")\nelse:\n    print(\"能量充足，出击！\")\n",
    "blocks": [
      "    print(\"进入节能模式\")",
      "    print(\"能量充足，出击！\")",
      "if power < 20:",
      "else:"
    ],
    "hints": [
      "缩进位置已经为你准备好啦，直接点击积木【print(\"进入节能模式\")】！"
    ],
    "must": [
      "if",
      "else",
      "print"
    ],
    "solution": "power = 15\nif power < 20:\n    print(\"进入节能模式\")\nelse:\n    print(\"能量充足，出击！\")",
    "tests": [
      {
        "inputs": [],
        "out": "进入节能模式\n"
      }
    ]
  },
  {
    "id": 7,
    "level": 2,
    "tierName": "Lv.2 进阶极客 · 逻辑大脑",
    "badge": "⚡ 双等比对",
    "title": "太空舱暗号：双等号比对",
    "img": "assets/python/l07.jpg",
    "goal": "输入暗号 code = '2026'，当暗号正确时打印“舱门开启！”",
    "story": [
      "⚠️ <b>注意 Python 里最大的新手坑！</b>",
      "一个等号 <code>=</code> 是<b>把东西放进盒子里</b>（赋值）。",
      "两个等号 <code>==</code> 才是<b>检查两边是不是完全一样</b>（比对判断）！就像人脸识别核验身份一样！"
    ],
    "vocab": [
      {
        "w": "equal",
        "ph": "/ˈiːkwəl/",
        "kind": "比较符",
        "zh": "等于",
        "root": "在编程里两个等号 == 专门用来比对双方是否完全相等！",
        "ex": [
          "code == \"2026\"",
          "检查暗号是否相等"
        ]
      }
    ],
    "errors": [
      {
        "code": "if code = \"2026\":",
        "say": "判断必须用双等号 == ，单等号 = 是存数据！",
        "fix": "if code == \"2026\":"
      }
    ],
    "demo": "code = \"2026\"\nif code == \"2026\":\n    print(\"舱门开启！\")",
    "taskPrompt": "当暗号比对成功时输出：<code>舱门开启！</code>",
    "starter": "code = \"2026\"\n# 👈 点击下方积木，用双等号 == 进行暗号核对：\nif code == \"2026\":\n    print(\"舱门开启！\")\n",
    "blocks": [
      "if code == \"2026\":",
      "    print(\"舱门开启！\")",
      "code == \"2026\""
    ],
    "hints": [
      "点击上方积木一键填入判断语句！"
    ],
    "must": [
      "==",
      "if",
      "print"
    ],
    "solution": "code = \"2026\"\nif code == \"2026\":\n    print(\"舱门开启！\")",
    "tests": [
      {
        "inputs": [],
        "out": "舱门开启！\n"
      }
    ]
  },
  {
    "id": 8,
    "level": 2,
    "tierName": "Lv.2 进阶极客 · 逻辑大脑",
    "badge": "⚡ 多路分流",
    "title": "段位评级芯片：多分支 elif",
    "img": "assets/python/l08.jpg",
    "goal": "得分 85 分，90以上是S级，80以上是A级，否则是B级！",
    "story": [
      "如果选择不止两个怎么办？",
      "Python 提供了 <code>elif</code>（就是 else if 的合体缩写）！",
      "它可以让你像过安检一样，一层一层筛查条件：第一道门没过，试第二道门！"
    ],
    "vocab": [
      {
        "w": "elif",
        "ph": "/ˌelˈɪf/",
        "kind": "关键字",
        "zh": "否则如果",
        "root": "else if 的合体，用来追加多重筛选条件！",
        "ex": [
          "elif score >= 80:",
          "否则若达到80分"
        ]
      }
    ],
    "errors": [
      {
        "code": "else if score >= 80:",
        "say": "Python 里不能写空格 else if，必须缩写成 elif！",
        "fix": "elif score >= 80:"
      }
    ],
    "demo": "score = 85\nif score >= 90:\n    print(\"S级战神\")\nelif score >= 80:\n    print(\"A级极客\")\nelse:\n    print(\"B级新星\")",
    "taskPrompt": "85 分时程序输出：<code>A级极客</code>",
    "starter": "score = 85\nif score >= 90:\n    print(\"S级战神\")\n# 👈 80分以上是A级极客（点击下方积木）：\nelif score >= 80:\n    print(\"A级极客\")\nelse:\n    print(\"B级新星\")\n",
    "blocks": [
      "elif score >= 80:",
      "    print(\"A级极客\")",
      "else:",
      "    print(\"B级新星\")"
    ],
    "hints": [
      "脚手架已经将 elif 排好啦，点击右下角运行或点击积木拼装！"
    ],
    "must": [
      "if",
      "elif",
      "else"
    ],
    "solution": "score = 85\nif score >= 90:\n    print(\"S级战神\")\nelif score >= 80:\n    print(\"A级极客\")\nelse:\n    print(\"B级新星\")",
    "tests": [
      {
        "inputs": [],
        "out": "A级极客\n"
      }
    ]
  },
  {
    "id": 9,
    "level": 2,
    "tierName": "Lv.2 进阶极客 · 逻辑大脑",
    "badge": "⚡ 循环充能",
    "title": "护盾三连充：for 循环魔法",
    "img": "assets/python/l09.jpg",
    "goal": "用 for 循环让小博自动连续打印 3 次“护盾充能完毕！”",
    "story": [
      "如果要重复做一件事 100 次，手敲 100 行会累哭！",
      "用 <code>for i in range(3):</code>，电脑就会不知疲倦地<b>自动重复跑 3 次</b>！",
      "这就是让电脑做苦力活的核心魔法——循环！"
    ],
    "vocab": [
      {
        "w": "for",
        "ph": "/fɔː/",
        "kind": "关键字",
        "zh": "循环",
        "root": "指挥电脑开启自动重复运转的大门！",
        "ex": [
          "for i in range(3):",
          "重复执行3次"
        ]
      },
      {
        "w": "range",
        "ph": "/reɪndʒ/",
        "kind": "范围函数",
        "zh": "范围/计数器",
        "root": "range(3) 相当于发了 3 张门票 [0, 1, 2]，一张一张检票进场！",
        "ex": [
          "range(3)",
          "产生3次机会"
        ]
      }
    ],
    "errors": [
      {
        "code": "for i in range(3)\nprint(1)",
        "say": "for 循环末尾不要漏掉冒号 : 并且内部必须缩进！",
        "fix": "for i in range(3):\n    print(1)"
      }
    ],
    "demo": "for i in range(3):\n    print(\"护盾充能完毕！\")",
    "taskPrompt": "让程序连续打印 3 行：<br><code>护盾充能完毕！</code>",
    "starter": "# 👈 点击上方积木，用 range(3) 重复充能 3 次：\nfor i in range(3):\n    print(\"护盾充能完毕！\")\n",
    "blocks": [
      "for i in range(3):",
      "    print(\"护盾充能完毕！\")",
      "range(3)"
    ],
    "hints": [
      "点击下方【🪄 召唤小博一键拼装】或直接点击上方积木！"
    ],
    "must": [
      "for",
      "range",
      "print"
    ],
    "solution": "for i in range(3):\n    print(\"护盾充能完毕！\")",
    "tests": [
      {
        "inputs": [],
        "out": "护盾充能完毕！\n护盾充能完毕！\n护盾充能完毕！\n"
      }
    ]
  },
  {
    "id": 10,
    "level": 2,
    "tierName": "Lv.2 进阶极客 · 逻辑大脑",
    "badge": "⚡ 倒计时",
    "title": "火箭发射倒计时：遍历列表",
    "img": "assets/python/l10.jpg",
    "goal": "依次输出倒计时 3、2、1，并在倒计时结束后在外面打印“发射！”",
    "story": [
      "循环不仅可以计次数，还可以挨个遍历清单里的数字！",
      "<code>for i in [3, 2, 1]:</code> 会让数字 3、2、1 轮流登场！",
      "注意：最后一句 <code>print(\"发射！\")</code> <b>不要缩进</b>，这样它只会在循环全部结束后发射一次！"
    ],
    "vocab": [
      {
        "w": "iterate",
        "ph": "/ˈɪtəreɪt/",
        "kind": "动作",
        "zh": "遍历/挨个巡检",
        "root": "把队伍里的人从头到尾挨个点一遍名！",
        "ex": [
          "for x in [3,2,1]:",
          "逐个巡检"
        ]
      }
    ],
    "errors": [
      {
        "code": "for i in [3, 2, 1]:\n    print(i)\n    print(\"发射！\")",
        "say": "把发射也缩进到循环里了！这样会发射 3 次！发射应该放在循环外面！",
        "fix": "for i in [3, 2, 1]:\n    print(i)\nprint(\"发射！\")"
      }
    ],
    "demo": "for i in [3, 2, 1]:\n    print(i)\nprint(\"发射！\")",
    "taskPrompt": "依次输出 3、2、1，最后输出一行：<code>发射！</code>",
    "starter": "for i in [3, 2, 1]:\n    print(i)\n# 👈 倒计时结束，在循环外点击积木发射：\nprint(\"发射！\")\n",
    "blocks": [
      "print(\"发射！\")",
      "for i in [3, 2, 1]:",
      "    print(i)"
    ],
    "hints": [
      "最后一句 print(\"发射！\") 不带缩进，直接顶格写！"
    ],
    "must": [
      "for",
      "print"
    ],
    "solution": "for i in [3, 2, 1]:\n    print(i)\nprint(\"发射！\")",
    "tests": [
      {
        "inputs": [],
        "out": "3\n2\n1\n发射！\n"
      }
    ]
  },
  {
    "id": 11,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 累加宝箱",
    "title": "存钱罐魔法：1 加到 5 的总和",
    "img": "assets/python/l11.jpg",
    "goal": "用一个存钱罐变量 total，通过循环把 1 到 5 全加进去，输出 15！",
    "story": [
      "💰 <b>累加器模型</b>是全宇宙最经典的算法！",
      "先准备一个空存钱罐 <code>total = 0</code>。",
      "每来一个金币 <code>i</code>，就执行 <code>total = total + i</code> 扔进罐子里！最后罐子里的就是所有数字的大合集！"
    ],
    "vocab": [
      {
        "w": "accumulator",
        "ph": "/əˈkjuːmjəleɪtə/",
        "kind": "算法模型",
        "zh": "累加器",
        "root": "像滚雪球一样，把每一次的结果越滚越大！",
        "ex": [
          "total = total + i",
          "不断累加"
        ]
      }
    ],
    "errors": [
      {
        "code": "total = 0\nfor i in range(1, 6):\n    total = i",
        "say": "写成了 total = i 就会覆盖前面的数字！必须写 total = total + i！",
        "fix": "total = total + i"
      }
    ],
    "demo": "total = 0\nfor i in range(1, 6):\n    total = total + i\nprint(total)",
    "taskPrompt": "计算 1+2+3+4+5，最终输出：<code>15</code>",
    "starter": "total = 0\nfor i in range(1, 6):\n    total = total + i\n# 👈 点击下方积木，打印出最终存钱罐的总数：\nprint(total)\n",
    "blocks": [
      "print(total)",
      "total = total + i",
      "total = 0"
    ],
    "hints": [
      "点击积木【print(total)】，运行就能得到 15！"
    ],
    "must": [
      "total",
      "for",
      "print"
    ],
    "solution": "total = 0\nfor i in range(1, 6):\n    total = total + i\nprint(total)",
    "tests": [
      {
        "inputs": [],
        "out": "15\n"
      }
    ]
  },
  {
    "id": 12,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 装备背包",
    "title": "查看背包第一格：列表初体验",
    "img": "assets/python/l12.jpg",
    "goal": "背包有激光剑、能量盾、加速靴，用 0 号门牌取出第一件神器！",
    "story": [
      "🎒 玩游戏都有物品栏背包。在 Python 里，用中括号 <code>[...]</code> 装着的一串东西叫<b>列表</b>！",
      "⭐ <b>黑客第一定律：程序员数数永远从 0 开始</b>！",
      "第 1 格是 <code>bag[0]</code>，第 2 格是 <code>bag[1]</code>！记住：0 才是王者位！"
    ],
    "vocab": [
      {
        "w": "list",
        "ph": "/lɪst/",
        "kind": "数据结构",
        "zh": "列表",
        "root": "方括号装起来的数据排队队列，可以放任意多装备！",
        "ex": [
          "bag = [\"A\", \"B\"]",
          "包含A和B的装备栏"
        ]
      },
      {
        "w": "index",
        "ph": "/ˈɪndeks/",
        "kind": "门牌号",
        "zh": "索引/门牌号",
        "root": "每个物品的位置编号，从 0 开始排号！",
        "ex": [
          "bag[0]",
          "第一件物品"
        ]
      }
    ],
    "errors": [
      {
        "code": "print(bag[1])",
        "say": "电脑数数从 0 开始！第 1 格的门牌号是 0，不是 1 呀！",
        "fix": "print(bag[0])"
      }
    ],
    "demo": "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]\nprint(bag[0])",
    "taskPrompt": "打印背包里的第 1 件神装：<code>激光剑</code>",
    "starter": "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]\n# 👈 第1格门牌号是0，点击下方积木打印：\nprint(bag[0])\n",
    "blocks": [
      "print(bag[0])",
      "bag[0]",
      "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]"
    ],
    "hints": [
      "点击积木【print(bag[0])】，一键打印出激光剑！"
    ],
    "must": [
      "bag",
      "print",
      "0"
    ],
    "solution": "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]\nprint(bag[0])",
    "tests": [
      {
        "inputs": [],
        "out": "激光剑\n"
      }
    ]
  },
  {
    "id": 13,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 拾取追加",
    "title": "拾取极品神装：append 追加道具",
    "img": "assets/python/l13.jpg",
    "goal": "捡到了一件“飞行羽翼”，用 append 追加进背包并打印背包！",
    "story": [
      "打怪掉落新神装怎么放进背包？",
      "在列表后面点一下 <code>.append(...)</code>（追加），新装备就会自动装进背包的最末尾！",
      "就像往抽屉里再塞进一件新宝物一样简单！"
    ],
    "vocab": [
      {
        "w": "append",
        "ph": "/əˈpend/",
        "kind": "方法",
        "zh": "追加；添加到末尾",
        "root": "英语原义就是附加、追加，把新成员塞到队伍最后！",
        "ex": [
          "bag.append(\"羽翼\")",
          "把羽翼加入背包"
        ]
      }
    ],
    "errors": [
      {
        "code": "bag.add(\"羽翼\")",
        "say": "列表的追加指令是 append，不是 add 规范哦！",
        "fix": "bag.append(\"羽翼\")"
      }
    ],
    "demo": "bag = [\"激光剑\", \"能量盾\"]\nbag.append(\"飞行羽翼\")\nprint(bag)",
    "taskPrompt": "把飞行羽翼加入背包，输出：<code>['激光剑', '能量盾', '飞行羽翼']</code>",
    "starter": "bag = [\"激光剑\", \"能量盾\"]\n# 👈 点击下方积木，把新神装塞进背包：\nbag.append(\"飞行羽翼\")\nprint(bag)\n",
    "blocks": [
      "bag.append(\"飞行羽翼\")",
      "print(bag)",
      "\"飞行羽翼\""
    ],
    "hints": [
      "点击下方【🪄 召唤小博一键拼装】或直接点击上方积木！"
    ],
    "must": [
      "append",
      "print"
    ],
    "solution": "bag = [\"激光剑\", \"能量盾\"]\nbag.append(\"飞行羽翼\")\nprint(bag)",
    "tests": [
      {
        "inputs": [],
        "out": "['激光剑', '能量盾', '飞行羽翼']\n"
      }
    ]
  },
  {
    "id": 14,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 仓库巡检",
    "title": "巡检所有装备：遍历整座仓库",
    "img": "assets/python/l14.jpg",
    "goal": "用 for 循环把背包里的每一件装备一行一行全报出来！",
    "story": [
      "机甲出战前要检阅所有武器！",
      "<code>for item in bag:</code> 就像一个尽职的仓库管理员，走进名为 <code>bag</code> 的仓库，把里面的物品一个个取出来放到 <code>item</code> 手里！"
    ],
    "vocab": [
      {
        "w": "in",
        "ph": "/ɪn/",
        "kind": "介词关键字",
        "zh": "在……里面",
        "root": "指定在哪个列表盒子里巡检成员！",
        "ex": [
          "for item in bag:",
          "在bag里面逐个查看"
        ]
      }
    ],
    "errors": [
      {
        "code": "for item in bag:\nprint(item)",
        "say": "循环内部必须缩进一个 Tab 键（4个空格）！",
        "fix": "for item in bag:\n    print(item)"
      }
    ],
    "demo": "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]\nfor item in bag:\n    print(item)",
    "taskPrompt": "分三行依次打印背包中每件装备：<br><code>激光剑</code><br><code>能量盾</code><br><code>加速靴</code>",
    "starter": "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]\n# 👈 点击下方积木，遍历整座装备库：\nfor item in bag:\n    print(item)\n",
    "blocks": [
      "for item in bag:",
      "    print(item)",
      "print(item)"
    ],
    "hints": [
      "代码骨架已经准备好啦，点击右下角运行即可！"
    ],
    "must": [
      "for",
      "in",
      "print"
    ],
    "solution": "bag = [\"激光剑\", \"能量盾\", \"加速靴\"]\nfor item in bag:\n    print(item)",
    "tests": [
      {
        "inputs": [],
        "out": "激光剑\n能量盾\n加速靴\n"
      }
    ]
  },
  {
    "id": 15,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 属性字典",
    "title": "机甲核心参数卡：键值对字典",
    "img": "assets/python/l15.jpg",
    "goal": "建立参数卡 bot = {'name': '小博', 'hp': 100}，分别查出名字和血量！",
    "story": [
      "📖 <b>字典是 Python 里最强大的百宝箱</b>！",
      "像翻中英字典一样：左边是<b>词条名（Key 键）</b>，右边是<b>内容解释（Value 值）</b>！用花括号 <code>{ }</code> 装起来！",
      "查字典不需要记门牌号 0、1、2，直接用名字查：<code>bot[\"hp\"]</code> 就能拿到 100！"
    ],
    "vocab": [
      {
        "w": "dictionary",
        "ph": "/ˈdɪkʃənri/",
        "kind": "数据结构",
        "zh": "字典",
        "root": "名字和内容一对一绑定，按名字一秒查出属性！",
        "ex": [
          "bot[\"hp\"]",
          "查出小博的血量"
        ]
      }
    ],
    "errors": [
      {
        "code": "bot = {'hp' = 100}",
        "say": "字典里配对用冒号 : ，不是等号 = 哦！",
        "fix": "bot = {'hp': 100}"
      }
    ],
    "demo": "bot = {\"name\": \"小博\", \"hp\": 100}\nprint(bot[\"name\"])\nprint(bot[\"hp\"])",
    "taskPrompt": "分两行分别输出机甲的名字 <code>小博</code> 和血量 <code>100</code>",
    "starter": "bot = {\"name\": \"小博\", \"hp\": 100}\n# 👈 点击下方积木，分别查出名字和血量：\nprint(bot[\"name\"])\nprint(bot[\"hp\"])\n",
    "blocks": [
      "print(bot[\"name\"])",
      "print(bot[\"hp\"])",
      "bot[\"hp\"]",
      "bot[\"name\"]"
    ],
    "hints": [
      "点击积木【print(bot[\"name\"])] 与 [print(bot[\"hp\"])] 一键填入！"
    ],
    "must": [
      "bot",
      "print"
    ],
    "solution": "bot = {\"name\": \"小博\", \"hp\": 100}\nprint(bot[\"name\"])\nprint(bot[\"hp\"])",
    "tests": [
      {
        "inputs": [],
        "out": "小博\n100\n"
      }
    ]
  },
  {
    "id": 16,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 战术宏",
    "title": "一键战术宏：def 自定义函数",
    "img": "assets/python/l16.jpg",
    "goal": "用 def 封装一套专属问候宏，并在最下方呼叫它执行！",
    "story": [
      "🎮 玩游戏时我们喜欢把一套连续动作设为“快捷键一键宏”！",
      "在 Python 里，用 <code>def</code>（define 定义）可以把几行代码打包成一个<b>专属新技能函数</b>！",
      "以后想用这个绝招，只要喊它的名字 <code>greet()</code>，就会立刻触发！"
    ],
    "vocab": [
      {
        "w": "define",
        "ph": "/dɪˈfaɪn/",
        "kind": "关键字",
        "zh": "定义函数",
        "root": "缩写为 def，是创造属于你自己的全新技能指令！",
        "ex": [
          "def fire():",
          "定义开火技能"
        ]
      }
    ],
    "errors": [
      {
        "code": "def greet():\n    print(\"hi\")",
        "say": "只定义了技能，没有在最下方调用它！必须写 greet() 它才会执行！",
        "fix": "def greet():\n    print(\"hi\")\ngreet()"
      }
    ],
    "demo": "def greet():\n    print(\"小博随时待命！\")\n\ngreet()",
    "taskPrompt": "定义并调用 greet 技能，输出：<code>小博随时待命！</code>",
    "starter": "# 1. 定义专属机甲口令\ndef greet():\n    print(\"小博随时待命！\")\n\n# 👈 点击下方积木，呼叫执行该技能：\ngreet()\n",
    "blocks": [
      "greet()",
      "def greet():",
      "    print(\"小博随时待命！\")"
    ],
    "hints": [
      "点击下方积木【greet()】，一键召唤技能！"
    ],
    "must": [
      "def",
      "greet"
    ],
    "solution": "def greet():\n    print(\"小博随时待命！\")\n\ngreet()",
    "tests": [
      {
        "inputs": [],
        "out": "小博随时待命！\n"
      }
    ]
  },
  {
    "id": 17,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🏆 传参大招",
    "title": "合体必杀技：带参数的大招函数",
    "img": "assets/python/l17.jpg",
    "goal": "定义一个会释放不同技能的招式 def cast(spell)，传入'离子光束'！",
    "story": [
      "如果大招可以换不同的属性子弹呢？",
      "函数的括号里可以放一个<b>插槽（参数 parameter）</b>！",
      "定义 <code>def cast(spell):</code>，你在调用时喂给它什么技能，它就释放什么大招：<code>cast(\"离子光束\")</code>！"
    ],
    "vocab": [
      {
        "w": "parameter",
        "ph": "/pəˈræmɪtə/",
        "kind": "概念",
        "zh": "参数/技能卡槽",
        "root": "函数括号里的空位，用来接收外部塞进来的数据！",
        "ex": [
          "cast(\"光束\")",
          "传入光束参数"
        ]
      }
    ],
    "errors": [
      {
        "code": "cast()",
        "say": "大招卡槽需要喂一个技能名字！比如 cast(\"离子光束\")！",
        "fix": "cast(\"离子光束\")"
      }
    ],
    "demo": "def cast(spell):\n    print(\"释放绝招：\" + spell)\n\ncast(\"离子光束\")",
    "taskPrompt": "传入技能名称，输出：<code>释放绝招：离子光束</code>",
    "starter": "def cast(spell):\n    print(\"释放绝招：\" + spell)\n\n# 👈 传入离子光束（点击下方积木）：\ncast(\"离子光束\")\n",
    "blocks": [
      "cast(\"离子光束\")",
      "\"离子光束\"",
      "cast(\"全息护盾\")"
    ],
    "hints": [
      "点击上方积木【cast(\"离子光束\")】，一键填入并释放绝招！"
    ],
    "must": [
      "def",
      "cast"
    ],
    "solution": "def cast(spell):\n    print(\"释放绝招：\" + spell)\n\ncast(\"离子光束\")",
    "tests": [
      {
        "inputs": [],
        "out": "释放绝招：离子光束\n"
      }
    ]
  },
  {
    "id": 18,
    "level": 3,
    "tierName": "Lv.3 机甲大师 · 数据与函数",
    "badge": "🎓 极客毕业礼",
    "title": "机甲极客毕业礼：综合全息大作战",
    "img": "assets/python/l18.jpg",
    "goal": "遍历技能清单依次检阅，并在最后一行打印“极客少侠圆满毕业！”",
    "story": [
      "🎉 <b>恭喜少侠！你已经解锁了 18 关全部 Python 核心秘密！</b>",
      "从最开始的一句 print，到变量、四则运算、if 分支、for 循环、列表、字典与函数！",
      "现在运行最后一次大检阅，正式荣获 13 岁机甲编程极客认证！"
    ],
    "vocab": [
      {
        "w": "developer",
        "ph": "/dɪˈveləpə/",
        "kind": "身份",
        "zh": "开发者/程序员",
        "root": "用代码让机器听指挥、改变世界的人！恭喜你正式迈入开发者殿堂！",
        "ex": [
          "print(\"Hello World\")",
          "开发者的第一声问候"
        ]
      }
    ],
    "errors": [
      {
        "code": "for s in skills\n    print(s)",
        "say": "for 循环末尾不要漏掉冒号 : 喔！",
        "fix": "for s in skills:\n    print(s)"
      }
    ],
    "demo": "skills = [\"离子光束\", \"全息护盾\"]\nfor s in skills:\n    print(s)\nprint(\"极客少侠圆满毕业！\")",
    "taskPrompt": "依次检阅技能，并在最后一行输出：<code>极客少侠圆满毕业！</code>",
    "starter": "skills = [\"离子光束\", \"全息护盾\"]\nfor s in skills:\n    print(s)\n\n# 👈 点击下方积木，打印毕业盛典台词：\nprint(\"极客少侠圆满毕业！\")\n",
    "blocks": [
      "print(\"极客少侠圆满毕业！\")",
      "for s in skills:",
      "    print(s)",
      "skills = [\"离子光束\", \"全息护盾\"]"
    ],
    "hints": [
      "点击下方【🪄 召唤小博一键拼装】或点击上方积木，见证毕业盛典！"
    ],
    "must": [
      "for",
      "skills",
      "print"
    ],
    "solution": "skills = [\"离子光束\", \"全息护盾\"]\nfor s in skills:\n    print(s)\nprint(\"极客少侠圆满毕业！\")",
    "tests": [
      {
        "inputs": [],
        "out": "离子光束\n全息护盾\n极客少侠圆满毕业！\n"
      }
    ]
  }
];
