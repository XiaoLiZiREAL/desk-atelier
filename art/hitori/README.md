# 后藤独（波奇）

《孤独摇滚！》的同人桌宠。角色采用粉色运动外套、深色百褶裙、蓝黄发饰和黑色单切电吉他。1.5 版可从「伙伴设置」直接选择。立绘与动作按同一份角色参考重新绘制。

![后藤独立绘](portrait.png)

| 动作 | 素材 |
| --- | --- |
| 待机、走路、挥手等基础动作 | [基础图集](atlas.png) |
| 拿琴练习 | [练琴](actions/focus.png) |
| 跟着歌词弹唱 | [跟唱](actions/song.png) |
| 拿起杯子喝茶 | [喝茶](actions/tea.png) |
| 藏起拨片 | [藏物](actions/game.png) |
| 钻进纸箱、探头和起身 | [休息](actions/rest.png) |

每组生活动作包含六幅 192 × 208 的透明姿势。要制作独立角色包，在仓库根目录运行下面的命令，然后从「伙伴设置 → 导入与切换其他角色」选择 ZIP。

```powershell
Compress-Archive -Path .\art\hitori\* -DestinationPath .\Hitori-Gotoh.zip
```
