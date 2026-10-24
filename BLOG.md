# 给《SimplePlanes 2》写一个小地图与自动驾驶 Mod

> 一个 BepInEx 插件：把航图、航点、空中交通和自动驾驶塞进座舱里。
> 项目代号 `SP2Minimap`，当前版本 2.9。

---

## 缘起

《SimplePlanes 2》的飞行体验很好，但有两个地方一直让我难受：

1. **没有像样的导航**。想飞到某个坐标，只能靠目视和地标。
2. **长距离巡航很无聊**。手动保持航向和高度，几分钟就开始走神。

于是有了这个 Mod：一张实时小地图，加上一套能真正接管飞机的自动驾驶。

![小地图](images/minimap.png)

---

## 功能一览

### 地图

- 正交相机俯视渲染到 `RenderTexture`，不干扰主视角
- **小窗 / 全屏**（F5）两种形态，可拖动、可滚轮缩放
- 图层独立开关：地形、出生点、机场、目标、自定义物体、其他飞机
- 三种单位制：公制 / 英制 / 海里

### 航点

- **F8** 在机头前方丢一个航点，或 **Shift + 点击地图**、或开启「选点模式」后点地图
- 航点可拖动、可在列表里启用/禁用/排序/编辑高度速度/删除
- 地图上用**紫色菱形 + 编号**标出，航点之间自动连线
- 所有连线都做了矩形裁剪，不会画到地图框外

### 空中交通

- 扫描场景内的所有飞机，区分**其他玩家**（青色）和 **AI**（橙红）
- 三角图标朝向机头方向，远处也能判断态势

### 自动驾驶

- **Enter / F9** 一键接管，再按一次交还手动
- 保持航向、高度、空速；固定值或跟随航点都行
- 低机动模式：俯仰限制收紧到 +15° / −8°，适合客机式平缓飞行

### 界面

整块面板是仿波音 **MCP（Mode Control Panel）** 风格：

![MCP 面板](images/mcp.png)

- 黑底数码窗，琥珀色等宽数字，**直接点击输入**
- `−` / `+` 步进按钮，步长随单位制自动换算
- 分段条显示响应增益，点哪设哪

---

## 自动驾驶是怎么接进去的

这部分花的时间最多，也最有意思。

### 第一版：Harmony 补丁 —— 失败

最初的思路很直接：在每个 `AircraftControls.Update` 之后打补丁，强行覆盖 `Pitch/Roll/Yaw`。

问题是**和原版 AI 打架**。原版 `AiControlledAircraftScript` 里有一段：

```csharp
private void MonitorInputOverrides()
{
    if (InputOverrideEnabled != AiAircraftScript.HasInputOverrides)
    {
        if (InputOverrideEnabled) EnableInputOverrides();
        else DisableInputOverrides();   // → Controls.RemoveInputOverrides()
    }
}
```

`RemoveInputOverrides()` 会**一次性清空所有覆盖**。我们装进去的指令下一秒就被抹掉，两边互相擦除。

### 第二版：成为 AI 的一个控制模式

翻 `Game.dll` 的时候发现了官方留的口子：

```csharp
// AircraftControls
public void SetInputOverride(IGameInput input, Func<float?> overrideFunction)

// AiManagerScript
public AiControlledAircraftScript SetPlayerAsAiControllable(AiControlSystem controlSystem)
```

而原版 AI 装输入覆盖时，装的是**指向控制系统的委托**：

```csharp
controls.SetInputOverride(instance.Pitch, () => _aiControlSystem.GetPitch());
controls.SetInputOverride(instance.Roll,  () => _aiControlSystem.GetRoll());
controls.SetInputOverride(instance.Throttle, () => _aiControlSystem.GetThrottle());
```

于是思路变成：**不跟它抢，直接成为它**。

写一个 `AiControlSystem` 子类，把 `GetPitch/GetRoll/GetThrottle` 换成我们自己的输出，然后通过 `SetPlayerAsAiControllable` 挂上去。输入覆盖由原版自己装、自己卸，冲突消失。

还顺手发现：原版对「本机玩家」会主动把油门覆盖设成 `null`：

```csharp
if (AiAircraftScript.IsPrimaryLocalPlayer)
{
    controls.SetInputOverride(instance.Throttle, null);
    controls.SetInputOverride(instance.Brake, null);
}
```

所以油门得自己再装回去，否则速度完全不受控。这也是「IAS 没有自动控制」的真正原因。

### 控制律

外环到内环：

```
航向误差  →  目标坡度（±35°）  →  坡度误差  →  Roll
高度误差  →  目标俯仰（+30/−15）→  俯仰误差  →  Pitch
速度误差  →  PID                →  Throttle
```

符号是推出来的，不是试出来的。原版 `AiCfFlyToLocation.GetPitch()`：

```csharp
result = 0f - Vector3.Dot(VecToTarget.normalized, ...up);
```

目标在上方时返回 **−1**，所以**负输入才是抬头**。

---

## 踩过的坑

### 低帧率下自动驾驶发疯

控制环一开始跑在 `LateUpdate`，用 `Time.deltaTime`。60 fps 时每秒算 60 次，掉到 15 fps 就只剩 15 次——

- 指令变台阶，零阶保持时间变长
- 数值微分变成"平均角速度"，阻尼总是慢半拍

改成跑在 `FixedUpdate`，用固定步长，再给角速度加一阶低通：

```csharp
void FixedUpdate()
{
    if (Plugin.NavEnabled) ComputeControl();
}
```

现在 144 fps 和 15 fps 表现一致。

### 配置项的默认值不会更新

BepInEx 只给**不存在的键**写默认值。我把 `MaxPitchDeg` 的默认从 12 改成 30，结果线上文件里还是 12——因为键早就存在了。

后来所有改过默认值的项都**换了键名**，并在 README 里标注旧的废弃键。

### 驾驶舱坐标不等于机体坐标

游戏暴露的 `PitchAngle` / `RollAngle` 用的是 `MainCockpit.transform`：

```csharp
private float PitchAngle => Mathf.DeltaAngle(0f, Mathf.Asin(MainCockpit.transform.forward.y) * 57.29578f);
```

驾驶舱部件是可以反装的。反装 180° 之后，爬升时读出来是**负俯仰**——限幅方向直接反掉，控制环变正反馈。

改成用刚体自身坐标系算：

```csharp
float pitchAng  = Mathf.Asin(bt.forward.y) * Mathf.Rad2Deg;   // 抬头为正
float bankRight = Mathf.Asin(-bt.right.y) * Mathf.Rad2Deg;    // 右倾为正
```

### 别迷信 Harmony

能用官方扩展点就别打补丁。补丁在游戏更新后极易失效，而且和别的 Mod 冲突时很难排查。这个项目最后把三处 Harmony 补丁全部删掉了。

---

## 工具链

| 用途 | 工具 |
|---|---|
| 反编译游戏程序集 | `ilspycmd`（ILSpy 命令行） |
| 查类型/方法签名 | `ilspycmd -l c Game.dll` |
| 单类型反编译 | `ilspycmd -t <TypeName> Game.dll` |
| 编译 | VS2019 自带的 `csc.exe`，一个 `build.bat` 搞定 |
| 看 mod 资源包 | UnityPy |

整个 Mod 是**单个 .cs 文件**（约 2600 行），没有 Unity 工程，改完跑 `build.bat` 几秒出 DLL。

---

## 安装

1. 游戏装好 **BepInEx 5**
2. 把 `SP2Minimap.dll` 丢进 `BepInEx\plugins\`
3. 进游戏

配置文件在 `BepInEx\config\com.sp2.minimap.cfg`。

### 按键

| 键 | 功能 |
|---|---|
| F5 | 全屏地图 |
| F6 | 显示/隐藏小地图 |
| F7 | 设置面板 |
| F8 | 在机头前方添加航点 |
| F9 / Enter | 自动驾驶开关 |

---

## 已知限制

- 地形渲染依赖图层遮罩，不同存档可能需要微调
- 自动驾驶的姿态环参数对手感影响很大，默认值偏保守
- 只适配当前游戏版本（`Game.dll` 直接引用，游戏大更新需要重新编译）

---

## 后记

这个 Mod 断断续续写了几周，最大的收获不是"写出了一个自动驾驶"，而是**学会先读代码再动手**。

反射能跑通不代表方向对；符号猜错一次，飞机就会一头扎进地里。真正把问题解决掉的，是翻开 `Game.dll` 看到那行 `-Vector3.Dot(...)` 的瞬间。

> 反编译仅用于本地互操作研究与接口对照，不分发任何游戏代码或资源。
