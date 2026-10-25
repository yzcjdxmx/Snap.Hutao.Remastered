# 合成台组合快捷键（个人测试构建）

参考 ky3 的“无 / Ctrl / Alt / Shift”选择方式，在胡桃自己的原生模块中实现。
没有使用 ky3 的二进制文件；隐藏 UID 等既有实现保留。

原生源码固定为 SnapHutaoRemasteringProject/Snap.Hutao.Remastered.UnlockerIsland
提交 270016179ea5c01ad9b76ea9331739c200ac3eb7，MIT 许可证。
CI 下载该源码，应用 island.patch 和 CraftHotkeyBinding.h，再编译并打包 DLL。
原共享内存字段 CombineHotkey 的低 16 位为主键，高 16 位为修饰键编号，
未增加字段或修改结构布局。Windows 的 Alt 按键通过 WM_SYSKEYDOWN 处理。
精确匹配一种修饰键；长按产生的重复消息不会重复打开。

已限定为游戏 7.1.0 / 7.2.0；游戏升级后应先核对新版原生源码、更新固定提交、
适配范围与补丁，然后重新编译。不要直接扩展版本白名单。
尚未选择主键或修饰键为“无”时使用正常自动更新的原生模块。
更改主键或修饰键后重新启动游戏。新设置默认 Alt，原主键保留。

云端编译：手动运行个人 alpha.yml，包含 C++ 匹配测试、托管编码测试和 MSIX 打包。
本地编译该模块需要 Windows、Visual C++ v145、Windows SDK、vcpkg。
