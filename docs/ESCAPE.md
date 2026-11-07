# 逃生单页（打印出来贴显示器边上）

锁在答题门里、口令又想不起来时，照这个做。**不需要口令，不需要答题。**

---

## 第一步：Ctrl + Alt + Del

登录界面出来。右下角有个电源图标。

## 第二步：按住 Shift，点"重启"

必须**按住 Shift 不放**，一直按到出现蓝色选择界面。

## 第三步：疑难解答 → 高级选项 → 启动设置 → 重启

选 **4（安全模式）** 或 **9（带命令提示符的安全模式）**。

## 第四步：在命令行里跑

先确认盘符（WinRE 里盘符会错位，C 可能变成 D）：

```cmd
diskpart
list volume
exit
```

找到装系统的那个卷（有 Windows 文件夹的）。假设是 `D:`：

```cmd
D:
cd D:\StudyGate\scripts
powershell -ExecutionPolicy Bypass -File uninstall.ps1
```

如果 StudyGate 装在别的路径，用 `dir D:\ /s StudyGate` 找一下。

## 第五步：重启回正常模式

桌面回来了。

---

## 更快的路：直接改注册表

WinRE 命令行里一行搞定（假设系统盘是 `D:`）：

```cmd
reg load HKLM\OFFLINE D:\Windows\System32\config\SOFTWARE
reg add "HKLM\OFFLINE\Microsoft\Windows NT\CurrentVersion\Winlogon" /v Shell /t REG_SZ /d explorer.exe /f
reg unload HKLM\OFFLINE
```

然后重启。这行只改 `Shell`，计划任务还在，下次登录前记得跑 `uninstall.ps1` 清干净（正常模式里跑，不需要管理员权限也能删用户级任务）。

---

## 别忘了还有一条：隐蔽口令通道

答题页直接敲：

```
（先按一下 Ctrl+A 无效，直接敲下面这串）
\x01 + 你设的口令 + 回车
```

那个 `\x01` 是 **Ctrl+A 的字符码**，实际按键就是 `Ctrl + A`，但界面不会有任何反应 —— 之后接着打字，再回车。

成功就静默放行，什么都不显示。

---

## 优先级

1. 记起口令 → 最快，答题页直接敲
2. 改注册表 → 30 秒，WinRE 里一行
3. 跑 uninstall.ps1 → 最干净，会连计划任务一起清

三条都不需要答题。
