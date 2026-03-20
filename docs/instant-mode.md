# Instant Mode

状态：已实现，可用于实机验证与 MCP 快速交互

更新时间：`2026-03-20`

---

## 目标

`instant mode` 的目标不是改写游戏动作本身，而是缩短一次动作请求到下一次可继续决策之间的等待时间：

- 服务端不再长时间阻塞等待“稳定态”
- 尽量把游戏视觉切到 `FastMode = Instant`
- 允许调用方在过渡态更早拿到 `pending`
- 结合 MCP 的 `wait_until_actionable`，更快进入下一次可操作状态

这让 agent 在地图、事件、奖励、战斗等链路中能够更快推进，而不需要为大部分动画完整播放买单。

---

## 实现概览

### 1. 动作级执行模式

`POST /action` 支持可选字段 `mode`：

- `stable`
- `instant`

`GameActionService` 会在动作执行开始时解析 `request.mode`，并通过作用域内的执行模式统一影响后续等待逻辑。

核心文件：

- `STS2AIAgent/Game/GameActionService.cs`

### 2. instant 的核心语义

`instant` 没有为每个动作单独做一套执行器，而是复用了现有动作逻辑，并改变“等待”的方式：

- `stable`：按原有 timeout 等待状态稳定
- `instant`：将等待截止时间压到当前时刻，动作提交后尽快返回

因此，`instant` 的典型返回特征是：

- 更快返回
- 更常见 `status = "pending"`
- `state` 可能仍处于过渡态

### 3. 视觉加速

在 `instant` 下，服务端会临时把游戏 `FastMode` 切到 `Instant`，以减少视觉动画时长；离开 `instant` 路径后会恢复原始设置。

这意味着当前实现更接近“尽量跳过动画并尽快返回”，而不是“强制所有动作瞬间完成”。

---

## 运行时控制

### HTTP 接口

默认 action mode 现在可以在运行中的游戏进程里切换：

- `GET /action-mode`
- `POST /action-mode`

示例：

```bash
curl -sS http://127.0.0.1:8080/action-mode
```

```bash
curl -sS -X POST http://127.0.0.1:8080/action-mode \
  -H 'Content-Type: application/json' \
  -d '{"mode":"instant"}'
```

`GET /health` 也会回显当前默认值：

- `default_action_mode`

### 游戏原生命令

Mod 现在会把 `sts2_action_mode` 注册到游戏原生 DevConsole。

可直接在控制台中输入：

```text
sts2_action_mode
sts2_action_mode instant
sts2_action_mode stable
```

实现位置：

- `STS2AIAgent/DevConsole/ActionModeConsoleCmd.cs`
- `STS2AIAgent/DevConsole/DevConsoleCommandRegistrar.cs`
- `STS2AIAgent/ModEntry.cs`

### 启动时默认值

仍然保留环境变量：

- `STS2_ACTION_MODE=instant`

它现在只是“初始默认值”，不再是唯一切换方式。

---

## MCP 侧联动

### act(mode="instant")

MCP `act(...)` 支持直接传 `mode="instant"`，并把该值透传到 `/action`。

当动作返回：

- `mode = "instant"`
- `status = "pending"`

MCP 会自动调用一次 `wait_until_actionable` 做收口，并补充：

- `transition_state`
- `state`
- `available_actions`
- `actionable`
- `post_action_wait`

### 更短的 actionable 等待

为减少 `instant` 的收口时间，`wait_until_actionable` 已调整为：

- `instant` 下只给 SSE 一个较短的事件等待窗口，默认 `0.6s`
- 若未收到事件，则立刻转快速 polling，默认间隔 `0.05s`

可通过环境变量调整：

- `STS2_MCP_INSTANT_EVENT_WAIT_SECONDS`
- `STS2_MCP_INSTANT_POLL_SECONDS`
- `STS2_MCP_FALLBACK_POLL_SECONDS`

这使得 `instant` 更偏向“快收口”，而不是把大部分 timeout 先浪费在事件流等待上。

---

## 已修复的问题

### reward 流程 instant no-op

早期 `collect_rewards_and_proceed(mode=instant)` 的奖励/继续点击逻辑完全位于一个基于 deadline 的循环内，而 `instant` 的 deadline 恰好是“现在”，导致循环体一次都不执行。

结果是：

- 动作返回了
- 但内部什么都没点
- 状态持续停在 `REWARD`
- 上层 agent 会不断重试，形成逻辑假死

现已修复为：即使 `instant` 的等待预算为 0，也会至少执行一轮奖励流点击。

### 只能启动时切换默认模式

早期只能依赖 `STS2_ACTION_MODE` 在启动前设置默认模式。现已改为支持：

- 启动前环境变量
- 运行时 HTTP 切换
- 运行时原生控制台命令切换

---

## 响应与可观测性

动作响应现在会回显实际执行模式：

- `ActionResponsePayload.mode`

这让上层客户端不必再仅靠 `pending/completed` 去推测当前动作是不是走了 `instant`。

---

## 验证情况

截至 `2026-03-20`，已覆盖以下验证：

- Mod 构建与安装通过
- MCP `tests.test_waits` 通过
- `instant-mode-smoke` 通过
- 实机从事件、地图、奖励到战斗链路已跑通多段 `instant` 流程
- 原生 `sts2_action_mode` 命令已完成注册实现

专项脚本：

- `scripts/test-instant-mode.sh`

运行时问题记录：

- `docs/instant-mode-runtime-findings-2026-03-20.md`

---

## 当前限制

`instant` 目前仍然存在一些设计上的限制，后续优化应优先关注：

- 某些动作仍然会先返回过渡态，需要依赖 `wait_until_actionable` 再收口
- 地图、事件、奖励等多阶段 UI 仍可能出现短暂的陈旧 `state`
- 部分多选/选牌状态的元数据还不够稳定，可能需要额外针对性等待
- 当前更偏向“尽快返回 + 快速收口”，还不是“无动画、无过渡、绝不 pending”

---

## 建议使用方式

如果希望获得目前最稳定的体验，建议：

1. 默认模式切到 `instant`
2. 动作调用统一走 MCP `act(...)`
3. 对 `pending` 返回使用自动 `wait_until_actionable` 收口
4. 对奖励、事件、地图等多阶段界面保留一次状态复读/确认

在现有实现下，这已经能显著缩短 agent 与游戏之间的交互停顿。
