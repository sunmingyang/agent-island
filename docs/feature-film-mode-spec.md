# Feature Spec: Film Mode(场景播放器)— 自然的 Demo 模式

## 问题
现有 demo 模式(AGENTISLAND_DEMO=1)只是静态假数据,拍出来"摆拍感"重;且切换方式(env 重启)别扭。营销需要**可重复、节奏自然、随拍随有**的真实感素材。

## 方案:不是"假数据开关",是"播放一段编排好的时间线"
新增 Film Mode:启动后 island 按**脚本时间线**演进,像真实一天的浓缩:

```
t=0s    Claude + Codex 两会话均 working(logo 呼吸,用量环缓慢变化)
t=6s    Codex 转 your-turn:logo 逆时针旋转 + 响铃 + 弹窗滑出(真实 TurnAlarm 组件)
t=10s   模拟点击"Open thread" → 弹窗收起,Codex 回到 working
t=16s   Claude 转 your-turn(顺时针旋转 + 弹窗,展示双 provider 差异)
t=22s   额度条推进到 90%+,重置倒计时走到 0 → 展示 auto-resume 触发标记
t=28s   全部回归 working → 无缝循环
```

## 要求
1. **入口隐藏**:`--film` 启动参数 + 隐藏快捷键(如 ⌥⇧F 连按两次),普通 Settings 里**不出现任何按钮**——普通用户永远不知道它存在。
2. **绝无真实副作用**(历史教训 b97b12a):Film Mode 下 Trigger/fire()/网络请求全部硬禁用,仅 UI 时间线;声音走真实铃声资源。
3. **自然感细节**:各时间点加 ±0.5-1.5s 随机抖动;用量数字非整数;会话名用真实感项目名(如 "api-refactor"、"docs-site"),避免 "Demo thread" 字样穿帮。
4. **循环 + 确定性**:默认无缝循环方便多 take;`--film-seed N` 固定随机数,便于两台机器拍出一致画面。
5. 退出:再按快捷键或正常退出 app。菜单栏/dock 无任何 Film 痕迹。

## 验收
- `./AgentIsland --film` 启动后 30s 内完整跑完上述节拍并循环;
- 全程无任何写盘/网络/CLI 调用(用 scripts/verify.sh 加一条冒烟断言);
- 录屏 30s 即可产出"像真实使用"的素材,无需任何人工配合演出。

优先级建议:1.5.2(1.5.1 已在发布流程,勿插队)。工作量估计:时间线引擎复用现有 demo 数据源 + 状态机直驱,1-2 天。
