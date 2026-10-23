# FaceGuard 登录留影

> 锁屏 / 解锁 / 开机登录时，用摄像头把「正在登录的人」拍下来，全部保存在本机。
> 纯本地运行，不联网、不上传；单个 exe + 一个 ini，无第三方运行库（.NET Framework 4.8 系统自带）。

**English TL;DR** — A small Windows tool that photographs whoever is signing in: while the lock/login screen is showing, at the moment of unlock, and on failed password attempts. Runs locally, never sends anything over the network. Windows only, .NET Framework 4.8, no external runtime. UI is Chinese.

---

## 特性

- **登录现场拍照**：停在锁屏/登录界面时每 4 秒留一张（画面没变自动跳过），谁试密码都会被拍
- **一次事件只留一张**：拍到即停，不连拍、不重复重试；连续失败退避 2 分钟
- **登录后第一张**：登录完成约 1.5 秒后拍一张 `logon`，记录刚坐下来的那个人
- **失败密码补拍**（需管理员 + 系统模式）：安全日志 4625 触发，立即补拍
- **不影响刷脸登录**：只用彩色（RGB）相机，红外（IR）相机硬性拒绝；一次性开流、拍完立刻释放
- **不打扰**：没有托盘图标、没有常驻窗口；登录后只弹一个 4 秒自动消失的状态提示，不抢焦点
- **本地存储**：按天分目录、8×8 均值哈希去重、14 天 / 3000 张自动清理、数据目录 ACL 收紧
- **零依赖**：`csc` 直接编译，取流优先 DirectShow，取不到时自动回退 ffmpeg

---

## 它是怎么工作的

登录界面（安全桌面）不允许普通程序截屏、读输入。所以本程序做的是**在登录现场拍照**，而不是"记录登录过程"。

| 触发时机 | 覆盖范围 | 说明 |
|---|---|---|
| 停留在锁屏 / 登录界面 | 用户模式 + 系统模式 | 每 4 秒一张（640×480，占用小）；画面没变自动跳过 |
| 解锁 / 登录成功瞬间 | 用户模式 + 系统模式 | 拍 1 张（拍到即停） |
| 登录完成（进程启动） | 用户模式 | 登录后约 1.5 秒拍 1 张 `logon` |
| 锁屏瞬间 | 用户模式 + 系统模式 | 拍下按下 Win+L 的人 |
| 睡眠唤醒 | 用户模式 | 唤醒后若停在锁屏，由上面的轮拍覆盖 |
| 开机登录界面（未登录时） | **仅系统模式** | SYSTEM 守护开机即运行，登录前就在轮拍 |
| 密码输错（事件 4625） | 仅系统模式 | 立即补拍 1 张，带 `failed-logon` 标签 |

两种安装模式：

- **用户模式**（默认，无需管理员）：登录自启，覆盖登录之后的锁屏/解锁/唤醒/登录首拍
- **系统模式**（需管理员）：注册开机任务（SYSTEM），额外覆盖"未登录时的登录界面"和失败密码

> 系统模式下"SYSTEM 进程能否打开相机"取决于机型/驱动，本仓库未在所有硬件上验证；若日志出现 `E_ACCESSDENIED`，请退回用户模式。

---

## 安装

### 方式 A：用户模式（无需管理员）

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -Mode user
```

装到 `%LOCALAPPDATA%\Programs\FaceGuard`，在「启动」文件夹放一个快捷方式，登录后自动运行。

### 方式 B：系统模式（需管理员）

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1
```

额外做：装到 `C:\Program Files\FaceGuard`、注册开机任务 `FaceGuard Watch`、开启登录失败审核（4625）、注册登录提示任务。

### 卸载

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1          # 保留照片
powershell -ExecutionPolicy Bypass -File .\uninstall.ps1 -Purge   # 连照片一起删
```

### 日常启停

```text
启动 FaceGuard.cmd    已在运行则不重复启动
关闭 FaceGuard.cmd    停开机任务（如有）+ 结束后台进程
```

---

## 编译

需要 .NET Framework 4.8（Windows 10/11 自带）与 `csc.exe`，无需 Visual Studio / NuGet：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

产物：

- `bin\FaceGuard.exe` — 命令行（`test` / `status` / `diagnose` / `capture` / `set-shots` / `cleanup`）
- `bin\FaceGuardDaemon.exe` — 后台守护（`agent` / `watch` / `notify`）

> `bin` 只是编译产物，日常用安装目录里的程序或两个 `.cmd`。

---

## 快速验证

```powershell
# 拍一张测试照：设备 / 分辨率 / 平均亮度 / 耗时
& "C:\Program Files\FaceGuard\FaceGuard.exe" test

# 环境自检：权限、摄像头、锁屏状态、安全日志
& "C:\Program Files\FaceGuard\FaceGuard.exe" diagnose

# 状态与最近事件
& "C:\Program Files\FaceGuard\FaceGuard.exe" status

# 改照片输出目录（默认：桌面\FaceGuard 照片）
& "C:\Program Files\FaceGuard\FaceGuard.exe" set-shots --path "D:\some\folder"
```

真实测试：按 **Win+L** 锁屏 5～10 秒再解锁，打开照片目录应能看到 `lock` / `unlock`（用户模式下还有 `logon`）。

---

## 配置（`config.ini`）

- 数据目录（配置/日志/事件）：`C:\ProgramData\FaceGuard`（不可写则退回 `%LOCALAPPDATA%\FaceGuard`）
- 照片目录：默认 `桌面\FaceGuard 照片`，按天建子目录

改完重启守护生效。

| 键 | 默认 | 说明 |
|---|---|---|
| `device` | `auto` | 自动选彩色相机（避开红外）；也可填序号或名称片段 |
| `width` / `height` | `1280` / `720` | 请求分辨率，不支持时自动协商 |
| `quality` | `85` | JPEG 质量 30–100 |
| `backend` | `auto` | 先 DirectShow，失败自动用 ffmpeg；可强制 `dshow` / `ffmpeg` |
| `ffmpegPath` | 空 | ffmpeg 路径，空 = 自动查找（程序目录、PATH、常见位置） |
| `retentionDays` / `maxFiles` | `14` / `3000` | 照片保留策略 |
| `lockPollMs` | `4000` | 登录界面轮拍间隔，`0` = 关闭（完全不占相机） |
| `pollWidth` / `pollHeight` | `640` / `480` | 轮拍分辨率 |
| `unlockBurst` | `3` | 一次事件内最多尝试几次（拍到即停） |
| `failedLogon` / `successLogon` | `1` / `1` | 4625 / 4624 事件触发（需管理员） |
| `dedupe` / `dedupeWindowMs` | `1` / `6000` | 画面去重 |
| `startupToast` | `1` | 登录后是否弹一次状态提示 |

---

## 与 Windows Hello 刷脸登录的关系

Windows Hello 用的是**红外（IR）相机**，本程序用的是**彩色（RGB）相机**，物理上是两台设备：

| | 用途 | 本程序 |
|---|---|---|
| RGB 相机 | 普通拍照 | 只用它 |
| IR 相机 | Windows Hello 刷脸 | **永不打开**（代码硬性拒绝，配置里填了也会报错） |

在此之上：

1. 每张照片都是「开流 → 取 1 帧 → 立刻关闭」，不常驻占用；
2. 登录界面轮拍降到 640×480、每 4 秒一张，USB 带宽占用可忽略；
3. 所有拍摄走单队列，不会两路同时抢设备。

要 100% 零占用：`lockPollMs = 0`（只在锁屏/解锁/唤醒/失败密码时各拍一次）。

---

## 已知限制

1. **拍不到登录界面本身**：安全桌面不允许截屏，也拿不到输入内容；只能拍"站在机器前的人"，且分不清这次是刷脸还是密码。
2. **开机登录界面只有系统模式能覆盖**；用户模式从登录之后开始。
3. 用的是彩色相机，不是 Hello 的红外相机，所以照片是普通彩色画面。
4. 相机被其它程序独占（如视频会议）时拍不到，会重试并记录日志。
5. 轮拍会亮摄像头指示灯（硬件行为）；介意就把 `lockPollMs` 调大或设 0。
6. 不是防盗软件：只留影像证据，不阻止登录、不报警、不上传。

---

## 常见问题

| 现象 | 处理 |
|---|---|
| `test` 报"拒绝访问" | 设置 → 隐私和安全性 → 相机 → 允许桌面应用访问相机 |
| 报"未找到 ffmpeg" | 把 `ffmpeg.exe` 放进程序目录，或用 `-FfmpegPath` 指定 |
| 照片全黑 / 亮度≈0 | 镜头物理遮挡，或相机被禁用 |
| 锁屏没有新照片 | 看日志是否有 `拍摄 ... 失败`；系统模式确认任务在跑 |
| 系统模式没反应 | 检查任务是否以 SYSTEM 运行；`diagnose` 看权限与 4625 状态 |
| 想临时停用 | `taskkill /im FaceGuardDaemon.exe /f` |

日志：`C:\ProgramData\FaceGuard\FaceGuard.log`（或 `%LOCALAPPDATA%\FaceGuard\`）。

---

## 项目结构

```
src/DShow.cs      DirectShow 设备枚举 + SampleGrabber 取流
src/Ffmpeg.cs     ffmpeg 后备取流
src/Capture.cs    后端选择与命令行子命令
src/Watch.cs      锁屏检测（LogonUI 进程）、SessionSwitch、4625/4624、拍摄队列
src/Store.cs      存储、去重、留存清理、目录权限收紧
src/Toast.cs      启动自检 + 自动消失的状态提示
src/Config.cs     配置读写
src/Commands.cs   status / diagnose / set-shots
src/Log.cs        日志
install.ps1       安装（用户模式 / 系统模式）
uninstall.ps1     卸载
build.ps1         编译（csc，无 NuGet）
```

---

## AI 参与说明

**本项目代码由 AI 生成，人类负责需求、验收与最终取舍。**

- 代码：由 AI 编码助手（DeepSeek 模型，经 DSH 编码代理）编写，包含架构、实现、安装脚本与文档
- 人类：提出需求与修改意见、实际运行验证、决定功能取舍（如去掉托盘、改输出目录、拍摄即停）
- 未经人工逐行审计：请自行审阅后再用于生产环境

详见 [AI-DISCLOSURE.md](AI-DISCLOSURE.md)。

---

## 隐私与合法使用

- 所有照片只保存在本机，程序**没有任何网络代码**。
- 照片默认放在桌面，登录后任何能操作桌面的人都能看到；介意请用 `set-shots` 改到别处。
- **只用在你自己的设备上**，并事先告知会使用这台设备的其他人。很多国家和地区对"未经告知拍摄他人面部"有明确法律限制，请自行确认合规。
- 不要把它装到别人的电脑上。

---

## License

[MIT](LICENSE)
