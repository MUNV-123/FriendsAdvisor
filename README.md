# FriendsAdvisor · 朋友梭哈吧预测助手

为 **Gamble With Your Friends（朋友梭哈吧）** 显示当前机器的牌面、落点或安全选择。窗口可拖动，工具不会自动下注或操作游戏。

## 下载与安装

从 [Releases](https://github.com/MUNV-123/FriendsAdvisor/releases) 下载 `FriendsAdvisor-v1.2-win-x64.zip`，完整解压后安装。

1. 完全退出游戏。
2. 确保电脑已安装 **.NET 9 Runtime（Windows x64）**。
3. 双击 `安装.cmd`。默认游戏目录为 `D:\steam\steamapps\common\Gamble With Your Friends`。
4. 自己创建房间，进入赌场，走近并看向机器。

如果游戏在其他目录，在工具包目录的终端中运行：

```powershell
.\AdvisorSetup.exe install "你的游戏目录"
```

## 支持的机器

| 机器 | 主要提示 |
|---|---|
| 扫雷 Minesweeper | 本轮雷区与安全格 |
| 龙塔 DragonTower | 每层危险按钮 |
| Crash | 爆点与提前兑现提示 |
| CrossyRoad | 危险步数与可行兑现步数 |
| HiLo | 点数、当前下注胜负及可获胜门槛 |
| 百家乐 Baccarat | 庄闲牌面、点数和胜负 |
| 轮盘赌 Roulette | 目标号码与红黑、单双、大小、列、打 |
| 幸运转盘 WheelOfFortune | 落点与基础返还 |
| 黑杰克 Blackjack | 双方起手牌、暗牌、下一张牌和条件补牌序列 |

黑杰克双方共用牌堆。庄家补牌预览以“此时停牌”为条件；继续要牌、加倍或分牌后，预览会更新。工具不计算最优打法。

## 操作与恢复

- **F8**：显示或隐藏助手。
- **F7**：显示或隐藏格子标记。
- **拖动窗口**：按 Esc 显示鼠标，拖动标题栏，位置自动保存。
- **检查**：双击 `检查.cmd`。
- **卸载**：完全退出游戏后双击 `卸载.cmd`。

需要本机作为房主。已验证版本为 **游戏 1.0.33 / Unity 6000.3.6f1 / Windows Mono x64**。其他版本的安装器校验可能拒绝安装。

安装器保存并校验原版程序集备份，支持 1.0、1.1 升级。请保留游戏中的 `.friends-advisor/original.bak`，以及工具包内的 `legacy/`、`previous/` 恢复文件。

完整操作说明见 [使用说明](使用说明.md)，测试范围和二进制校验值见 [验证记录](验证记录.md)，第三方许可见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。发行文件 SHA256 见 Release 附件 `SHA256SUMS.txt`。

## 源码与测试

源码在 `source/`。编译助手需要 .NET SDK 和本机合法安装游戏中的 Managed 程序集：

```powershell
.\source\build.ps1 -GamePath "你的游戏目录"
dotnet publish .\source\installer\AdvisorSetup.csproj -c Release -o .\build\installer
.\source\tests\blackjack\test.ps1
dotnet run --project .\source\tests\race_wheels\RaceWheelHarness.csproj --artifacts-path .\build\tests
```

重新编译后须刷新 `advisor-package.json` 中的 DLL SHA256。黑杰克测试 87 项、百家乐与 HiLo 测试 39 项、转盘测试 36 项通过；独立黑杰克审查另通过 5,546 项断言，用户已确认 1.2 实际使用正常。

仓库与发行包只包含助手源码、自制 DLL、安装器及说明；运行和编译所需的游戏文件由用户自己的游戏安装提供。
