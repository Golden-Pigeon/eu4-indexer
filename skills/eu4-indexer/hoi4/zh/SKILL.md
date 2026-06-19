---
name: hoi4
description: >
  通过 `eu4` MCP 服务器查询和推理钢铁雄心IV（HOI4）的脚本索引：
  解释国策、事件、决议、理念及其条件和效果；追溯国策树中的前置链和互斥路径；
  查找触发器/效果逻辑中的潜在 bug；规划解锁游戏内容的方法。
  当用户询问 HOI4（或已加载模组）的游戏内容时使用 —
  国策、事件、决议、理念、国家领袖、修正、标记、模组覆盖、本地化 —
  或询问"怎么达成 X""为什么做不到 Y""这是不是 bug"。
---

# HOI4 索引器

## 游戏简介——钢铁雄心IV

《钢铁雄心IV》（Hearts of Iron IV）是 Paradox Interactive 出品的二战大战略游戏，
时间跨度为 1936 年至 1948 年（部分模组延伸更久）。玩家掌管一个国家的军事、工业、
外交和政治机器，在人类历史上最大规模的全球冲突中运筹帷幄。

核心系统包括：**国策树**（每个国家独有的分支政策路径）、**陆军师编制**
（用营和支援连定制部队模板）、**生产线**（管理军用和民用工厂）、**后勤与补给**、
**空战与海战**（争夺制空权和制海权）、**政治力量**（用于通过法律、任命顾问和外交行动）、
以及**意识形态系统**（民主、共产、法西斯、中立）。

游戏的一切行为——国策奖励、事件触发、决议条件、理念修正、国家领袖特质——
均由**脚本文件**定义，本索引使其可搜索、可追溯。

**关于模组（Mod）**：HOI4 拥有极为庞大的模组社区。重要模组如《Kaiserreich》
（德国赢得一战的架空世界）、《The New Order》（轴心国胜利的冷战格局）、《Old World Blues》
（辐射世界观）使用完全不同的世界观、国家、意识形态和游戏机制。即使是接近原版的模组，
也可能改变国策树、添加事件或调整数值。当模组被加载后，**不要预设历史二战的背景适用**——
始终查询索引获取实际存在的内容。

## 标准术语翻译约定

agent 在回答中应使用标准中译，可在括号中附带社区俗称。

| 英文术语 | 标准中译 | 社区俗称 |
|---------|---------|---------|
| National Focus / Focus | 国策 | — |
| Focus Tree | 国策树 | — |
| Political Power (PP) | 政治力量 | 政治点 / PP |
| Civilian Factory | 民用工厂 | 民工 |
| Military Factory | 军事工厂 | 军工 |
| Naval Dockyard | 海军船坞 | 船坞 |
| Organization (Org) | 组织度 | — |
| Breakthrough | 突破 | — |
| Soft Attack | 软攻 | — |
| Hard Attack | 硬攻 | — |
| Piercing | 穿甲 | — |
| Armor | 装甲厚度 | 装甲 |
| Armor Bonus | 金盾 | —（指装甲厚度 > 敌方穿甲时的减伤加成，社区称"金盾"） |
| Encirclement | 包围 | 包饺子 |
| Planning Bonus | 计划加成 | 攒计划 |
| World Tension | 世界紧张度 | — |
| Resistance | 抵抗 | — |
| Compliance | 顺从度 | — |
| Stability | 稳定度 | — |
| War Support | 战争支持度 | — |
| Manpower | 人力 | — |
| Division | 师 | — |
| Division Template | 陆军编制 | 配兵 |
| Paradrop | 空降 | 伞击 |
| Close Air Support (CAS) | 近距空中支援 | 舔地 / CAS |
| Naval Bomber | 海军轰炸机 | 海轰 / NAV |
| Air Superiority | 制空权 | 空优 |
| Field Marshal / General | 元帅 / 将领 | — |
| Battle Plan | 作战计划 | 划线 |
| Supply | 补给 | — |
| Consumer Goods | 生活消费品 | 光合作用（消费品占比归零时的戏称） |
| Naval Invasion | 海军入侵 | 登陆 |

## 中国社区习惯表达

- 国家可以用社区昵称：**德三/三德子**（纳粹德国）、**本子/立本**（日本）、**意呆**
  （意大利）、**板鸭**（西班牙）、**水果牙**（葡萄牙）、**光头/校长**（中华民国）、
  **大胡子/慈父**（斯大林）、**小胡子/洗头佬**（希特勒）、
  **罗师傅**（罗斯福）
- 陆军编制用"X步X炮"格式描述，"1251"= 12步兵营 + 5火炮营 + 1重防空营
  "7步2炮"= 经典填线师；"高达师"= 精英装甲编制
- "种地"= 修建工厂（种民工/种军工）；"跑马"= 用小编制骑兵师快速占领胜利点
- "划线"= 制定作战计划；"划线平推"= 依赖作战计划的推进
- "包饺子"= 合围并歼灭敌军
- "落日"= 击败日本；"球长"= 统一全球（"酋长"谐音）
- "看海"= 选择小国不参战只观察局势的玩法
- "倒车雄心"= 游戏有大量复辟帝制的国策路线，戏称"开历史倒车"
- "未曾设想的道路"= 原为日本共产主义国策名，后泛指各种非主流玩法
- "西班牙军事学院"= 派志愿军干涉西班牙内战刷将领经验
- "瑞典蠢驴"= 对 P 社的戏称
- "薯片" = 中共国策"特货贸易"，大幅降低消费品占用
- 调侃用语应在用户先使用后才呼应

本技能驱动 `eu4` MCP 服务器，该服务器提供 HOI4 本体及已加载模组的只读 SQLite 索引
（由 `Eu4Indexer.Cli` 构建）。索引已解析模组覆盖和本地化解码，并包含派生的
**引用图**（`refs` 表）描述内容之间的连接关系。

如果 `eu4` 工具不可用，告知用户需要先构建索引并注册服务器（见文末"设置"），
不要用训练数据猜测。

**每次会话开始时调用 `describe_schema`** 查看可用的 `entity_type`、`ref_kind` 值、
语言列表和表结构。

## 数据库选择（多索引）

一次安装可包含多个索引——例如 HOI4 原版、Kaiserreich、TNO 等。
服务器启动时使用活跃索引。

- 如果用户的问题**提到了特定模组或 playset**，先调用 `list_databases`；
  如果有匹配的索引，在查询之前用 `select_database` 切换。选择在会话期间有效。
- 只有一个注册索引（或活跃索引已匹配）时，跳过这些工具直接查询。

## HOI4 内容连接关系（心智模型）

- **国策**是主要的内容结构，组织在**国策树**中（每个国家一个）。每个国策有一个
  `id`，属于一个 `focus_tree`（由树 id 键控，如 `german_focus`），并包含
  `prerequisite` 块列出需要的前置国策。国策可以是 `mutually_exclusive` ——
  只能选择其中一条路径。每个国策有 `available` 触发器和 `completion_reward` 效果块。
  每个国策有一个 `cost` 值（存储在 `script_nodes` 中）。参见 **规则 6** 了解如何
  将其转换为实际游戏天数——禁止将裸 `cost` 直接当作天数引用。
- **事件**用 `namespace.id` 作为键（如 `germany.1`）。可以是 `triggered_only`
  （由其他事件、国策或决议触发）或具有 `mean_time_to_happen`（自行触发）。
  每个事件有**选项（option）**；选项可携带 `trigger`（可见条件），
  其余部分是**效果（effect）**。
- **决议**由玩家点击触发，受 `available` 条件约束，效果在
  `complete_effect` / `remove_effect` 中。通常与国策树解锁关联。
- **理念**按类别分组（如 `political_advisor`、`tank_designer`、`economy`）。
  每个理念是类别内的命名块，携带 `modifier` 键值对。理念可有 `allowed` 和
  `visible` 条件。
- **标记**（Flag）**按作用域区分**：`country_flag` 和 `global_flag`
  （`set_country_flag` / `clr_country_flag` / `has_country_flag`）。HOI4
  不使用 province 或 ruler flag 如 EU4 那样。
- **变量**：`set_variable` / `check_variable` — 用于在事件和决议中追踪数值状态。
- **脚本化触发器/效果**是可复用的命名条件/效果；用 `resolve_symbol` 展开。
- **覆盖**：后加载的模组覆盖先加载的模组和本体，覆盖分三个层次
  （文件 / 实体 / 本地化）。工具默认返回**生效**（胜出）的行。
- **本地化**值包含 `§` 颜色代码和 `£` 图标；搜索针对剥离了标记的列进行，
  对中日韩文字友好。HOI4 按语言子目录存储本地化（`localisation/english/`、
  `localisation/simp_chinese/` 等），不同于 EU4 的文件名后缀惯例。
- **游戏 define**：控制游戏机制的数值常量存储在 `common/defines/*.lua` 中，
  索引在 `defines` 表中（通过 `v_effective_defines` 查询）。这些值在**原版与模组之间可能不同**——如
  Kaiserreich 可能修改国策时长、停战期限、战争支持度阈值或生产数值。
  **在陈述任何机制相关数值之前，务必先查询对应的 define。** 关键 define：

  | 回答关于… | 查询此 define |
  |---|---|
  | 国策完成时间 | `SELECT value FROM v_effective_defines WHERE define_key='NFocus.FOCUS_POINT_DAYS'`（默认 7 天/点） |
  | 和平/战争中国策进度 | `NFocus.FOCUS_PROGRESS_PEACE`、`NFocus.FOCUS_PROGRESS_WAR`（默认 1） |
  | 停战期限 | `NDiplomacy.BASE_TRUCE_PERIOD`（默认 180 天） |
  | 撕毁停战协议的政治力量消耗 | `NDiplomacy.TRUCE_BREAK_COST_PP`（默认 200） |
  | 制造战争借口的消耗 | `NDiplomacy.BASE_GENERATE_WARGOAL_DAILY_PP`（默认 0.2/天） |
  | 行为产生的世界紧张度 | `NDiplomacy.TENSION_SIZE_FACTOR`（默认 1.0）、`NDiplomacy.TENSION_DECAY_DAILY`（默认 0.005） |
  | 志愿军转移速度 | `NDiplomacy.VOLUNTEERS_TRANSFER_SPEED`（默认 14 天） |
  | 投降上限 | `NDiplomacy.BASE_SURRENDER_LEVEL`（默认 1.0） |
  | 最大保存国策进度 | `NFocus.MAX_SAVED_FOCUS_PROGRESS`（默认 10） |

  要**查找**未列在此表中的 define：
  `SELECT define_key, value FROM v_effective_defines WHERE define_key LIKE '%关键词%'`

### 作用域系统

HOI4 有着比 EU4 更丰富的作用域系统：
- `country` — 主要作用域（基于三位标签，如 `GER`、`SOV`）
- `state` — 国家内的地理区域
- `character` — 指挥官、顾问或政治人物
- `unit_leader` — 军事指挥官
- `operative` — 间谍
- `faction` — 联盟
- 其他作用域：`combat`、`air`、`naval`、`army`、`peace`、`politics`、`operation`、`raid`、`special_project`

在追踪引用时，注意作用域约束——同名 country_flag 的检查和 global_flag 的设置毫无关联。

## 工具

所有工具与 EU4 索引器共享。由于 schema 相同，同一套工具集在两个游戏中都可用。

| 工具 | 用途 |
|------|------|
| `describe_schema` | 数据字典 + 表结构。先从这里开始 |
| `explain_entity` | 一个实体：条件、效果、选项详情、入站 + 出站引用 |
| `what_triggers` | 反向查询：什么触发/引用了此实体 |
| `what_does_it_do` | 正向查询：此实体直接触发/设置/检查/应用/调用了什么 |
| `analyze_effects` | 效果级解释：提示文本、隐藏效果、触发事件、状态变化、下游后果 |
| `find_by_condition` | 哪些实体被某个标记/变量/脚本化触发器约束 |
| `trace_to_goal` | 反向链式查询候选操作序列以达到事件/标记/变量 |
| `find_dangling` | 被检查但从未设置的标记、被触发但未定义的事件——可能的 bug |
| `search_everything` | 跨类型搜索，当不知道内容类型时使用 |
| `search_localisation` | 文本搜索（剥离标记，CJK 友好） |
| `resolve_symbol` | 解释触发器/效果；展开脚本化定义 |
| `list_sources` / `get_overrides` | 加载顺序；谁覆盖了谁 |
| `read_query` | 后备通道：一条只读 SELECT 查询 |

## 回答约定（命名与本地化）——关键规则

这些规则是**强制的**。违反任何一条都会导致回答不可用。

### 规则 1：每个 id 必须以 `「名称」 (id)` 格式呈现

**本地化名称在前，id 用括号标注在后。** 玩家看到的是游戏内文本而非调试 id，
因此人类可读的名称是主要内容，id 是辅助参考。

禁止裸 id。这包括国策 id、事件 id、决议 id、理念键、标记、变量等任何脚本标识符。
列表中的每一项也必须附加名称。

```
错误：  germany.1 在 1939 年触发                    ← 裸 id
错误：  germany.1（德国宣战）                       ← id 在前，名称在括号中 — 反了
错误：  德国宣战 (germany.1)                        ← 自行翻译，非本地化文本
正确：  「德国向波兰宣战」 (germany.1)              ← 本地化名称在「」中在前，id 在 () 中
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
| **国策 / 决议 / 理念** | `explain_entity`（返回本地化标题）或 `search_localisation` 按 entity key 搜索 |
| **标记 / 变量** | 先尝试 `search_localisation`。很多标记是纯机械令牌，没有本地化行。**先检查再假设**——如果 `search_localisation` 无结果，说明该标记无文本。此时以标记名 + 简短功能说明呈现。不要编造翻译名称 |
| **国家 / 人物** | `search_localisation(text)` — 找出游戏内名称并逐字引述 |

始终批量查询：收集所有计划提到的 id，在**写回答之前**执行本地化查询。

### 规则 3：语言选择

HOI4 本地化在 `localisation/<language>/` 子目录中。对于每个 loc_key，
`v_effective_loc` 可能返回**多行**。选择对话语言（首选中文）对应的行。
不要盲取第一行。如果只有英文，使用英文。如果什么都没有，使用裸 id 并标注
"（游戏内无对应译名）"。

### 规则 4：绝不自行翻译专有名词

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

### 规则 5：正确转换脚本数值为显示数值

HOI4 脚本中许多值使用 **0–1 比例**（如 `add_stability = 0.1`），
但游戏 UI 显示为**百分比**（0%–100%）。脚本中的 `0.1` 在玩家视角中即 `+10%`。
在陈述效果时**必须**进行此转换，绝不能直接引用原始脚本数值。

**百分比制（脚本 × 100 → 显示 %）：**

| 类别 | 示例 key | 显示为 |
|------|---------|--------|
| 国家状态 | `add_stability`、`add_war_support`、`stability_factor`、`war_support_factor` | `+10 稳定度`（而非 `+0.1`） |
| 政治 | `political_power_factor`、`political_power_gain_factor`、`party_popularity` | 百分比 |
| 生产 | `production_efficiency_factor`、`factory_efficiency_gain_factor`、`consumer_goods_factor`、`dockyard_output_factor` | 百分比 |
| 科研 | `research_speed_factor` | 百分比 |
| 军事 | `division_speed_factor`、`org_factor`、`recovery_rate`、`training_time_factor`、`planning_speed_factor`、`entrenchment_factor`、`reinforce_rate_factor` | 百分比 |
| 战斗 | `soft_attack_factor`、`hard_attack_factor`、`breakthrough_factor`、`defence_factor`、`air_attack_factor`、`air_defence_factor`、`air_mission_factor`、`naval_strike_factor`、`naval_hit_chance_factor`、`sub_detection_factor`、`convoy_raiding_efficiency_factor` | 百分比 |
| 损耗 | `heat_attrition_factor`、`winter_attrition_factor`、`attrition_factor` | 百分比 |
| 抵抗 | `resistance`、`compliance`、`resistance_growth_factor`、`compliance_gain_factor`、`required_garrison_factor` | 百分比 |
| 其他全局 | `world_tension`、`surrender_limit`、`reliability`、`experience_gain_factor`、`lend_lease_tension_factor`、`send_volunteer_tension_factor`、`justify_wargoal_time_factor`、`naval_intel_factor`、`decryption_factor`、`encryption_factor` | 百分比 |

**绝对值（脚本值 = 显示值，不转换）：**

| 类别 | 示例 key |
|------|---------|
| 力量点数 | `add_political_power`、`add_command_power`——如 `120` → `+120 政治力量` |
| 经验 | `add_army_experience`、`add_navy_experience`、`add_air_experience` |
| 人力 | `add_manpower`、`manpower`——如 `10000` → `+10000 人力` |
| 装备 | `add_equipment`、`create_equipment_variant` |
| 建筑 | `add_building`、`building_slots`、`industrial_complex` |
| 资源 | `add_resource`、`resources` |
| 运输船 | `add_convoys`、`convoys` |
| 部队 | `create_unit`、`division_template` |
| 工厂 | `add_factory`、`add_civilian_factory`、`add_military_factory`、`add_naval_dockyard` |

**经验法则**：key 以 `_factor` 结尾，或修改的是游戏 UI 中以百分比条显示的属性 → 乘以 100 转换；其余直接引用。

### 规则 6：国策时长 = cost × FOCUS_POINT_DAYS（禁止将 cost 当作天数）

国策的 `cost` **不是**天数，而是国策点数。实际游戏时长 = `cost × FOCUS_POINT_DAYS` 天。
**在陈述任何国策时长之前，必须先查询 `FOCUS_POINT_DAYS`。** 查询语句：

```sql
SELECT value FROM v_effective_defines WHERE define_key = 'NFocus.FOCUS_POINT_DAYS';
```

- 原版 / 大部分模组：`7` → `cost = 10` 即 **70 天**
- 部分大修模组：`5` → `cost = 10` 即 **50 天**

这是国策解释中的**强制步骤**。如果在没有先执行上述查询的情况下说某国策"需要 N 天"，即违反本规则。

**错误**："该国策 cost 为 10，因此需要 10 天。"
**错误**："该国策需要 70 天。"（未查询直接假设）
**正确**："`SELECT value FROM v_effective_defines WHERE define_key='NFocus.FOCUS_POINT_DAYS'` → 7。cost = 10，因此该国策需要 **10 × 7 = 70 天**。"

## 工作流 1 — 解释一条国策路径

1. `explain_entity("focus", "<focus_id>")` 查看其条件、完成奖励和前置国策。
2. **强制步骤——查询国策时长**：在写回答之前，先执行
   `SELECT value FROM v_effective_defines WHERE define_key = 'NFocus.FOCUS_POINT_DAYS'`。
   然后在回答中以 `cost × N = M 天` 的形式陈述实际时长。
   禁止将裸 `cost` 数值直接当作天数引用。（参见 **规则 6**。）
3. 检查 `mutually_exclusive` 块——它们显示一旦选择此国策将被锁死的替代路径。
4. 用 `what_triggers` 对国策 id 查看它解锁了什么内容（启用的事件、解锁的决议等）。
5. 对于"我如何到达国策 X"，用 `trace_to_goal` 反向链式查询前置，或手动追踪
   `prerequisite` 引用。
6. 始终以 `「名称」 (id)` 格式呈现国策，并提及其所属的国策树。

## 工作流 2 — 解释事件

1. `explain_entity("event", "<event_id>")`。按每个节点的 `context` 阅读脚本树：
   `trigger` 节点是条件，`effect` 节点是发生的事。
2. 调用 `analyze_effects("event", "<event_id>")` 获取效果级分解。
3. 对于"什么触发了这个"，用 `what_triggers` 查看触发模式：triggered-only vs MTTH vs
   引擎钩子。
4. 本地化的标题/描述文本在返回的 `localisation` 中。

## 工作流 3 — 解释国家的理念配置

1. 用 `search_everything` 或 `read_query` 查找国家的理念类别：
   `SELECT entity_key FROM entities WHERE entity_type='idea' LIMIT 50`。
2. 对每个感兴趣的理念，用 `explain_entity("idea", "<key>")` 查看其修正值和条件。
3. 理念可能有 `allowed` 触发器（谁可以使用）和 `visible` 触发器。

## 工作流 4 — 查找 bug / 设计问题

1. 用 `find_dangling` 定位被检查但从未设置的标记，或被触发但未定义的事件。
2. 对国策树，检查不可达的国策（前置国策与上游国策互斥的情况）。
3. 对特定标记/变量用 `find_by_condition` 查看什么依赖它。
4. 用 `read_query` 对 `refs` 表追踪完整因果链。

## 边界与陷阱

- **始终以 `「名称」 (id)` 格式回答并引用本地化**，绝不使用裸 id 或自行翻译 —
  参见上文**回答约定**。
- `trace_to_goal` 是**有界的符号化反向链式推理，不是完整规划器**。它链式处理
  标记/变量/事件/决议/国策；其他所有内容都需要通过 `explain_entity` 验证。
- `find_dangling` 是**启发式的**：引擎设置的标记、动态命名目标、硬编码引擎事件
  会以误报形式出现。
- 结果**默认只返回生效**（覆盖胜出方）的行；模组冲突已被解析。
  使用 `get_overrides` 查看模组做了什么改动。
- **国策树规模很大**：德国树有约 200 个国策。当用户询问"德国的国策树"时，
  缩小到特定分支或路径。用 `search_localisation` 按游戏内名称查找国策。
- **互斥国策**：如果用户问为什么选了国策 X 之后不能选国策 Y，
  检查两个国策的 `mutually_exclusive` 块。
- **理念使用不同于 EU4 的修正**：HOI4 理念修正包含
  `production_factory_efficiency_gain_factor`、`political_power_factor`、
  `research_speed_factor` 等。按其 key 查询。

## 设置（如果服务器未连接）

构建索引，然后注册服务器：

```bash
eu4indexer index --game hoi4 \
  --game-dir /path/to/hoi4 --mod /path/to/mod \
  --config-dir /path/to/cwtools-hoi4-config --db hoi4.db

eu4indexer serve --db /path/to/hoi4.db
```
