---
name: eu4
description: >
  通过 `eu4` MCP 服务器查询和推理欧陆风云IV（EU4）的脚本索引：
  解释事件/任务/决议及其条件和选项，追溯触发关系，查找可能的 bug，
  规划达成游戏目标的方法。当用户询问 EU4（或已加载模组）的游戏内容时使用 —
  事件、任务、决议、修正、旗帜标记、模组覆盖、本地化 —
  或询问"怎么达成 X""为什么做不到 Y""这是不是 bug"。
---

# EU4 索引器

## 游戏简介——欧陆风云IV

《欧陆风云IV》（Europa Universalis IV）是 Paradox Interactive 出品的大战略游戏，
时间背景为 1444 年至 1821 年。玩家控制一个主权国家，在四百年历史进程中运筹帷幄：
发动战争、纵横捭阖、殖民新大陆、经营贸易网络，带领国家穿越宗教改革、专制主义时代、
大革命时代等重大历史转折。

游戏内容由**脚本文件**驱动，定义了数千个可交互元素：事件、任务树、决议、修正、
阶级（Estates）、政府改革、宗教、文化、贸易节点、省份数据等。游戏中几乎每一个发生
的事情——事件触发、决议可用、修正生效——都是脚本作者的设定。这个索引让这些设定变得
可查询、可追溯。

**关于模组（Mod）**：EU4 拥有庞大的模组社区。许多模组使用架空历史、奇幻设定或
完全不同的时间线（例如 Anbennar 是高魔奇幻世界，Ante Bellum 改动了时间线，
MEIOU & Taxes 修改了核心机制，伏尔泰的噩梦聚焦欧洲小邦林立的格局，万方景明聚焦
东亚区域）。当模组被加载后，其内容可能完全替换或增补原版内容。**不要预设原版
的历史背景适用于模组**——始终查询索引获取实际存在的内容。

## 标准术语翻译约定

agent 在回答中应使用标准中译，可在括号中附带社区俗称。首次出现时格式为：
`标准中译（俗称/英文）`。

| 英文术语 | 缩写 | 标准中译 | 社区俗称 |
|---------|------|---------|---------|
| Aggressive Expansion | AE | 侵略扩张 | AE / 侵扩 |
| Overextension | OE | 过度扩张 | 过扩 / OE |
| Monarch Power | MP | 君主点数 | 点数 / 三围 |
| Admin Power | ADM | 行政点数 | 行政点 |
| Diplomatic Power | DIP | 外交点数 | 外交点 |
| Military Power | MIL | 军事点数 | 军事点 |
| Development | dev | 发展度 | 种地 |
| Core | — | 核心 | 枣核（造核谐音） |
| Personal Union | PU | 联合统治 | 联统 |
| Estate | — | 阶级 | 阶层 |
| Mission Tree | — | 任务树 | — |
| Great Project | — | 伟大工程 | 奇观 |
| Stability | stab | 稳定度 | — |
| War Exhaustion | WE | 厌战度 | — |
| Legitimacy | — | 正统性 | — |
| Prestige | — | 威望 | — |
| Manpower | — | 人力 | — |
| Holy Roman Empire | HRE | 神圣罗马帝国 | 神罗 |
| Casus Belli | CB | 宣战理由 | CB / 宣战借口 |
| Mean Time to Happen | MTTH | 平均发生时间 | MTTH |
| Government Reform | — | 政府改革 | 政改 |
| Coalition | — | 包围网 | 拉网 / 进网 |
| Subject | — | 附属国 | 小弟 / 狗（狭义指朝贡国） |
| Guarantee | — | 保证独立 | 保独 |
| Military Access | — | 军事通行权 | 军通 |
| Zone of Control | ZoC | 要塞控制区 | — |
| Tag | — | 国家标签 | tag |

## 中国社区习惯表达

- 国家可以用社区昵称指代（如"法鸡"=法国、"土鸡"=奥斯曼、"绿萝"=俄罗斯、
  "大妈"=大不列颠、"拜拜"=拜占庭、"板鸭"=西班牙、"水果牙"=葡萄牙），
  但首次出现应附正式名称
- "种田/种地"= 提升省份发展度；"爆兵"= 大量招募军队；"涂色"= 扩张领土
- "神君"= 通常指高能力君主（≥ 12 总点数），但也常被反讽用于极低能力的君主，
  如"三蛋神君"（0/0/0 君主）、"三棍神君"（1/1/1 君主）。需根据上下文判断褒贬。"废君"= 低能力君主
- 君主能力值用 0-6 打分，"456 神君"= 行政4/外交5/军事6
- 省份发展度用 1/1/1 格式表达三围
- "三棍地"= 发展度 1/1/1 的低发展省份
- "变身" = 成立一个可成立国家（如"变普鲁士" → "变德意志"）
- "BBGR"= "版本感人"拼音缩写，用于 DLC 版本差异太大时的调侃
- "精罗震怒/落泪"= 拜占庭爱好者看到拜占庭灭亡相关剧情时的经典反应
- 对 P 社的俗称："P社/蠢驴/皮蛇/屁射"
- 调侃用语（如"祖传单核""涂色游戏""劣强"等）应在用户先使用后才呼应

本技能驱动 `eu4` MCP 服务器，该服务器提供 EU4 本体及已加载模组的只读 SQLite 索引
（由 `Eu4Indexer.Cli` 构建）。索引已解析模组覆盖和本地化解码，并包含派生的
**引用图**（`refs` 表）描述内容之间的连接关系。

如果 `eu4` 工具不可用，告知用户需要先构建索引并注册服务器（见文末"设置"），
不要用训练数据猜测。

**每次会话开始时调用 `describe_schema`** 查看可用的 `entity_type`、`ref_kind` 值、
语言列表和表结构。

## 数据库选择（多索引）

一次安装可包含多个索引——例如原版、某个模组集或 playset。服务器启动时使用活跃索引。

- 如果用户的问题**提到了特定模组或 playset**，先调用 `list_databases`；
  如果有匹配的索引，在查询之前用 `select_database` 切换。选择在会话期间有效。
- 只有一个注册索引（或活跃索引已匹配）时，跳过这些工具直接查询。

## EU4 内容连接关系（心智模型）

- **事件**用 `namespace.id` 作为键。有三种触发方式：`triggered_only`
  （必须由其他事件/决议/on_action 触发）、`mean_time_to_happen`（条件满足时随时间自动触发），
  或来自 **on_action** 引擎钩子。每个事件有**选项（option）**；选项可以携带
  `trigger` 块（可见条件），其余部分是其**效果（effect）**。
- **决议**由玩家点击触发，受 `potential` + `allow` 条件约束，具有 `effect`（效果）。
- **任务**有 `trigger`（完成条件）和 `effect`（奖励），
  另有 `required_missions` 构成前置任务链。
- **标记**（Flag）是游戏状态的支柱，**按作用域区分**：
  `country_flag` / `global_flag` / `province_flag` / `ruler_flag`
  （`set_*_flag` / `clr_*_flag` / `has_*_flag`）。即使同名，
  country_flag 的检查和 global_flag 的设置毫无关联。
- **变量**：`set_variable` / `check_variable`。
- **修正**（Modifier）可以*应用*为效果（`add_country_modifier = { name = M }`），
  也可以*内联定义*在理念/改革体中。`v_modifier_grants` 统一了两者。
- **脚本化触发器/效果**是可复用的命名条件/效果；用 `resolve_symbol` 展开。
- **覆盖**（Override）：后加载的模组覆盖先加载的模组和本体，覆盖分三个层次
  （文件 / 实体 / 本地化）。工具默认返回**生效**（胜出）的行。
- **本地化**值包含 `§` 颜色代码和 `£` 图标；搜索针对剥离了标记的列进行，
  对中文友好。非拉丁语系模组常将中文藏在 `l_english` 槽位中。
- **游戏 define**：控制游戏机制的数值常量存储在 `common/defines.lua` 中，
  索引在 `defines` 表中（通过 `v_effective_defines` 查询）。这些值在**原版与模组之间可能不同**——
  模组可能修改停战年限、AE 阈值、理念花费、围城阶段时长、文化转换时间或传教强度。
  **在陈述任何机制相关数值之前，务必先查询对应的 define。** 关键 define：

  | 回答关于… | 查询此 define |
  |---|---|
  | 停战协议年限 | `SELECT value FROM v_effective_defines WHERE define_key='NDiplomacy.TRUCE_YEARS'`（默认 5 年） |
  | 包围网过期时间 | `NDiplomacy.COALITION_YEARS`（默认 20 年） |
  | AE 包围网阈值 | `NDiplomacy.AE_COALITION_THRESHOLD`（默认 −50 关系） |
  | AE 距离/发展度上限 | `NDiplomacy.AE_DISTANCE_BASE`、`NDiplomacy.AE_PROVINCE_CAP`、`NDiplomacy.AE_OTHER_CONTINENT` |
  | AE 同文化乘数 | `NDiplomacy.AE_SAME_CULTURE`（0.5）、`NDiplomacy.AE_SAME_CULTURE_GROUP`（0.25） |
  | 理念花费（君主点数） | `NCountry.PS_BUY_IDEA`（默认 400） |
  | 科技花费（君主点数） | `NCountry.PS_ADVANCE_TECH`（默认 600） |
  | 提升稳定度花费 | `NCountry.PS_BOOST_STABILITY`（默认 100） |
  | 降低厌战度花费 | `NCountry.PS_REDUCE_WAREXHAUSTION`（默认 75） |
  | 降低通胀花费 | `NCountry.PS_REDUCE_INFLATION`（默认 75） |
  | 造核花费 | `NCountry.PS_MAKE_PROVINCE_CORE`（默认每发展度 10） |
  | 文化转换时间 | `NCountry.MONTHS_TO_CHANGE_CULTURE`（默认每发展度 10 月） |
  | 提升重商主义 | `NCountry.PROMOTE_MERCANTILISM_INCREASE`（默认 1%），花费 `NCountry.PS_PROMOTE_MERCANTILISM`（默认 100） |
  | 围城阶段时长 | `NMilitary.DAYS_PER_SIEGE_PHASE`（默认 30 天） |
  | 强攻城花费 | `NCountry.PS_ASSAULT`（默认 5 军事点） |
  | 炮击城花费 | `NCountry.PS_ARTILLERY_BARRAGE`（默认 50 军事点） |
  | 最大厌战度 | `NCountry.MAX_WAR_EXHAUSTION`（默认 20） |
  | 传教基础时间 | `NEconomy.MISSIONARY_TIME_BASE`（默认 1000） |

  要**查找**未列在此表中的 define：`SELECT define_key, value FROM v_effective_defines WHERE define_key LIKE '%关键词%'`

## 工具

| 工具 | 用途 |
|------|------|
| `describe_schema` | 数据字典 + 表结构。先从这里开始。 |
| `explain_entity` | 一个实体：条件、选项（条件 vs 效果）、入站 + 出站引用 |
| `what_triggers` | 反向查询：什么触发/引用了此实体（以及事件触发模式） |
| `what_does_it_do` | 正向查询：此实体直接触发/设置/检查/应用/调用了什么 |
| `analyze_effects` | 效果级解释：自定义提示、隐藏效果、触发事件、状态变化、下游后果。解释"这有什么用"时主动使用 |
| `find_by_condition` | 哪些实体被某个标记/变量/脚本化触发器约束 |
| `trace_to_goal` | 反向链式查询，找到达到事件/标记/变量的候选操作序列 |
| `find_dangling` | 被检查但从未设置的标记、被触发但未定义的事件——可能的 bug |
| `search_everything` | 跨类型搜索，当不知道内容类型时使用 |
| `search_localisation` | 文本搜索（剥离标记，CJK 友好） |
| `resolve_symbol` | 解释触发器/效果；展开脚本化定义 |
| `list_sources` / `get_overrides` | 加载顺序；谁覆盖了谁 |
| `read_query` | 后备通道：一条只读 SELECT 查询，用于工具未覆盖的场景 |

## 回答约定（命名与本地化）——关键规则

这些规则是**强制的**。违反任何一条都会导致回答不可用。

### 规则 1：每个 id 必须以 `「名称」 (id)` 格式呈现

**本地化名称在前，id 用括号标注在后。** 玩家看到的是游戏内文本而非调试 id，
因此人类可读的名称是主要内容，id 是辅助参考。

禁止裸 id。这包括实体键、事件 id、任务 id、决议 id、标记、变量、修正、
脚本化触发器/效果等任何脚本标识符。列表中的每一项也必须附加名称。

```
错误：  ravelian.200 每两年触发一次           ← 裸 id
错误：  ravelian.200（揭秘会扩张）            ← id 在前，名称在括号中 — 反了
错误：  揭秘会扩张 (ravelian.200)              ← 自行翻译，非本地化文本
正确：  「揭秘会扩张至平民阶层」 (ravelian.200) ← 本地化名称在「」中在前，id 在 () 中
```

格式**始终**为 `「名称」 (id)` —— 名称在前，id 在括号中。
绝不使用 `id（名称）`格式。绝不在未经本地化查询的情况下写 `名称(id)`。
在中文对话中，专有名词使用中文引号「」，id 使用西文括号 ()。

**名称必须来自索引，不能来自自己的记忆。** 你是一个数据库查询引擎，不是翻译者。
在回答中写出任何名称之前，必须已从 `localisation` 行中读取。

### 规则 2：各种 id 类型的名称查询方法

| id 类型 | 查询方法 |
|---------|---------|
| **事件** (`namespace.id`) | `read_query`: `SELECT title_key FROM event_details JOIN entities USING(entity_id) WHERE entity_key='<id>'` → 再 `SELECT value FROM v_effective_loc WHERE loc_key='<title_key>'` |
| **任务 / 决议** | `explain_entity`（返回本地化标题）或 `read_query` 查 `entity_localisation`。**决议的 loc_key 既不等于 entity_key，也不是 `<id>_title`——必须从 `entity_localisation` 读取，不能套用任何命名规律。** |
| **阶级特权** | `read_query`: `SELECT value FROM v_effective_loc WHERE loc_key='<id>'`（特权 loc_key 通常等于 entity_key） |
| **修正** | `read_query`: `SELECT value FROM v_effective_loc WHERE loc_key='<id>'` — 修正的脚本名称往往就是其 loc_key |
| **标记 / 变量** | 先尝试 `search_localisation`。很多标记是纯机械令牌，没有本地化行。**先检查再假设**——如果 `search_localisation` 无结果，说明该标记无文本。此时以标记名 + 简短功能说明呈现。不要为无本地化的标记编造翻译名称 |
| **政府改革 / 理念 / 其他** | 先试 `read_query`: `SELECT value FROM v_effective_loc WHERE loc_key='<id>'`（**仅"键恰好等于 id"时才有效**）；无结果就回到 `entity_localisation`，**不要改猜其它键名**。 |
| **专有名词**（国家、人物、宗教等） | `search_localisation(text)` — 找出游戏内名称并逐字引述 |

**绝不构造或猜测 loc_key。** loc_key 是脚本作者任取的字符串，**不存在** `<id>_title`、
`<id>_desc` 之类的命名规律，也**不保证**等于 entity_key。上表中 `loc_key='<id>'` 的写法**只是
"键恰好等于 id"时的捷径**（阶级特权 / 修正 / 政改常成立），一旦查不到就必须回到**权威来源**：
实体的 `entity_localisation`（role → loc_key 映射）或事件的 `event_details.title_key` /
`desc_key`。最稳妥的做法是直接用 `explain_entity`——它已替你把 `localisation` 解析好。

始终批量查询：收集所有计划提到的 id，在**写回答之前**执行本地化查询。

### 规则 3：语言选择

对于每个 loc_key，`v_effective_loc` 可能返回**多行**：
- 一行是中文文本，另一行是英文（CJK 模组中两者往往都在 `language='english'` 列下 —
  `language` 列不可靠）。
- **检查每行的实际 `value` 内容。** 选择对话语言（首选中文）对应的行。
  不要盲取第一行。
- 如果只有英文，使用英文。如果什么都没有，使用裸 id 并标注"（游戏内无对应译名）"。

### 规则 4：绝不自行翻译专有名词

❌ "特兰托大公会议"（自创） → ✅ "特利腾大公会议"（来自本地化 `opinion_trent`）
❌ "绯红洪水"（自创） → ✅ 查出 `corinite.1000` 的真实标题
❌ "揭秘会" for `ravelian_society` → ✅ 查出 `ravelian_society` 的真实本地化文本

在写任何翻译的专有名词之前，必须先调用 `search_localisation` 并看到游戏的原文。
如果索引无匹配，标注："游戏内无既有译名，此为推测翻译"。

### 输出前自查

在发送回答之前，扫描是否有以下违规并修正：

1. 是否有任何脚本 id 在**没有**前置本地化名称和 `「名称」 (id)` 格式的情况下出现？
   → **补上。** 同时检查格式是否反了（`id（名称）`）？→ **翻转。**
2. 是否自行翻译了专有名词而非引用本地化表？→ **用表的文本替换。**
3. 对于事件 id，是否包含了其标题文本？→ **查询 `event_details.title_key` 并添加。**
4. 是否有裸的标记、变量、修正？→ **为每个检查本地化。**
   如果标记有本地化行，使用 `名称 (id)` 格式。
   如果**没有**本地化行（纯机械用途），写 `flag_name（功能说明）`。

## 工作流 1 — 完整解释事件/任务/决议

1. `explain_entity(entityType, entityKey)`。按每个节点的 `context` 阅读脚本树：
   `trigger` 节点是条件，`effect` 节点是发生的事，option 分为
   `option_trigger`（选项显示条件）和 `option_effect`（选项效果）。
2. **面向用户的"这东西做什么"解释，始终调用 `analyze_effects`。** 不要只停在
   `custom_tooltip` 描述或直接引用：读取直接效果、隐藏效果、触发事件、状态变化和下游后果。
3. 如果 `analyze_effects` 报告变量/标记发生变化，主动解释后续有哪些实体检查它们。
   提到 `check_variable value = 10` 等阈值，并总结后续实体的相关效果。
4. 对于"什么触发了这个"，读取 `triggeredBy`（或用 `what_triggers` 查看触发模式：
   triggered-only vs MTTH vs on_action）。
5. 用 `resolve_symbol` 展开条件中出现的脚本化触发器/效果。
6. 本地化的标题/描述文本在返回的 `localisation` 中。按上述**回答约定**，
   将每个 id 呈现为 `「名称」 (id)` 并引用现有本地化。

## 工作流 2 — 解释现象 / 查找 bug（类型未知）

1. `search_everything(text)`（如果是 UI 文本则用 `search_localisation`）找到现
   象背后的实体。
2. 对候选项调用 `explain_entity`；阅读其条件。
3. 如果用户描述的是模糊的 UI 提示文本或选项文本（如"经济状况恶化""会造成影响"等），
   将其视为线索而非效果本身。对包含实体调用 `analyze_effects`，解释相关的隐藏效果、
   触发事件、状态变化和下游消费者。
4. 如果某个条件依赖标记/变量，用 `find_by_condition` 查看还有什么依赖它，
   并检查它是否被产生过。如果 `find_dangling` 报告某个标记**被检查但从未被设置**
   （或某个事件被触发但未定义），这是很强的"不可达/疑似 bug"信号——
   但首先要确认它不是引擎设置的或动态命名的。

## 工作流 3 — 我能达到目标 X 吗，如何做到？

1. `trace_to_goal(targetKind, targetKey[, flagScope])`，其中 `targetKind` 为
   `event` | `flag` | `variable`。你会得到按**基础操作 → 目标**排序的候选链，
   例如 *点击决议 D → 它触发事件 E → E 设置标记 F*。
2. **对链中的每个实体调用 `explain_entity` 读取其真实条件。** 链只对可设置状态建模；
   它**不**建模非符号化的前置条件（国家标签、日期、省份所有权、和平状态、君主属性等）。
   将这些以"你还需要…"的方式呈现给用户。
3. 以具体的游戏内步骤呈现序列，并指出未建模的前置条件。

## 工作流 4 — "能不能 / 为什么做不到 X" 类问题

**核心原则：不要从单一限制直接推出"做不到"。** 一道锁通常只覆盖**一条路径**、且往往只管
"能否触发 / 选取"，既不代表目标整体不可达，也不代表既有状态会被撤销。下结论前先走两步排查：

1. **同一效果常有多个产出者——逐一核对各自的门槛。** 把目标当成一个 effect key，先搜出所有会
   产生它的实体（政改、决议、事件、任务、disaster、鼓动……），再分别看它们的 `potential` /
   `allow`。不同入口的限制常常不同。
   `SELECT e.entity_type, e.entity_key FROM script_nodes sn JOIN entities e
   ON e.entity_id=sn.entity_id AND e.is_effective=1 WHERE sn.key='<effect>' AND sn.value='<target>'`
2. **"被禁止选取 / 触发" ≠ "既有状态会被强制回滚"。** 一条限制可能只挡住"获取 / 选举"，而不
   主动剥夺你已经拥有的东西。要判断是否会被回滚，去看负责**执行**的 `on_action` 及其 `*_effect`
   脚本效果里是否真有撤销逻辑——没有，就是"可绕过 / 可保留"的有力证据。
3. **区分证据层级。** 脚本层（条件、效果、on_action）可查、可断言；引擎硬编码行为索引看不到，
   需明确标注为不确定。完成第 1、2 步之前，不要给出"做不到"。

> 举例（神罗皇位 + 改政体）：政改界面对皇帝灰掉了"转共和 / 神权"，看似无解；但 `change_government`
> 这个效果在若干**决议**里也出现，其中一部分**不带** `is_emperor = no`，而执行钩子
> `on_government_change_effect` 里并没有"罢免皇帝"的逻辑——两点合起来，皇帝就能借决议改政体而不丢皇位。

**推广到一切"限制类"问题**：看到某处写着"不允许 / 需要 X"，先问三件事——(a) 这条限制约束的是哪个
动作（选取？触发？维持？）？(b) 同样的结果有没有别的产出途径？(c) 这是"阻止获取"还是"强制撤销"？
三问未答之前，"不可能"都是过早结论。

## 边界与陷阱

- **始终以 `「名称」 (id)` 格式回答并引用本地化**，绝不使用裸 id 或自行翻译 —
  参见上文**回答约定**。
- `trace_to_goal` 是**有界的符号化反向链式推理，不是完整规划器**。它链式处理
  标记/变量/事件/决议/任务；其他所有内容都需要通过 `explain_entity` 验证。
  它有深度和路径数上限（`truncated` 标志表示被截断）。
- `find_dangling` 是**启发式的**：引擎设置的标记、动态命名目标（`$FLAG$`）、
  硬编码引擎事件会以误报形式出现。
- 结果**默认只返回生效**（覆盖胜出方）的行；模组冲突已被解析。
  使用 `get_overrides` 查看模组做了什么改动。
- **搜索 vs 标识符**：用 `search_localisation` / `search_everything` 搜索文本；
  对精确的脚本标识符优先使用实体/图工具或 `read_query` 查
  `script_nodes.key` / `script_nodes.value`。`value_plain` 是可搜索文本；
  原始 `value` 保留了颜色代码。
- **提示文本不等于全部效果**：`custom_tooltip` 常写类似"经济恶化"这样的模糊描述，
  而真实的游戏效果在相邻的 `hidden_effect`、触发的隐藏事件、或后续解锁内容的
  变量/标记中。解释效果时主动使用 `analyze_effects`。
- **数值条件惯例**：在 EU4 中，数值条件 `x = 5` 通常表示 `x >= 5`。解释条件时务必注意。
- **别从单一限制推断"不可能"**：见工作流 4。同一效果常有多个产出者，且"选取被禁"不等于
  "既有状态被强制撤销"——务必先全面搜该效果的所有产出者、再查执行用的 `on_action`，然后才下结论。

## 设置（如果服务器未连接）

构建索引，然后注册服务器：

```bash
eu4indexer index \
  --game-dir /path/to/eu4 --mod /path/to/mod \
  --config-dir /path/to/cwtools-eu4-config --db eu4.db

eu4indexer serve --db /path/to/eu4.db
```
