# SP2 Minimap

《SimplePlanes 2》的导航与自动驾驶 Mod（BepInEx 插件）。

- 实时小地图（正交相机 → RenderTexture），支持小窗 / 全屏
- 航点系统：添加、拖动、排序、紫色菱形编号、航点连线
- 空中交通：识别其他玩家与 AI 飞机
- 自动驾驶：接管航向 / 高度 / 空速，支持低机动模式
- 仿波音 MCP 风格控制面板，数码窗可直接输入

详细说明见 [BLOG.md](BLOG.md)。

## 安装

1. 游戏安装 **BepInEx 5**
2. 把 `SP2Minimap.dll` 放进 `BepInEx\plugins\`
3. 进游戏

配置：`BepInEx\config\com.sp2.minimap.cfg`

## 按键

| 键 | 功能 |
|---|---|
| F5 | 全屏地图 |
| F6 | 显示/隐藏小地图 |
| F7 | 设置面板 |
| F8 | 在机头前方添加航点 |
| F9 / Enter | 自动驾驶开关 |

## 编译

`build.bat` 里改两个路径：

```bat
set GAME=D:\steam\steamapps\common\SimplePlanes 2
set CSC=...\Roslyn\csc.exe
```

然后双击即可，产物为 `SP2Minimap.dll`。

编译需要引用游戏的 `Game.dll`，因此**游戏版本更新后需要重新编译**。

## 免责

反编译仅用于本地互操作研究与接口对照。本仓库不包含任何游戏代码或资源。
