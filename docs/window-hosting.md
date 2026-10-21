# 窗口宿主与生命周期

胡桃现有窗口使用 `Microsoft.UI.Xaml.Window + XamlWindowController`。玻璃设置窗口也沿用这条路径：液态玻璃是控件材质，不要求窗口继承 `WinUI3Package.WindowEx`。

## 两类对象不能当成同一个 Window

`WinUI3Package.WindowEx` 的 IDL 基类是 `Microsoft.UI.Xaml.Controls.ContentControl`。它持有一个真正的 `Window`，并转发部分窗口属性；它自身不是 `Microsoft.UI.Xaml.Window`，因此不满足 `WindowExtension.InitializeController<TWindow>` 的泛型约束。

也不能仅改成给 `WindowEx.Window` 调用 `InitializeController`：当前控制器从传入的 Window 本身读取 `IXamlWindowExtendContentIntoTitleBar`、`IXamlWindowHasInitSize`、`IXamlWindowClosedHandler` 等接口，并使用窗口实际类型生成位置恢复标识。若传入库持有的普通 Window，这些定义在外层宿主上的契约不会自动转发，关闭清理和每窗口位置恢复也会失配。

## 当前选择

- XAML 根节点与代码基类都使用原生 Window，保持单一窗口身份。
- `IXamlWindowHasInitSize` 提供初始尺寸；`OverlappedPresenter.PreferredMinimumWidth/Height` 提供最小尺寸，均通过 `ScaledSizeInt32` 缩放。
- `XamlWindowController` 统一管理标题栏、背景、窗口位置、卸载、关闭及服务作用域释放，不重复注册另一套窗口生命周期。
- 控件窗口的 `LiquidGlassProfiles.SelectionChanged` 仍通过 `IXamlWindowClosedHandler` 在真正关闭后撤销。

若未来确实要整体迁移到 WindowEx，应先定义明确的宿主适配边界，分别传递原生 Window、宿主契约和作用域所有权，并确定由哪一层管理 HWND 与关闭流程；不要强制转换、放宽现有泛型约束，或把两套完整控制器同时挂到同一窗口上。当前这两个设置窗口仅需尺寸和材质预览，没有必要为此引入第二套宿主架构。

验证（2026-10-20）：Debug/x64 解决方案增量编译通过，0 错误、176 警告；两个窗口重新生成的 XAML 基类均为 Microsoft.UI.Xaml.Window，不再有 CS0311。本轮没有修改 MainWindow.xaml，编译也没有复现之前的 WMC0060，因此不据旧日志改动其资源键。日志：`C:/Files/OpenNet/artifacts/hutao-glass-window-host-fix-20261020.log`。尚未启动窗口做运行验证。
