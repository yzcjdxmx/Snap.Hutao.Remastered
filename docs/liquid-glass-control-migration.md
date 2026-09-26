# 液态玻璃控件切换与参数设置

## 当前入口与持久化

OpenNet 的“外观 → 液态玻璃控件与效果”和胡桃的“设置 → 外观 → 液态玻璃控件与效果”提供相同选项：

- 按类型启用：按钮、切换按钮、超链接按钮、复选框、单选按钮、Slider、文本输入、搜索输入、密码输入、ComboBox、ToggleSwitch、卡片与表面。
- 光学参数：总体强度、模糊、折射、色散、高光、放大，均为 0–2 倍。
- 默认 1 倍保留各控件原本的预设。总体强度作用于折射、色散、高光与放大；模糊使用独立倍数。
- 类型开关会切换实际视觉控件。总体材质为默认时，所有类型均使用原生分支。

OpenNet 存储于 AppSettingsDatabase 的 `liquid_glass` 分类；胡桃使用 LocalSetting 的 `LiquidGlass.*` 键。数值在写入 brush 前还会按库允许的范围限制。设置修改会同步到已打开的分支和已加载的玻璃控件。

## 共用状态

OpenNet 的资源键为 `MaterialControlState`，类型为 `OpenNet.UI.Xaml.Control.Theme.MaterialControlState`。胡桃的资源键为 `ControlMaterialState`，类型为 `Snap.Hutao.Remastered.UI.Xaml.Control.Theme.ControlMaterialState`。

每种类型都有 `ButtonEnabled`、`ButtonMaterial` 这样的属性。Enabled 是持久化 Bool；Material 是计算得到的 Int32，0 表示原生，1 表示玻璃。绑定应使用资源作为 Source，避免列表项、弹窗或内层 DataContext 改变材质来源。

```xml
<essential:SwitchPresenter
    IsTabStop="False"
    HorizontalContentAlignment="Stretch"
    Value="{Binding SliderMaterial, Source={StaticResource MaterialControlState}, Mode=OneWay}">
    <essential:Case>
        <essential:Case.Value><x:Int32>0</x:Int32></essential:Case.Value>
        <Slider Value="{x:Bind ViewModel.Value, Mode=TwoWay}" />
    </essential:Case>
    <essential:Case>
        <essential:Case.Value><x:Int32>1</x:Int32></essential:Case.Value>
        <liquid:LiquidGlassSlider
            theme:MaterialControlState.IsManaged="True"
            Value="{x:Bind ViewModel.Value, Mode=TwoWay}" />
    </essential:Case>
</essential:SwitchPresenter>
```

胡桃使用同样结构，将命名空间及资源键替换成自己的类型。Case.Value 必须是明确的 Int32，不能把字符串 `"0"` 当作整数。

## 业务绑定与命名控件

有 ViewModel 的普通使用点，两支绑定同一个业务属性。库 1.1.351 的玻璃 ToggleSwitch 已提供 IsOn、Header、OnContent、OffContent 等依赖属性，支持 TwoWay Binding。

原有代码直接读取、初始化或更新的命名控件，保留原生命名实例作为原有业务入口；玻璃分支通过 NativeControl 和 DP 绑定接入这个入口。原生实例位于未选中的 Case 中，不参与视觉布局。其 DataContext 和 RequestedTheme 显式跟随 Presenter，避免断开视觉树后丢失数据源或主题。

必须同时同步内容、选择值、启用状态及业务代码会改变的属性。例如多行 TextBox 还需要 AcceptsReturn、TextWrapping、MaxLength 和尺寸约束；不能只同步 Text。

ToggleSwitch 的 Toggled、Slider 的 ValueChanged 等原有处理方法只保留在业务入口，玻璃值变化通过 TwoWay 绑定进入它，避免重复执行确认弹窗或保存逻辑。Button 的 Click 仍使用同一个兼容处理方法。

命名控件的 Visibility 由外层 Presenter 绑定，以便原有代码隐藏控件时两个分支都退出布局。Presenter 本身不增加 Tab 停靠。教学提示目标应指向外层 Presenter，焦点操作应指向当前可见的输入控件。

## 独立 brush 与卸载

每个玻璃控件保持库创建的独立 GlassBrush。应用状态只是参数源，不把一个动态 brush 分配给多个控件。

`IsManaged` 在 Loaded 时取得该控件的 brush 并保存原始预设。参数更新始终从这份预设计算，而不是对上次已经缩放的结果继续相乘。Unloaded 只移除跟踪，不修改正在断开的 Composition 属性。

预设缓存按 brush 身份弱关联保存，不能随着控件 Unloaded 一起删除：切换材质或返回缓存页面可能复用同一个 brush。再次 Loaded 必须复用首次记录的基准；只有换成新 brush 才记录新的预设。已卸载控件不参与参数更新，重新加载时再应用当前设置。

已有语义卡片画刷保留原来的应用/页面资源生命周期。CardEnabled 和光学参数会应用到这些表面；窗口的 Mica、Acrylic、自定义 backdrop 类型仍由原来的窗口外观设置负责。

## 强类型属性与自定义卡片

InfoBar.ActionButton 接受 Button，不能放 SwitchPresenter。OpenNet 使用 XamlToolkit SwitchConverter，胡桃使用 ControlMaterialSelector；它们从两个 Button 实例中选择一个赋给 ActionButton。

胡桃七张首页卡片由 MaterialCardButton 宿主承载。宿主内部使用 SwitchPresenter，在两个按钮间转移同一个 Body 内容对象，先清空旧按钮的 Content 再赋给新按钮，防止重复创建 ScrollViewer、绑定、加载命令或同一元素同时有两个父级。

动态创建的控件也应使用 Presenter 工厂。OpenNet 提供 CreateTextBoxPresenter、CreateRadioButtonPresenter；胡桃提供 CreateButtonPresenter。调用者继续从原有状态实例读值并接入同一事件处理方法。

## 映射边界

当前应用中具有直接库对应类型的控件都接入上述选择方式；搜索输入使用 LiquidGlassSearchBox。AppBarButton、RadioButtons 聚合控件、NumberBox、TabView 以及 token 输入等自定义模板内部组件没有可直接替换的同契约类型，保持现有组件。不要把普通列表当成 LiquidGlassTabBar，或把模板中有名称契约的原生子控件再包成 Presenter。

新增相关控件时，同时检查两支的命令、输入值、可见性、焦点、主题和命名引用。本文所述变更只进行了源码结构及引用检查；依用户要求未执行编译、单元测试或运行烟测。

运行时仍待确认的交互包括：输入或选择后切换材质是否保留值；重复切换和导航返回后强度是否保持不变；原有命令是否只执行一次；搜索快捷键是否聚焦当前可见输入框；修改参数后已打开窗口是否同步。XML 可解析仅证明文件结构可读，不证明 XAML 编译、绑定解析或实际运行正确。
