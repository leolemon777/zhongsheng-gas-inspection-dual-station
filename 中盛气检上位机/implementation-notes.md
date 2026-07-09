# 工位切换按钮 — 实现笔记

日期：2026-07-07

## 需求
- 两台上位机电脑，各运行一份本程序。上位机 A 固定用工位 1，上位机 B 固定用工位 2。
- 两台都连同一交换机、共用同一 IO 模块（192.168.0.7），但 Modbus 点位地址分开（工位1：DO0/DO1/AI0；工位2：DO2/DO3/AI1）。
- 主界面加显式按钮，可设定/切换本机当前工位；两台机器不互相干扰。

## 设计决策

### 切换逻辑复用现成入口
工位切换走 `IAppConfigurationStore.ApplyStation(stationId, recipe, settings)`（`AppConfigurationStore.cs:71`），它会：
1. 把 `ActiveStationId` 设为目标工位；
2. 通过 `ApplyActiveStation` 把共享单例 `HardwareSettings` 的 `StationName/InletOpenCoil/InletCloseCoil/PressureRegister/...` 更新为目标工位的点位；
3. 触发 `ActiveStationChanged` 事件（硬件页/参数页已订阅，自动刷新）。

切换后立即 `_configurationStore.Save(recipe, settings)` 持久化到 `Data/appsettings.json`，保证重启后仍是该工位（符合“固定一个工位”语义）。

### 绑定 HardwareSettings 单例，而非自维护状态
`HardwareSettings` 是 `[ObservableProperty]`（INPC）+ DI Singleton。`MainWindowViewModel` 直接暴露 `public HardwareSettings Hardware => _settings`，XAML 绑 `Hardware.ActiveStationId` / `Hardware.StationName`。
- 切换工位时 `HardwareSettings` 属性变化自动通知 UI 高亮，无需自维护 `CurrentStationId`、无需订阅事件、无需 `IDisposable`。
- 主界面按钮与「硬件连接」页的工位输入框天然双向同步（都基于同一单例）。

### Command 用 string 参数
`SwitchStation(string stationId)` 内部 `int.TryParse`。原因：WPF `CommandParameter="1"` 传的是 string，CommunityToolkit `RelayCommand<int>.Execute(object)` 会 `(int)parameter` 直接 cast，string→int 抛 `InvalidCastException`。用 string 参数避开，且 TryParse 保证健壮。

## 不互相干扰的依据（已核对代码）
- 写 DO 全是单点 `WriteSingleCoilAsync`（功能码 0x05），无 `WriteMultipleCoils`（`ZhongshengModbusTcpClient.cs`）。
- `SetDigitalOutputAsync`（`ZhongshengInspectionHardware.cs:116`）校验 `outputIndex` ∈ [0,3]，且只写当前工位配置的那一个线圈地址。
- IO 监控页对非当前工位的 DO 标记“其他工位/备用输出，锁定”（`IoMonitorPageViewModel.cs:248`）。
- 两台机器 Modbus 地址不重叠（工位1 DO0/DO1 + AI0；工位2 DO2/DO3 + AI1），各写各的线圈、各读各的寄存器 → 协议层天然不冲突，不需要跨机互锁。

## 改动文件
- `ViewModels/MainWindowViewModel.cs`：注入 `IAppConfigurationStore`/`HardwareSettings`/`GasInspectionRecipe`；新增 `Hardware` 属性与 `SwitchStationCommand`。
- `MainWindow.xaml`：导航栏标题与 `NavigationItems` 之间插入「本机工位」卡片 + `工位1`/`工位2` 两个按钮，`DataTrigger` 绑 `Hardware.ActiveStationId` 做选中高亮（绿底白字）。

## 验证
- `dotnet build -c Debug`：0 警告 0 错误。
- 运行时验证受阻：本机 Windows 应用控制策略（AppLocker/WDAC/Smart App Control，`0x800711C7`）阻止加载 `bin\Debug` 下未签名的散装 dll；非代码问题。改用 single-file publish 形态（与现有 dist 一致）验证。

## 部署要点 / 风险
- 两台机器分别配置：上位机 A 点「工位 1」并确认点位；上位机 B 点「工位 2」。切换即保存。
- 部署前确认两套工位的 Modbus 地址确实分开（默认值已分开，见 `AppConfigurationStore.Normalize`）。
- IO 模块需支持两台机器并发 TCP 连接（多数 Modbus TCP 模块支持）；若模块连接数受限，可能出现偶发连接失败。
- `appsettings.json` 各机器独立（各自 `Data/` 目录），不存在跨机配置覆盖。

## 工位驱动整体主题切换（绿 / 克莱因蓝）（2026-07-08 更新）
用户最终意图：**整个 UI 主题色随当前工位切换** —— 工位 1 = 绿色主题（原始配色），工位 2 = 克莱因蓝主题（`#002FA7`）。操作员看一眼界面色调即知当前工位。
- `MainWindow.xaml.cs`：`ApplyStationTheme(stationId)`，工位 1/2 各一套配色（Primary/PrimaryDark/PrimarySoft/Text）；订阅 `HardwareSettings.ActiveStationId` 的 INPC 通知，切换工位即换主题。
- **关键技术坑**：`Window.Resources` 里的 `SolidColorBrush` 经 XAML 加载后**被冻结（IsFrozen=true / 只读）**，直接改 `.Color` 会抛 `InvalidOperationException`（曾导致 `window.Show()` 前的异常被全局处理器吞掉 → 窗口不显示、无 crash.log，进程空转）。
- **解法**：XAML 中 `Brush.Primary/PrimaryDark/PrimarySoft/Text` 的所有引用从 `{StaticResource}` 改为 `{DynamicResource}`（`replace_all` 批量，约 70 处）；code-behind 改为**整体替换** `Resources[key] = new SolidColorBrush(color)`，DynamicResource 引用自动跟随、整个界面重绘换色。
- 工位切换 UI（灰底卡片 + 工位1/工位2 两按钮）保持不变；按钮高亮用 `Brush.Primary`，随主题自然变绿/蓝。
- 验证：`dotnet build` 0 警告 0 错误；本机运行确认：工位 1 显示绿色主题，点击「工位 2」后整界面切换为克莱因蓝（截图确认），工位状态同步切换。

## 双工位隔离 Review + 修复（2026-07-08）
目标：6 模块（参数设置/生产主页/IO监控/硬件连接/通讯日志/检测记录）在两台上位机完全独立，工位 1 绝不映射/同步到工位 2。部署：两台电脑共用一个电柜的 IO/模拟量模块，但用不同通道（点）。

代码层修复（R1/R2/R3）：
- **R3 检测记录**：`InspectionRecordStore.GetLatestAsync` 加 `WHERE station_id` 过滤；`RecordsPageViewModel` 注入 `HardwareSettings`、按当前工位查询、切换工位刷新、`IDisposable`。（原本只靠物理分库，现代码层也隔离）
- **R2 IO 监控**：`IoMonitorPageViewModel.Outputs` 只放当前工位两个阀门 DO（`InletOpenCoil`/`InletCloseCoil`），切换工位重建；`ApplyStates` 用真实 DO 地址映射。机器 A 不再显示工位 2 的 DO。
- **R1 阀门脉冲**：`WriteSingleCoilForDurationAsync` 改短连接（写 ON 断开 → 延时 → 重连写 OFF），脉冲期间不占 TCP 连接，两台机器共用模块不互相阻塞（不再依赖模块并发连接数）。

已确认隔离正确（无需改）：写 DO 全是单点 0x05（无批量写）+ 校验 [0,3]；压力读取只读当前工位 `PressureRegister`；手动写 DO 锁定非当前工位；配置/记录/日志各机器本地文件/db。

待用户定夺：
- **R4**：`Normalize` 每台机器配置文件含工位 1+2（一用一默认），不互染但不纯粹；要「机器 A 完全只知工位 1」需重构配置模型（大改）。
- **R5**：检测运行中切工位无防护（实际不发生），可选加「检测中禁切」。
- **硬件**：R1 已用短连接规避并发连接依赖；但两台同时操作时模块连接数仍影响吞吐，建议确认模块规格。

待清理（质量）：R1 改造后 `SendSingleCoilOnStreamAsync` / `TrySendSingleCoilOffAsync` 为 dead code，建议删除。

验证：`dotnet build` 0 警告 0 错误（R1/R2/R3）。
