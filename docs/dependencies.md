# CWTools and game rules

Checked against the canonical Git repositories on 2026-11-07. These are commit
pins, not floating branch downloads.

| Dependency | Previous pin | Current canonical head / selected pin | Result |
|---|---|---|---|
| CWTools | `e26ba840` (build fork) | upstream `b377453d`, fork `e26ba840` | Already current; the fork contains upstream plus the .NET build adaptation. |
| EU4 rules | `a85622d3` | `a85622d3` | Already current. |
| HOI4 rules | `f1460d13` | `297dd69c` | Updated across 44 commits / 48 files. |

Primary sources: [CWTools](https://github.com/cwtools/cwtools/commit/b377453dee803f9258be92cfc49896d09039702d),
[build fork](https://github.com/Golden-Pigeon/cwtools/commit/e26ba8402dee52c84e3eb7df6faf20cf410c5398),
[EU4 rules](https://github.com/cwtools/cwtools-eu4-config/commit/a85622d368bbb7afca938ed70fdd5eda44aec769),
[HOI4 changes](https://github.com/cwtools/cwtools-hoi4-config/compare/f1460d139036e75a8dd065dbd27b27415172654f...297dd69c42aabe10078a8b7ab7f2d07ee631d4ef).

## HOI4 feature coverage

| Rule changes | Index and query behavior |
|---|---|
| Custom doctrine folders | Generic `doctrine_folder` entities, explicit name localisation, `allowed` condition context, grand-doctrine folder edges and inbound lookup. |
| Subdoctrine multi-track membership and XOR | Both scalar and list `track` forms and all `xor` members become graph edges. Repeated `equipment_bonus` blocks retain their separate script nodes. |
| Expanded `set_sub_doctrine` | Scalar and block forms reference the assigned doctrine. The folder parameter is linked separately; the numeric slot index is not mistaken for a track identifier. |
| Doctrine/mastery checks and effects | `has_doctrine`, completion/track/folder checks, assignments and mastery folder/track parameters support forward/reverse graph queries; doctrine checks participate in `find_by_condition`, including negation. |
| New deployed/strategic-region triggers, faction-leader scope | Loaded from the pinned symbol catalog; condition blocks preserve their trigger context and symbol tags. |
| Ability one-time effects, trait add/remove effects and country conditions | Effect/condition contexts and scripted calls are captured. `analyze_effects` includes these effect blocks and their state changes. |
| Naval-leader combat hooks | New hook bodies are indexed, effects are analyzed, direct/weighted event lists and effect-fired events or flag writes are traceable. |
| Modifier categories and generated names | Repeated scope categories are merged with `|`. Concrete identifiers such as `production_cost_arms_factory_factor` match `<building>` templates for tagging and `resolve_symbol`; direct generic `modifier` blocks populate `modifier_values`. |
| New math functions, variable-expression forms and division fallback | The full ordered expression tree is retained, including `pow`, `root`, `mod`, `lerp`, trigonometry and `if_zero`. `which`, `var`, and shorthand assignment targets are traced. Effect analysis marks expression values explicitly instead of inventing runtime values or listing math operands as standalone effects. |
| Equipment mission/module forms, regimental support, unit model priority, advisor tooltips, technology conditions, faction colors, bookmark filters, raids and state history | Generic entities and recursive script nodes preserve the added fields, repeated entries and nested conditions; no closed field whitelist drops them. |
| Hidden-event and validation/cardinality corrections | Existing event extraction continues to retain hidden events and optional text. Cardinality and validation severity rules do not impose new indexing restrictions. |
| Graphics, GUI and sound rule fixes | These remain CWTools validation rules; the indexer's supported gameplay-script extension set is unchanged. They do not introduce new gameplay query primitives. |

`grand_doctrine` and `subdoctrine` share the reference target namespace
`doctrine`, matching the overloaded game trigger `has_doctrine`. Structural
relations are scoped to their entity type, so an unrelated `folder`/`track` key
cannot create a doctrine relation.

Run `setup --game hoi4` to install the new pin, then `refresh --name <index>`
to populate existing indexes with the additional derived data. The DSH manager
provides the equivalent rules-install and refresh operations. No table layout
changes or forced database-version migration are needed. The engine does not
evaluate runtime/save-game math expressions, simulate doctrine choices, or add
XOR branches together.

The synthetic HOI4 fixture and `Hoi4RulesUpdateTests` cover these query paths
without copyrighted game data. Run the complete test suite with both
`EU4_CONFIG_DIR` and `HOI4_CONFIG_DIR` pointing at the pinned rules; the HOI4
feature test requires the updated rules.
