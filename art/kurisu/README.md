# 牧濑红莉栖

![牧濑红莉栖的内置立绘](portrait.png)

这里是 Windows 桌宠内置的牧濑红莉栖。基础动作和生活动作均使用随程序发行的透明 PNG 原图。

| 文件 | 用途 |
| --- | --- |
| [atlas.png](atlas.png) | 基础图集，1536 × 2288 像素。按 8 列、11 行切分，每帧 192 × 208。前九行为基础动作，最后两行为十六个注视方向。 |
| [portrait.png](portrait.png) | 角色选择页使用的 78 × 198 像素立绘。 |
| [actions/tea.png](actions/tea.png) · [game.png](actions/game.png) · [song.png](actions/song.png) · [focus.png](actions/focus.png) · [rest.png](actions/rest.png) | 依次是喝茶、藏物、跟唱、专注和休息。每张 1152 × 208，六个姿势从左往右排列。 |
| [pet.json](pet.json) | 供角色导入器读取的名称、简介和基础图集文件名。 |

## 导入角色

在仓库根目录运行以下命令，或者在资源管理器中把整个 `kurisu` 文件夹压缩成 ZIP。

```powershell
Compress-Archive -Path .\art\kurisu -DestinationPath .\kurisu.zip
```

右击桌宠，进入 **伙伴设置 → 导入与切换其他角色 → 导入角色 ZIP**，选择 `kurisu.zip`。程序会识别原图并切换到内置红莉栖。导入修改过的基础图集或动作图时，程序会为这个版本建立单独的角色，并从基础图集的首帧裁出头像。
