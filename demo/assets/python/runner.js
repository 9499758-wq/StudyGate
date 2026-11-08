/* 运行器：网页和验证脚本共用。依赖已加载的 Skulpt（全局 Sk）。 */
(function (root) {
  var SHIM = [
    "class _F:",
    "    def __init__(self, name, mode):",
    "        self.name = name",
    "        self.mode = mode",
    "        old = _fsget(name)",
    "        if 'r' in mode:",
    "            if old is None:",
    "                raise IOError(\"[Errno 2] No such file or directory: '\" + name + \"'\")",
    "            self.buf = old",
    "        elif 'a' in mode:",
    "            self.buf = old if old is not None else ''",
    "            _fsset(name, self.buf)",
    "        else:",
    "            self.buf = ''",
    "            _fsset(name, '')",
    "        self.pos = 0",
    "        self.closed = False",
    "    def write(self, s):",
    "        if 'r' in self.mode:",
    "            raise IOError('File not open for writing')",
    "        if not isinstance(s, str):",
    "            raise TypeError('write() argument must be str, not ' + type(s).__name__)",
    "        self.buf = self.buf + s",
    "        _fsset(self.name, self.buf)",
    "        return len(s)",
    "    def read(self):",
    "        r = self.buf[self.pos:]",
    "        self.pos = len(self.buf)",
    "        return r",
    "    def readline(self):",
    "        if self.pos >= len(self.buf):",
    "            return ''",
    "        i = self.buf.find('\\n', self.pos)",
    "        if i < 0:",
    "            r = self.buf[self.pos:]",
    "            self.pos = len(self.buf)",
    "        else:",
    "            r = self.buf[self.pos:i + 1]",
    "            self.pos = i + 1",
    "        return r",
    "    def readlines(self):",
    "        res = []",
    "        while True:",
    "            l = self.readline()",
    "            if l == '':",
    "                break",
    "            res.append(l)",
    "        return res",
    "    def __iter__(self):",
    "        return iter(self.readlines())",
    "    def close(self):",
    "        self.closed = True",
    "    def __enter__(self):",
    "        return self",
    "    def __exit__(self, a, b, c):",
    "        self.close()",
    "        return False",
    "def open(name, mode='r', encoding=None):",
    "    return _F(name, mode)",
    ""
  ];
  var SHIM_TEXT = SHIM.join("\n");
  var SHIM_N = SHIM.length - 1;

  var PLAIN = {
    SyntaxError: "这一行的写法电脑看不懂。最常见的原因：引号或括号没有成对、行尾漏了冒号、用了中文标点。",
    IndentationError: "缩进不对。冒号后面的下一行要往右缩进 4 个空格，同一块里的每一行缩进要一样多。",
    NameError: "电脑不认识这个名字。可能是：文字忘了加引号、变量还没创建就使用、或者大小写拼错了。",
    TypeError: "类型搞混了。比如把文字和数字直接相加，或者给函数的东西个数不对。",
    ValueError: "这个值不合适。比如用 int() 去转一段不是数字的文字。",
    IndexError: "编号超出范围。列表里没有这么多东西，记住编号是从 0 开始数的。",
    KeyError: "字典里没有这把“钥匙”。检查名字拼写是否一致。",
    ZeroDivisionError: "0 不能当除数，电脑算不出来。",
    AttributeError: "这个东西没有你要的这个功能。检查方法名有没有拼错。",
    IOError: "文件出问题了。多半是要读的文件还不存在，需要先写入。",
    TimeLimitError: "程序跑了太久，被自动停下了。多半是循环停不下来（死循环）。检查循环的条件有没有机会变成假。",
    ExternalError: "这个功能在网页版里暂时不支持。"
  };

  function norm(s) {
    var lines = String(s).replace(/\r/g, "").split("\n").map(function (x) { return x.replace(/\s+$/, ""); });
    while (lines.length && lines[lines.length - 1] === "") lines.pop();
    return lines.join("\n");
  }

  function parseError(e, offset) {
    var raw = (e && e.toString ? e.toString() : String(e)) + "";
    var m = raw.match(/^(\w+)\s*:\s*([\s\S]*?)(?:\s+on line (\d+))?$/);
    var type = m ? m[1] : "Error";
    var msg = m ? m[2] : raw;
    var line = m && m[3] ? parseInt(m[3], 10) - offset : null;
    if (line !== null && line < 1) line = null;
    if (type === "TimeLimitError") msg = "程序运行太久";
    var key = type;
    if (type === "SyntaxError" && /indent/i.test(msg)) key = "IndentationError";
    return { type: type, msg: msg, line: line, plain: PLAIN[key] || "出错了，看看红色提示指出的那一行。", raw: raw };
  }

  function getRepr(v) {
    var r = Sk.misceval.objectRepr(v);
    if (typeof r === "string") return r;
    if (r && r.v !== undefined) return r.v;
    if (r && r.$jsstr) return r.$jsstr();
    return String(r);
  }

  /**
   * run(code, opts) -> Promise<{out, err, files, globals}>
   * opts: inputs[] 模拟输入；onInput(prompt)->Promise<string> 真实输入；onOut(text) 实时输出；
   *       files{} 初始文件柜；limit 毫秒
   */
  function run(code, opts) {
    opts = opts || {};
    var inputs = (opts.inputs || []).slice();
    var files = Object.assign({}, opts.files || {});
    var out = "";
    var needShim = /\bopen\s*\(/.test(code) || /\bopen\s*\(/.test(opts.append || "");
    var full = (needShim ? SHIM_TEXT : "") + code + (opts.append ? "\n" + opts.append + "\n" : "");
    var offset = needShim ? SHIM_N : 0;
    var total = 0;
    var lastOutLen = 0;   // 每次 print 之前 out 的长度；input 提示语是紧挨着 inputfun 之前的那一次输出
    var lastChunk = 0;

    Sk.configure({
      output: function (t) {
        total += t.length;
        if (total > 30000) throw new Error("OutputLimit: 输出内容太多，已停止");
        lastChunk = out.length;
        out += t;
        if (opts.onOut) opts.onOut(t);
      },
      read: function (f) {
        if (Sk.builtinFiles === undefined || Sk.builtinFiles["files"][f] === undefined) throw "File not found: '" + f + "'";
        return Sk.builtinFiles["files"][f];
      },
      inputfun: function (p) {
        if (inputs.length) {
          var v = inputs.shift();
          if (opts.quiet) { out = out.slice(0, lastChunk); return v; }   // 判题时：把刚输出的 input 提示语从结果里去掉
          var t = (p || "") + v + "\n";
          out += t; if (opts.onOut) opts.onOut(t);
          return v;
        }
        if (opts.quiet && opts.eofError) throw new Sk.builtin.EOFError("程序要求的输入比测试提供的多：检查 input() 的次数");
        if (opts.onInput) {
          // 等用户打字的时间不能算进"运行时间"，否则想 3 秒就被误判成死循环
          return Promise.resolve(opts.onInput(p || "")).then(function (v) {
            Sk.execStart = new Date();
            return v;
          });
        }
        var t2 = (p || "") + "\n";
        out += t2; if (opts.onOut) opts.onOut(t2);
        return "";
      },
      __future__: Sk.python3,
      execLimit: opts.limit || 3000
    });
    Sk.builtins._fsget = new Sk.builtin.func(function (n) {
      n = Sk.ffi.remapToJs(n);
      return Object.prototype.hasOwnProperty.call(files, n) ? new Sk.builtin.str(files[n]) : Sk.builtin.none.none$;
    });
    Sk.builtins._fsset = new Sk.builtin.func(function (n, t) {
      files[Sk.ffi.remapToJs(n)] = Sk.ffi.remapToJs(t);
      return Sk.builtin.none.none$;
    });

    return Sk.misceval.asyncToPromise(function () {
      return Sk.importMainWithBody("<stdin>", false, full, true);
    }).then(function () {
      var g = {};
      try {
        Object.keys(Sk.globals || {}).forEach(function (k) {
          if (k[0] === "_" || k === "F" || k === "open") return;
          var kk = k.replace(/_\$rw\$$/, "");
          var v = Sk.globals[k];
          var tn = (v && v.tp$name) || (v && v.ob$type && v.ob$type.tp$name) || "";
          g[kk] = { type: tn, repr: getRepr(v) };
        });
      } catch (e) { /* ignore */ }
      return { out: out, err: null, files: files, globals: g };
    }, function (e) {
      return { out: out, err: parseError(e, offset), files: files, globals: {} };
    });
  }

  /** 判题：逐个测试，返回 {ok, results[]} */
  function judge(code, task) {
    var chain = Promise.resolve([]);
    (task.tests || []).forEach(function (t) {
      chain = chain.then(function (acc) {
        var opts = { inputs: t.inputs || [], quiet: !!task.quiet, eofError: !!task.quiet, append: t.append || task.append || "", files: t.files || task.files || {} };
        return run(code, opts).then(function (r) {
          var want = norm(t.out);
          var got = norm(r.out);
          var ok = !r.err && (task.contains ? got.indexOf(want) >= 0 : want === got);
          acc.push({ ok: ok, want: t.out, got: r.out, err: r.err, inputs: t.inputs || [], hidden: !!t.hidden, note: t.note || "" });
          return acc;
        });
      });
    });
    return chain.then(function (results) {
      var mustFail = [];
      (task.must || []).forEach(function (re) {
        if (!new RegExp(re).test(code)) mustFail.push(re);
      });
      var ok = results.every(function (x) { return x.ok; }) && mustFail.length === 0;
      return { ok: ok, results: results, mustFail: mustFail };
    });
  }

  root.PyRunner = { run: run, judge: judge, norm: norm, parseError: parseError, SHIM_N: SHIM_N };
})(typeof window !== "undefined" ? window : globalThis);
